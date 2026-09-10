import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import type { TimestampMode } from "@/lib/log-window";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import { LogToolbar } from "./shared/LogToolbar";
import { LogOutput } from "./shared/LogOutput";
import { useLogBuffer } from "./shared/useLogBuffer";
import { useLogWindow } from "./shared/useLogWindow";
import { rangeOptions, type LogRange } from "./shared/logRange";

interface PodLogViewProps {
  ns: string;
  podName: string;
  containers?: string[];
  onClose?: () => void;
}

const VISIBLE = 200;
const MAX_BUFFER = 50_000;
const TAIL_INITIAL = 2_000;
const HISTORY_CAP = 200_000;
const TIMESTAMP_PREF_KEY = "aks-log-timestamp-mode";

/// Builds the stream query shared by the live path and the export path, so the two can
/// never disagree about which slice of history they are asking for.
function streamParams(range: LogRange, container: string, tail: number, follow: boolean) {
  const params = new URLSearchParams({
    follow: String(follow),
    tail: String(tail),
    // Ask the container runtime for the real emission time rather than inferring one
    // from arrival, which network jitter makes unreliable.
    timestamps: "true",
  });
  if (container) params.set("container", container);
  const selected = rangeOptions.find((r) => r.value === range);
  if (selected?.since) params.set("sinceSeconds", String(selected.since));
  params.set("previousContainer", String(range === "previous"));
  return params;
}

export function PodLogView({ ns, podName, containers = [], onClose }: PodLogViewProps) {
  const [container, setContainer] = useState(containers[0] ?? "");
  const [range, setRange] = useState<LogRange>("5m");
  const [isLive, setIsLive] = useState(true);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [timestampMode, setTimestampMode] = useState<TimestampMode>(() =>
    loadViewPreference<TimestampMode>(TIMESTAMP_PREF_KEY, "time"),
  );

  const scrollRef = useRef<HTMLDivElement>(null);
  const eventSourceRef = useRef<EventSource | null>(null);

  // Paused/paged state has to reach the buffer so arrivals are counted as pending rather
  // than silently sliding the window, but the window is derived from the buffer — so the
  // frozen flag is mirrored through a ref to break the cycle.
  const frozenRef = useRef(false);
  const buffer = useLogBuffer({ maxBuffer: MAX_BUFFER, frozen: frozenRef.current });
  const win = useLogWindow(buffer.entries, VISIBLE);
  frozenRef.current = win.frozen;

  const { push, clear, resetPending } = buffer;

  const applyTimestampMode = (mode: TimestampMode) => {
    setTimestampMode(mode);
    saveViewPreference(TIMESTAMP_PREF_KEY, mode);
  };

  // Keep the selected container in sync with the pod's available containers.
  useEffect(() => {
    if (containers.length > 0 && (!container || !containers.includes(container))) {
      setContainer(containers[0]);
    }
  }, [containers, container]);

  const stopStream = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    setIsStreaming(false);
  }, []);

  const streamSignature = useMemo(
    () => `${ns}|${podName}|${container}|${range}|${isLive}`,
    [ns, podName, container, range, isLive],
  );

  const startStream = useCallback(
    (forceLive?: boolean) => {
      stopStream();
      setError(null);
      setIsStreaming(true);
      clear();

      const follow = forceLive ?? isLive;
      const params = streamParams(range, container, range === "all" ? TAIL_INITIAL : 0, follow);
      const es = new EventSource(
        `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${podName}/logs/stream?${params}`,
      );
      eventSourceRef.current = es;

      es.onmessage = (e) => push(e.data);

      es.addEventListener("done", () => {
        setIsStreaming(false);
        es.close();
        eventSourceRef.current = null;
      });

      es.onerror = () => {
        setIsStreaming(false);
        es.close();
        eventSourceRef.current = null;
        if (es.readyState === EventSource.CLOSED) {
          setError("Log stream closed unexpectedly.");
        }
      };
    },
    [ns, podName, container, range, isLive, stopStream, clear, push],
  );

  // Restart when the stream signature changes. Closing the source in the cleanup is what
  // stops a stream delivering into an unmounted component.
  useEffect(() => {
    if (ns && podName) startStream();
    return () => stopStream();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [streamSignature]);

  // Auto-scroll while following the newest lines.
  useEffect(() => {
    if (!win.frozen && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [buffer.entries.length, win.frozen]);

  const isPrevious = range === "previous";
  const isFollowing = isLive && isStreaming && !win.frozen;

  const followState = isFollowing ? "live" : win.paused ? "paused" : "historical";
  const followText =
    followState === "live"
      ? "Live • tailing"
      : followState === "paused"
        ? "Paused — new lines are buffered"
        : "Historical (older loaded)";

  const handleGoLive = useCallback(() => {
    if (isPrevious) return;
    win.reset();
    resetPending();
    setIsLive(true);
    if (!isStreaming || !isLive) {
      startStream(true);
    }
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [isPrevious, win, resetPending, isStreaming, isLive, startStream]);

  const handleClear = useCallback(() => {
    clear();
    win.reset();
  }, [clear, win]);

  const handleCopyVisible = useCallback(async () => {
    if (win.visible.length === 0) return;
    try {
      await navigator.clipboard.writeText(win.visible.map((e) => e.text).join("\n"));
    } catch {
      // Clipboard can be unavailable; nothing useful to tell the user.
    }
  }, [win.visible]);

  // Exports the full stream, not the on-screen window — the buffer is capped and the
  // point of an export is the part that scrolled away.
  const handleExportAll = useCallback(() => {
    if (isExporting) return;
    setIsExporting(true);

    const collected: string[] = [];
    const params = streamParams(range, container, HISTORY_CAP, false);
    const es = new EventSource(
      `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${podName}/logs/stream?${params}`,
    );

    es.onmessage = (e) => collected.push(e.data);

    const finish = () => {
      es.close();
      setIsExporting(false);
    };

    es.addEventListener("done", () => {
      finish();
      const blob = new Blob([collected.join("\n")], { type: "text/plain" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = `${podName}-${container || "logs"}.log`;
      a.click();
      URL.revokeObjectURL(a.href);
    });

    es.onerror = finish;
  }, [isExporting, range, container, ns, podName]);

  return (
    <div className="flex h-full flex-col" data-testid="pod-log-view">
      <LogToolbar
        leading={
          <>
            <label
              className="flex items-center gap-1.5 whitespace-nowrap text-xs"
              data-testid="log-live-toggle"
              title={isPrevious ? "Cannot tail previous container" : undefined}
            >
              <input
                type="checkbox"
                checked={isLive}
                disabled={isPrevious}
                onChange={(e) => {
                  const next = e.target.checked;
                  setIsLive(next);
                  if (!next) stopStream();
                }}
              />
              <span
                className={`h-2 w-2 rounded-full ${isLive && isStreaming ? "bg-success" : "bg-muted-foreground"}`}
              />
              Live
            </label>

            {containers.length > 1 && (
              <select
                value={container}
                onChange={(e) => setContainer(e.target.value)}
                className="rounded border bg-background px-2 py-1 text-xs"
                data-testid="log-container-select"
              >
                {containers.map((c) => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
            )}

            <select
              value={range}
              onChange={(e) => setRange(e.target.value as LogRange)}
              className="rounded border bg-background px-2 py-1 text-xs"
              data-testid="log-range-select"
            >
              {rangeOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </>
        }
        textFilter={win.textFilter}
        onTextFilterChange={win.setTextFilter}
        summary={win.summary}
        timestampMode={timestampMode}
        onTimestampModeChange={applyTimestampMode}
        paused={win.paused}
        onTogglePause={win.togglePause}
        canShowOlder={win.canShowOlder}
        canShowNewer={win.canShowNewer}
        canJumpToLatest={win.frozen || win.heldBack > 0}
        pendingCount={win.heldBack}
        onShowOlder={win.showOlder}
        onShowNewer={win.showNewer}
        onJumpToLatest={win.jumpToLatest}
        onCopyVisible={handleCopyVisible}
        onExport={handleExportAll}
        onClear={handleClear}
        isExporting={isExporting}
        disableFollowControls={isPrevious}
        goLive={{ onGoLive: handleGoLive, isFollowing, disabled: isPrevious }}
        trailing={
          onClose ? (
            <button onClick={onClose} className="rounded border px-2 py-1 text-xs hover:bg-accent">
              Close
            </button>
          ) : undefined
        }
      />

      {/* Status bar */}
      <div className="flex flex-wrap items-center gap-3 border-b bg-card px-3 py-1 text-xs text-muted-foreground">
        <span>
          Buffer holding{" "}
          {buffer.entries.length >= MAX_BUFFER
            ? `newest ${buffer.entries.length}`
            : buffer.entries.length}{" "}
          lines
        </span>
        {win.paused && <span>Paused: new lines are buffered until you resume or jump latest</span>}
        {!win.paused && win.canShowNewer && (
          <span>Browsing history: latest lines are buffered in the background</span>
        )}
        {win.heldBack > 0 && (
          <button
            onClick={win.jumpToLatest}
            className="rounded-full border px-2 py-0.5 text-xs hover:border-primary hover:text-primary"
          >
            {win.heldBack === 1 ? "1 newer line buffered" : `${win.heldBack} newer lines buffered`}
          </button>
        )}
        {error && <span className="text-destructive">{error}</span>}
      </div>

      <LogOutput
        ref={scrollRef}
        entries={win.visible}
        startIndex={win.visibleStart}
        timestampMode={timestampMode}
        emptyMessage={
          isStreaming ? "Waiting for logs..." : error ? "No logs available" : "No log lines yet"
        }
        testId="log-output"
      />

      {/* Footer */}
      <div
        className={`flex items-center gap-2 border-t bg-card px-3 py-1 text-xs ${
          followState === "live"
            ? "text-success"
            : followState === "paused"
              ? "text-warning"
              : "text-muted-foreground"
        }`}
      >
        <span
          className={`h-2 w-2 rounded-full ${
            followState === "live"
              ? "bg-success animate-pulse"
              : followState === "paused"
                ? "bg-warning"
                : "bg-muted-foreground"
          }`}
        />
        <span>{followText}</span>
      </div>
    </div>
  );
}
