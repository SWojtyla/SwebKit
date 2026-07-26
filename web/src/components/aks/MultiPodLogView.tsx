import { useState, useEffect, useRef } from "react";
import { X } from "lucide-react";
import { SIDECAR_BASE_URL } from "@/lib/api";

interface Props {
  ns: string;
  pods: string[];
  onClose: () => void;
}

interface LogLine {
  pod: string;
  line: string;
  timestamp: string;
}

export function MultiPodLogView({ ns, pods, onClose }: Props) {
  const [selectedPods, setSelectedPods] = useState<string[]>([]);
  const [logs, setLogs] = useState<LogLine[]>([]);
  const [container, setContainer] = useState("");
  const sourcesRef = useRef<Map<string, EventSource>>(new Map());
  const streamKeyRef = useRef(`${ns}::${container}`);

  const togglePod = (pod: string) => {
    setSelectedPods((prev) =>
      prev.includes(pod) ? prev.filter((p) => p !== pod) : [...prev, pod],
    );
  };

  // One SSE stream per selected pod (matches the sidecar's actual log-stream
  // contract — it's SSE, not a WebSocket). Deselecting a pod closes just its
  // stream; selecting one opens a new one alongside the rest, so logs from
  // multiple pods interleave for correlation. Changing namespace/container
  // tears down and reopens every stream since the filter itself changed.
  useEffect(() => {
    const sources = sourcesRef.current;
    const streamKey = `${ns}::${container}`;
    const filterChanged = streamKeyRef.current !== streamKey;
    streamKeyRef.current = streamKey;

    if (filterChanged) {
      for (const es of sources.values()) es.close();
      sources.clear();
      setLogs([]);
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
      const params = new URLSearchParams({ tail: "100", follow: "true" });
      if (container) params.set("container", container);
      const es = new EventSource(
        `${SIDECAR_BASE_URL}/api/aks/${ns}/pods/${pod}/logs/stream?${params}`,
      );
      es.onmessage = (e) => {
        setLogs((prev) =>
          [...prev, { pod, line: e.data, timestamp: new Date().toLocaleTimeString() }].slice(-500),
        );
      };
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
  }, [selectedPods, ns, container]);

  // Full teardown on unmount.
  useEffect(() => {
    return () => {
      for (const es of sourcesRef.current.values()) es.close();
      sourcesRef.current.clear();
    };
  }, []);

  return (
    <div className="flex h-full flex-col" data-testid="multi-pod-log-view">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="text-sm font-semibold">Multi-Pod Log Correlation</h2>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="multi-pod-log-close">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="border-b px-4 py-2">
        <span className="text-xs text-muted-foreground">Select pods to correlate logs:</span>
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
      <div className="flex-1 overflow-auto p-2" data-testid="multi-pod-log-output">
        {logs.length === 0 ? (
          <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
            {selectedPods.length === 0 ? "Select pods to start streaming logs" : "Connecting..."}
          </div>
        ) : (
          logs.map((log, i) => (
            <div key={i} className="border-b py-0.5 text-xs last:border-0">
              <span className="text-muted-foreground">[{log.timestamp}] {log.pod}: </span>
              <span className="font-mono">{log.line}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
