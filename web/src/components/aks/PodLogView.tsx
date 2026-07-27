import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { Play, Pause, Square, Download, Search } from "lucide-react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import { getLogLineClass } from "@/lib/logLevel";

interface PodLogViewProps {
  ns: string;
  podName: string;
  containers?: string[];
  onClose?: () => void;
}

const MAX_LINES = 5000;
const PAGE_SIZE = 200;

export function PodLogView({ ns, podName, containers = [], onClose }: PodLogViewProps) {
  const [container, setContainer] = useState(containers[0] ?? "");
  const [tail, setTail] = useState(100);
  const [follow, setFollow] = useState(true);
  const [paused, setPaused] = useState(false);
  const [filter, setFilter] = useState("");
  const [sinceSeconds, setSinceSeconds] = useState<number | null>(null);
  const [previousContainer, setPreviousContainer] = useState(false);
  const [lines, setLines] = useState<string[]>([]);
  const [streaming, setStreaming] = useState(false);
  const [page, setPage] = useState(0);
  const eventSourceRef = useRef<EventSource | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const pausedRef = useRef(false);
  const bufferRef = useRef<string[]>([]);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  const stopStream = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    setStreaming(false);
  }, []);

  const startStream = useCallback(() => {
    stopStream();
    setLines([]);
    bufferRef.current = [];
    setPage(0);

    const params = new URLSearchParams({
      tail: String(tail),
      follow: String(follow),
    });
    if (container) params.set("container", container);
    if (sinceSeconds !== null) params.set("sinceSeconds", String(sinceSeconds));
    if (previousContainer) params.set("previousContainer", "true");

    const url = `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${podName}/logs/stream?${params}`;
    const es = new EventSource(url);
    eventSourceRef.current = es;
    setStreaming(true);

    es.onmessage = (e) => {
      if (!pausedRef.current) {
        bufferRef.current.push(e.data);
        if (bufferRef.current.length > MAX_LINES) {
          bufferRef.current = bufferRef.current.slice(-MAX_LINES);
        }
      }
    };

    es.addEventListener("done", () => {
      setStreaming(false);
      es.close();
      eventSourceRef.current = null;
    });

    es.onerror = () => {
      setStreaming(false);
      es.close();
      eventSourceRef.current = null;
    };
  }, [ns, podName, container, tail, follow, sinceSeconds, previousContainer, stopStream]);

  // Render timer: throttle re-renders to ~10/sec
  useEffect(() => {
    const id = setInterval(() => {
      if (bufferRef.current.length > 0) {
        setLines([...bufferRef.current]);
      }
    }, 100);
    return () => clearInterval(id);
  }, []);

  // Auto-scroll to bottom when not paused and following
  useEffect(() => {
    if (!paused && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [lines, paused]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
      }
    };
  }, []);

  // Restart stream when key params change
  useEffect(() => {
    if (ns && podName) {
      startStream();
    }
    return () => stopStream();
  }, [ns, podName, container, tail, follow, sinceSeconds, previousContainer, startStream, stopStream]);

  const handleExport = () => {
    const blob = new Blob([lines.join("\n")], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${podName}-${container || "logs"}.log`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const timeRangeOptions = [
    { label: "All", value: null },
    { label: "Last 5m", value: 300 },
    { label: "Last 10m", value: 600 },
    { label: "Last 1h", value: 3600 },
  ];

  const filteredLines = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return lines;
    return lines.filter((line) => line.toLowerCase().includes(term));
  }, [lines, filter]);

  const totalPages = Math.max(1, Math.ceil(filteredLines.length / PAGE_SIZE));
  const safePage = Math.min(page, totalPages - 1);
  const visibleLines = filteredLines.slice(safePage * PAGE_SIZE, (safePage + 1) * PAGE_SIZE);

  return (
    <div className="flex h-full flex-col" data-testid="pod-log-view">
      {/* Controls bar */}
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2">
        {/* Container selector */}
        {containers.length > 1 && (
          <select
            value={container}
            onChange={(e) => setContainer(e.target.value)}
            className="rounded border bg-card px-2 py-1 text-xs"
            data-testid="log-container-select"
          >
            {containers.map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </select>
        )}

        {/* Tail lines */}
        <select
          value={tail}
          onChange={(e) => setTail(Number(e.target.value))}
          className="rounded border bg-card px-2 py-1 text-xs"
          data-testid="log-tail-select"
        >
          <option value={50}>50 lines</option>
          <option value={100}>100 lines</option>
          <option value={500}>500 lines</option>
          <option value={1000}>1000 lines</option>
        </select>

        {/* Time range */}
        <select
          value={sinceSeconds ?? 0}
          onChange={(e) => setSinceSeconds(e.target.value ? Number(e.target.value) : null)}
          className="rounded border bg-card px-2 py-1 text-xs"
          data-testid="log-time-range"
        >
          {timeRangeOptions.map((opt) => (
            <option key={opt.label} value={opt.value ?? 0}>{opt.label}</option>
          ))}
        </select>

        {/* Previous container */}
        <label className="flex items-center gap-1 text-xs" data-testid="log-previous-container">
          <input
            type="checkbox"
            checked={previousContainer}
            onChange={(e) => setPreviousContainer(e.target.checked)}
          />
          <span>Previous</span>
        </label>

        {/* Follow toggle */}
        <label className="flex items-center gap-1 text-xs" data-testid="log-follow-toggle">
          <input
            type="checkbox"
            checked={follow}
            onChange={(e) => setFollow(e.target.checked)}
          />
          <span>Follow</span>
        </label>

        {/* Filter */}
        <div className="relative">
          <Search className="absolute left-2 top-1/2 h-3 w-3 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filter..."
            className="rounded border bg-card py-1 pl-7 pr-2 text-xs"
            data-testid="log-filter-input"
          />
        </div>

        {/* Pause/Resume */}
        <button
          onClick={() => setPaused(!paused)}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="log-pause-btn"
        >
          {paused ? <Play className="h-3 w-3" /> : <Pause className="h-3 w-3" />}
          {paused ? "Resume" : "Pause"}
        </button>

        {/* Stop */}
        <button
          onClick={stopStream}
          disabled={!streaming}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
          data-testid="log-stop-btn"
        >
          <Square className="h-3 w-3" />
          Stop
        </button>

        {/* Export */}
        <button
          onClick={handleExport}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="log-export-btn"
        >
          <Download className="h-3 w-3" />
          Export
        </button>

        {/* Status indicator */}
        {streaming && (
          <span className="flex items-center gap-1 text-xs text-green-500" data-testid="log-streaming-indicator">
            <span className="h-2 w-2 animate-pulse rounded-full bg-green-500" />
            Live
          </span>
        )}

        {onClose && (
          <button
            onClick={onClose}
            className="ml-auto rounded border px-2 py-1 text-xs hover:bg-accent"
          >
            Close
          </button>
        )}
      </div>

      {/* Log output */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-auto bg-background p-2 font-mono text-xs"
        data-testid="log-output"
      >
        {visibleLines.length === 0 ? (
          <div className="flex h-full items-center justify-center text-muted-foreground">
            {streaming ? "Waiting for logs..." : "No logs. Press Start or adjust filters."}
          </div>
        ) : (
          visibleLines.map((line, i) => (
            <div key={safePage * PAGE_SIZE + i} className={`log-line whitespace-pre-wrap break-all hover:bg-accent/30 ${getLogLineClass(line)}`}>
              {line}
            </div>
          ))
        )}
      </div>

      {/* Pagination */}
      {filteredLines.length > PAGE_SIZE && (
        <div className="flex items-center justify-between border-t px-3 py-1 text-xs" data-testid="log-pagination">
          <span className="text-muted-foreground">
            Page {safePage + 1} of {totalPages} ({filteredLines.length} lines)
          </span>
          <div className="flex gap-1">
            <button
              onClick={() => setPage(Math.max(0, safePage - 1))}
              disabled={safePage === 0}
              className="rounded border px-2 py-0.5 disabled:opacity-50"
            >
              ← Newer
            </button>
            <button
              onClick={() => setPage(Math.min(totalPages - 1, safePage + 1))}
              disabled={safePage >= totalPages - 1}
              className="rounded border px-2 py-0.5 disabled:opacity-50"
            >
              Older →
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
