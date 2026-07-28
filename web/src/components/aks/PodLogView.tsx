import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import {
  Play,
  Pause,
  ChevronUp,
  ChevronDown,
  ArrowDown,
  Download,
  ClipboardCopy,
  Trash2,
  Search,
} from "lucide-react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import { getLogLineClass } from "@/lib/logLevel";

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

type LogRange = "5m" | "10m" | "1h" | "all" | "previous";

interface LogLine {
  line: string;
  cls: string;
}

const rangeOptions: { label: string; value: LogRange; since?: number }[] = [
  { label: "Last 5m", value: "5m", since: 300 },
  { label: "Last 10m", value: "10m", since: 600 },
  { label: "Last 1h", value: "1h", since: 3600 },
  { label: "All", value: "all" },
  { label: "Previous container", value: "previous" },
];

export function PodLogView({ ns, podName, containers = [], onClose }: PodLogViewProps) {
  const [container, setContainer] = useState(containers[0] ?? "");
  const [range, setRange] = useState<LogRange>("5m");
  const [isLive, setIsLive] = useState(true);
  const [textFilter, setTextFilter] = useState("");
  const [lines, setLines] = useState<string[]>([]);
  const [paused, setPaused] = useState(false);
  const [pageFromNewest, setPageFromNewest] = useState(0);
  const [frozenFilteredCount, setFrozenFilteredCount] = useState<number | null>(null);
  const [pendingNewLineCount, setPendingNewLineCount] = useState(0);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const requestedTailLines = TAIL_INITIAL;
  const [error, setError] = useState<string | null>(null);

  const scrollRef = useRef<HTMLDivElement>(null);
  const eventSourceRef = useRef<EventSource | null>(null);
  const bufferRef = useRef<string[]>([]);
  const pendingRef = useRef(0);
  const pausedRef = useRef(paused);
  const pageFromNewestRef = useRef(pageFromNewest);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  useEffect(() => {
    pageFromNewestRef.current = pageFromNewest;
  }, [pageFromNewest]);

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
    () => `${ns}|${podName}|${container}|${range}|${isLive}|${requestedTailLines}`,
    [ns, podName, container, range, isLive, requestedTailLines],
  );

  const startStream = useCallback(
    (preserveView = false, forceLive?: boolean) => {
      stopStream();
      setError(null);
      setIsStreaming(true);
      if (!preserveView) {
        setLines([]);
        bufferRef.current = [];
        pendingRef.current = 0;
        setPendingNewLineCount(0);
        setPageFromNewest(0);
        setFrozenFilteredCount(null);
      }

      const follow = forceLive ?? isLive;
      const params = new URLSearchParams({
        follow: String(follow),
        tail: String(range === "all" ? requestedTailLines : 0),
      });
      if (container) params.set("container", container);
      const selected = rangeOptions.find((r) => r.value === range);
      if (selected?.since) params.set("sinceSeconds", String(selected.since));
      params.set("previousContainer", String(range === "previous"));

      const url = `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${podName}/logs/stream?${params}`;
      const es = new EventSource(url);
      eventSourceRef.current = es;

      es.onmessage = (e) => {
        if (pausedRef.current || pageFromNewestRef.current > 0) {
          pendingRef.current += 1;
        }
        bufferRef.current.push(e.data);
        if (bufferRef.current.length > MAX_BUFFER) {
          bufferRef.current = bufferRef.current.slice(-MAX_BUFFER);
        }
      };

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
    [ns, podName, container, range, isLive, requestedTailLines, stopStream],
  );

  // Restart when the stream signature changes.
  useEffect(() => {
    if (ns && podName) {
      startStream();
    }
    return () => {
      stopStream();
    };
  }, [streamSignature, startStream, stopStream]);

  // Render timer: flush buffered lines and pending count at ~10 fps.
  useEffect(() => {
    const id = setInterval(() => {
      const buffered = bufferRef.current;
      if (buffered.length !== lines.length || pendingRef.current !== pendingNewLineCount) {
        setLines([...buffered]);
        setPendingNewLineCount(pendingRef.current);
      }
    }, 100);
    return () => clearInterval(id);
  }, [lines.length, pendingNewLineCount]);

  const filteredLines = useMemo<LogLine[]>(() => {
    const term = textFilter.trim().toLowerCase();
    const source = term
      ? lines.filter((l) => l.toLowerCase().includes(term))
      : lines;
    return source.map((line) => ({ line, cls: getLogLineClass(line) }));
  }, [lines, textFilter]);

  const isWindowFrozen = paused || pageFromNewest > 0;
  const visibleTotal =
    isWindowFrozen && frozenFilteredCount !== null
      ? Math.min(frozenFilteredCount, filteredLines.length)
      : filteredLines.length;
  const maxPage = Math.max(0, Math.ceil(visibleTotal / VISIBLE) - 1);
  const safePage = Math.min(pageFromNewest, maxPage);
  const visibleEnd = Math.max(0, visibleTotal - safePage * VISIBLE);
  const visibleStart = Math.max(0, visibleEnd - VISIBLE);
  const visibleLines = filteredLines.slice(visibleStart, visibleEnd);

  const pending = Math.max(0, filteredLines.length - visibleTotal);

  // Auto-scroll to bottom when live and at the newest page.
  useEffect(() => {
    if (!paused && safePage === 0 && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [lines, paused, safePage, filteredLines.length]);

  const windowSummary =
    visibleTotal === 0
      ? "0 lines"
      : `Showing ${visibleStart + 1}-${Math.min(visibleEnd, visibleTotal)} of ${visibleTotal}`;
  const bufferSummary = `Buffer holding ${lines.length >= MAX_BUFFER ? `newest ${lines.length}` : lines.length} lines`;

  const followState =
    isLive && isStreaming && !paused && safePage === 0
      ? "live"
      : paused
        ? "paused"
        : "historical";
  const followText =
    followState === "live"
      ? "Live • tailing"
      : followState === "paused"
        ? `Paused at line ${visibleEnd}`
        : "Historical (older loaded)";

  const captureAnchor = useCallback(() => {
    setFrozenFilteredCount(filteredLines.length);
  }, [filteredLines.length]);

  const releaseAnchor = useCallback(() => {
    setFrozenFilteredCount(null);
  }, []);

  const handleTogglePause = () => {
    setPaused((p) => {
      if (!p) {
        captureAnchor();
      } else {
        releaseAnchor();
        setPageFromNewest(0);
      }
      return !p;
    });
  };

  const handleOlder = () => {
    if (safePage >= maxPage) return;
    if (!isWindowFrozen) captureAnchor();
    setPageFromNewest((p) => p + 1);
  };

  const handleNewer = () => {
    setPageFromNewest((p) => {
      const next = Math.max(0, p - 1);
      if (next === 0 && !paused) {
        releaseAnchor();
      }
      return next;
    });
  };

  const handleJumpToLatest = () => {
    setPageFromNewest(0);
    if (paused) {
      captureAnchor();
    } else {
      releaseAnchor();
    }
  };

  const handleGoLive = () => {
    if (range === "previous") return;
    setPaused(false);
    releaseAnchor();
    setPageFromNewest(0);
    setPendingNewLineCount(0);
    pendingRef.current = 0;
    setIsLive(true);
    if (!isStreaming || !isLive) {
      startStream(false, true);
    }
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  };

  const handleClear = () => {
    bufferRef.current = [];
    setLines([]);
    pendingRef.current = 0;
    setPendingNewLineCount(0);
    setPageFromNewest(0);
    releaseAnchor();
  };

  const handleCopyVisible = async () => {
    if (visibleLines.length === 0) return;
    try {
      await navigator.clipboard.writeText(visibleLines.map((l) => l.line).join("\n"));
    } catch {
      // ignore
    }
  };

  const handleExportAll = () => {
    if (isExporting) return;
    setIsExporting(true);
    const collected: string[] = [];
    const params = new URLSearchParams({
      follow: "false",
      tail: String(range === "all" ? HISTORY_CAP : HISTORY_CAP),
    });
    if (container) params.set("container", container);
    const selected = rangeOptions.find((r) => r.value === range);
    if (selected?.since) params.set("sinceSeconds", String(selected.since));
    params.set("previousContainer", String(range === "previous"));

    const url = `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${podName}/logs/stream?${params}`;
    const es = new EventSource(url);

    es.onmessage = (e) => {
      collected.push(e.data);
    };

    es.addEventListener("done", () => {
      es.close();
      setIsExporting(false);
      const blob = new Blob([collected.join("\n")], { type: "text/plain" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = `${podName}-${container || "logs"}.log`;
      a.click();
      URL.revokeObjectURL(a.href);
    });

    es.onerror = () => {
      es.close();
      setIsExporting(false);
    };
  };

  const canShowOlder = safePage < maxPage;
  const canShowNewer = safePage > 0;
  const canJumpToLatest = paused || safePage > 0 || pending > 0;
  const isPrevious = range === "previous";

  return (
    <div className="flex h-full flex-col" data-testid="pod-log-view">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2 border-b bg-card px-3 py-2">
        {/* Live toggle */}
        <label
          className="flex items-center gap-1.5 text-xs whitespace-nowrap"
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
              if (!next) {
                stopStream();
              }
            }}
          />
          <span
            className={`h-2 w-2 rounded-full ${isLive && isStreaming ? "bg-green-500" : "bg-muted-foreground"}`}
          />
          Live
        </label>

        {/* Container selector */}
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

        {/* Range select */}
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

        {/* Filter */}
        <div className="relative flex-1 min-w-[120px]">
          <Search className="absolute left-2 top-1/2 h-3 w-3 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            value={textFilter}
            onChange={(e) => {
              setTextFilter(e.target.value);
              if (isWindowFrozen) captureAnchor();
            }}
            placeholder="Filter..."
            className="w-full rounded border bg-background py-1 pl-7 pr-2 text-xs"
            data-testid="log-filter-input"
          />
        </div>

        {/* Window summary */}
        <span className="text-xs text-muted-foreground whitespace-nowrap" data-testid="log-line-count">
          {windowSummary}
        </span>

        {/* Action groups */}
        <div className="ml-auto flex flex-wrap items-center gap-1">
          {/* Navigate */}
          <div className="flex items-center gap-0.5">
            <button
              onClick={handleOlder}
              disabled={!canShowOlder}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
              title="Show older buffered lines"
              data-testid="log-older-btn"
            >
              <ChevronUp className="h-3 w-3" /> Older
            </button>
            <button
              onClick={handleNewer}
              disabled={!canShowNewer}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
              title="Show newer buffered lines"
              data-testid="log-newer-btn"
            >
              <ChevronDown className="h-3 w-3" /> Newer
            </button>
            <button
              onClick={handleJumpToLatest}
              disabled={!canJumpToLatest}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
              title="Jump back to the newest buffered lines"
              data-testid="log-latest-btn"
            >
              <ArrowDown className="h-3 w-3" />
              {pending > 0 ? `Latest (${pending})` : "Latest"}
            </button>
          </div>

          <span className="mx-1 h-4 w-px bg-border" />

          {/* Live state */}
          <button
            onClick={handleGoLive}
            disabled={isPrevious || (isLive && isStreaming && !paused && safePage === 0)}
            className={`flex items-center gap-1 rounded px-2 py-1 text-xs ${
              isLive && isStreaming && !paused && safePage === 0
                ? "border border-green-500/50 text-green-500"
                : "border bg-primary text-primary-foreground hover:bg-primary/90"
            }`}
            title="Resume live tailing and jump to the newest lines"
            data-testid="log-go-live-btn"
          >
            <Play className="h-3 w-3" />
            {isLive && isStreaming && !paused ? "Live" : "Go to live"}
          </button>

          {!isPrevious && (isLive || isStreaming) && (
            <button
              onClick={handleTogglePause}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
              title={paused ? "Resume tailing" : "Pause tailing"}
              data-testid="log-pause-btn"
            >
              {paused ? <Play className="h-3 w-3" /> : <Pause className="h-3 w-3" />}
              {paused ? "Resume" : "Pause"}
            </button>
          )}

          <span className="mx-1 h-4 w-px bg-border" />

          {/* Data */}
          <button
            onClick={handleCopyVisible}
            disabled={visibleLines.length === 0}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            title="Copy the currently visible log window"
            data-testid="log-copy-visible-btn"
          >
            <ClipboardCopy className="h-3 w-3" /> Copy visible
          </button>
          <button
            onClick={handleExportAll}
            disabled={isExporting}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            title="Export the full pod log stream"
            data-testid="log-export-btn"
          >
            <Download className="h-3 w-3" />
            {isExporting ? "Exporting…" : "Export all"}
          </button>
          <button
            onClick={handleClear}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            title="Clear log buffer"
            data-testid="log-clear-btn"
          >
            <Trash2 className="h-3 w-3" /> Clear
          </button>
        </div>

        {onClose && (
          <button
            onClick={onClose}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
          >
            Close
          </button>
        )}
      </div>

      {/* Status bar */}
      <div className="flex flex-wrap items-center gap-3 border-b bg-card px-3 py-1 text-xs text-muted-foreground">
        <span>{bufferSummary}</span>
        {paused && (
          <span>Paused: new lines are buffered until you resume or jump latest</span>
        )}
        {!paused && safePage > 0 && (
          <span>Browsing history: latest lines are buffered in the background</span>
        )}
        {pending > 0 && canJumpToLatest && (
          <button
            onClick={handleJumpToLatest}
            className="rounded-full border px-2 py-0.5 text-xs hover:border-primary hover:text-primary"
          >
            {pending === 1 ? "1 newer line buffered" : `${pending} newer lines buffered`}
          </button>
        )}
        {error && <span className="text-destructive">{error}</span>}
      </div>

      {/* Log output */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-auto bg-background p-2 font-mono text-xs"
        data-testid="log-output"
      >
        {visibleLines.length === 0 ? (
          <div className="flex h-full items-center justify-center text-muted-foreground">
            {isStreaming ? "Waiting for logs..." : error ? "No logs available" : "No log lines yet"}
          </div>
        ) : (
          visibleLines.map((entry, i) => (
            <div
              key={visibleStart + i}
              className={`log-line whitespace-pre-wrap break-all ${entry.cls}`}
              data-testid={`log-line-${visibleStart + i}`}
            >
              {entry.line}
            </div>
          ))
        )}
      </div>

      {/* Footer */}
      <div
        className={`flex items-center gap-2 border-t bg-card px-3 py-1 text-xs ${
          followState === "live"
            ? "text-green-500"
            : followState === "paused"
              ? "text-yellow-500"
              : "text-muted-foreground"
        }`}
      >
        <span
          className={`h-2 w-2 rounded-full ${
            followState === "live"
              ? "bg-green-500 animate-pulse"
              : followState === "paused"
                ? "bg-yellow-500"
                : "bg-muted-foreground"
          }`}
        />
        <span>{followText}</span>
      </div>
    </div>
  );
}
