import { useState } from "react";
import { X, Download, Terminal } from "lucide-react";
import { useAksPodLogs } from "@/lib/hooks";
import type { PodInfo } from "@/lib/types";

interface PodDetailPanelProps {
  pod: PodInfo;
  ns: string;
  onClose: () => void;
}

export function PodDetailPanel({ pod, ns, onClose }: PodDetailPanelProps) {
  const [container, setContainer] = useState<string>(pod.containers[0] ?? "");
  const [tail, setTail] = useState(100);
  const [filter, setFilter] = useState("");
  const { data: logs, isLoading, refetch } = useAksPodLogs(ns, pod.name, container, tail);

  const filteredLines = (logs ?? "")
    .split("\n")
    .filter((l) => !filter || l.toLowerCase().includes(filter.toLowerCase()));

  const handleDownload = () => {
    const blob = new Blob([logs ?? ""], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${pod.name}-${container}.log`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="flex h-full flex-col" data-testid="pod-detail-panel">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <Terminal className="h-4 w-4" />
        <span className="text-sm font-medium">{pod.name}</span>
        <span className="text-xs text-muted-foreground">Logs</span>
        <button onClick={onClose} className="ml-auto rounded p-1 hover:bg-accent">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="flex items-center gap-2 border-b px-4 py-2">
        <select
          data-testid="pod-log-container-select"
          value={container}
          onChange={(e) => setContainer(e.target.value)}
          className="rounded border bg-card px-2 py-1 text-xs"
        >
          {pod.containers.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
        <select
          data-testid="pod-log-tail-select"
          value={tail}
          onChange={(e) => setTail(Number(e.target.value))}
          className="rounded border bg-card px-2 py-1 text-xs"
        >
          <option value={50}>50 lines</option>
          <option value={100}>100 lines</option>
          <option value={500}>500 lines</option>
          <option value={1000}>1000 lines</option>
        </select>
        <input
          data-testid="pod-log-filter-input"
          type="text"
          placeholder="Filter logs..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="rounded border bg-card px-2 py-1 text-xs"
        />
        <button
          onClick={() => refetch()}
          className="rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="pod-log-refresh"
        >
          Refresh
        </button>
        <button
          onClick={handleDownload}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="pod-log-download"
        >
          <Download className="h-3 w-3" /> Download
        </button>
      </div>

      <div className="flex-1 overflow-auto bg-black p-3" data-testid="pod-log-view">
        {isLoading ? (
          <div className="text-green-400 text-xs font-mono">Loading logs...</div>
        ) : filteredLines.length === 0 ? (
          <div className="text-gray-500 text-xs font-mono">No log lines</div>
        ) : (
          <pre className="whitespace-pre-wrap break-all text-xs font-mono text-green-400">
            {filteredLines.join("\n")}
          </pre>
        )}
      </div>
    </div>
  );
}
