import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { X } from "lucide-react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import { mergeByTimestamp, type TimestampMode } from "@/lib/log-window";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import { LogToolbar } from "./shared/LogToolbar";
import { LogOutput } from "./shared/LogOutput";
import { useLogBuffer } from "./shared/useLogBuffer";
import { useLogWindow } from "./shared/useLogWindow";

interface Props {
  ns: string;
  pods: string[];
  onClose: () => void;
}

const VISIBLE = 200;
const MAX_BUFFER = 50_000;
const EXPORT_TAIL = 200_000;
const TIMESTAMP_PREF_KEY = "aks-log-timestamp-mode";

export function MultiPodLogView({ ns, pods, onClose }: Props) {
  // Every pod starts streaming. The user opened a correlation view for this exact set of
  // pods, so opening onto a dead panel that needs a click per pod before any log appears
  // inverts the intent — correlation is the default, deselecting is the exception. `pods`
  // is memoised off the URL param upstream, so this re-syncs only when the set itself
  // genuinely changes, not on every render.
  const [selectedPods, setSelectedPods] = useState<string[]>(pods);
  useEffect(() => {
    setSelectedPods(pods);
  }, [pods]);

  const [container, setContainer] = useState("");
  const [timestampMode, setTimestampMode] = useState<TimestampMode>(() =>
    loadViewPreference<TimestampMode>(TIMESTAMP_PREF_KEY, "time"),
  );
  const [isExporting, setIsExporting] = useState(false);

  const sourcesRef = useRef<Map<string, EventSource>>(new Map());
  const streamKeyRef = useRef(`${ns}::${container}`);
  const scrollRef = useRef<HTMLDivElement>(null);

  const buffer = useLogBuffer({ maxBuffer: MAX_BUFFER, frozen: false });
  const { push, clear } = buffer;

  // Correlation is the point of this view, and arrival order does not give it: network
  // jitter delivers lines out of order, so two events seconds apart can read as
  // simultaneous. Sorting by the pods' own timestamps is what makes the view honest.
  const ordered = useMemo(() => mergeByTimestamp(buffer.entries), [buffer.entries]);
  const win = useLogWindow(ordered, VISIBLE);

  const applyTimestampMode = (mode: TimestampMode) => {
    setTimestampMode(mode);
    saveViewPreference(TIMESTAMP_PREF_KEY, mode);
  };

  // One SSE stream per selected pod (matches the sidecar's actual log-stream contract —
  // it's SSE, not a WebSocket). Deselecting a pod closes just its stream; selecting one
  // opens a new one alongside the rest, so logs from multiple pods interleave for
  // correlation. Changing namespace/container tears down and reopens every stream since
  // the filter itself changed.
  useEffect(() => {
    const sources = sourcesRef.current;
    const streamKey = `${ns}::${container}`;
    const filterChanged = streamKeyRef.current !== streamKey;
    streamKeyRef.current = streamKey;

    if (filterChanged) {
      for (const es of sources.values()) es.close();
      sources.clear();
      clear();
    } else {
      for (const [pod, es] of sources) {
        if (!selectedPods.includes(pod)) {
          es.close();
          sources.delete(pod);
        }
      }
    }

    for (const pod of selectedPods) {
      if (sources.has(pod)) continue;
      // `timestamps` asks the container runtime for the real emission time. Without it
      // the only clock available is the browser's arrival time, which is what made the
      // correlation misleading.
      const params = new URLSearchParams({ tail: "100", follow: "true", timestamps: "true" });
      if (container) params.set("container", container);
      const es = new EventSource(
        `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${pod}/logs/stream?${params}`,
      );
      es.onmessage = (e) => push(e.data, pod);
      es.addEventListener("done", () => {
        es.close();
        sources.delete(pod);
      });
      es.onerror = () => {
        es.close();
        sources.delete(pod);
      };
      sources.set(pod, es);
    }
  }, [selectedPods, ns, container, push, clear]);

  // Full teardown on unmount: an SSE stream left open after the panel closes keeps
  // delivering into a dead component.
  useEffect(() => {
    return () => {
      for (const es of sourcesRef.current.values()) es.close();
      sourcesRef.current.clear();
    };
  }, []);

  // Stick to the bottom while following, exactly as the single-pod view does.
  useEffect(() => {
    if (!win.frozen && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [ordered.length, win.frozen]);

  const togglePod = (pod: string) => {
    setSelectedPods((prev) =>
      prev.includes(pod) ? prev.filter((p) => p !== pod) : [...prev, pod],
    );
  };

  const handleCopyVisible = useCallback(async () => {
    if (win.visible.length === 0) return;
    try {
      await navigator.clipboard.writeText(
        win.visible.map((e) => `${e.ts ?? ""} ${e.pod}: ${e.text}`.trim()).join("\n"),
      );
    } catch {
      // Clipboard can be unavailable; nothing useful to tell the user.
    }
  }, [win.visible]);

  // Exports every selected pod, not just what is on screen, and tags each line with its
  // pod so the merged file is still readable.
  const handleExport = useCallback(() => {
    if (isExporting || selectedPods.length === 0) return;
    setIsExporting(true);

    const collected: { pod: string; line: string }[] = [];
    let remaining = selectedPods.length;

    const finish = () => {
      remaining -= 1;
      if (remaining > 0) return;
      setIsExporting(false);
      const body = collected.map((c) => `${c.pod}: ${c.line}`).join("\n");
      const blob = new Blob([body], { type: "text/plain" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = `${ns}-multi-pod.log`;
      a.click();
      URL.revokeObjectURL(a.href);
    };

    for (const pod of selectedPods) {
      const params = new URLSearchParams({
        tail: String(EXPORT_TAIL),
        follow: "false",
        timestamps: "true",
      });
      if (container) params.set("container", container);
      const es = new EventSource(
        `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${pod}/logs/stream?${params}`,
      );
      es.onmessage = (e) => collected.push({ pod, line: e.data });
      es.addEventListener("done", () => {
        es.close();
        finish();
      });
      es.onerror = () => {
        es.close();
        finish();
      };
    }
  }, [isExporting, selectedPods, ns, container]);

  const handleClear = useCallback(() => {
    clear();
    win.reset();
  }, [clear, win]);

  return (
    <div className="flex h-full flex-col" data-testid="multi-pod-log-view">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="text-sm font-semibold">Multi-Pod Log Correlation</h2>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="multi-pod-log-close">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="border-b px-4 py-2">
        <span className="text-xs text-muted-foreground">Pods being correlated:</span>
        <div className="mt-1 flex flex-wrap gap-1">
          {pods.map((pod) => (
            <button
              key={pod}
              onClick={() => togglePod(pod)}
              className={`rounded px-2 py-1 text-xs ${selectedPods.includes(pod) ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid={`multi-pod-toggle-${pod}`}
            >
              {pod}
            </button>
          ))}
        </div>
        <div className="mt-2 flex items-center gap-2">
          <input
            type="text"
            value={container}
            onChange={(e) => setContainer(e.target.value)}
            placeholder="Container name (optional)"
            className="rounded border bg-background px-2 py-1 text-xs"
            data-testid="multi-pod-container-input"
          />
        </div>
      </div>

      <LogToolbar
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
        onExport={handleExport}
        onClear={handleClear}
        isExporting={isExporting}
      />

      <LogOutput
        ref={scrollRef}
        entries={win.visible}
        startIndex={win.visibleStart}
        timestampMode={timestampMode}
        showPod
        emptyMessage={
          selectedPods.length === 0 ? "Select pods to start streaming logs" : "Connecting..."
        }
        testId="multi-pod-log-output"
      />
    </div>
  );
}
