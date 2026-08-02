import { useState, useEffect, useCallback } from "react";
import { Plus, Trash2, Terminal, RefreshCw } from "lucide-react";
import { listPortForwards, startPortForward, stopPortForward, type PortForwardSessionInfo } from "@/lib/tauri-bridge";

interface Props {
  ns: string;
  selectedPod: string | null;
  context?: string | null;
}

export function PortForwardPanel({ ns, selectedPod, context }: Props) {
  const [sessions, setSessions] = useState<PortForwardSessionInfo[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [pod, setPod] = useState("");
  const [remotePort, setRemotePort] = useState(8080);
  const [localPort, setLocalPort] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const list = await listPortForwards();
      setSessions(list);
    } catch {
      // Not in Tauri — show empty
      setSessions([]);
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  useEffect(() => {
    if (selectedPod) setPod(selectedPod);
  }, [selectedPod]);

  const handleStart = async () => {
    setLoading(true);
    setError(null);
    try {
      await startPortForward(ns, pod, remotePort, localPort || undefined, context);
      await refresh();
      setShowForm(false);
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  };

  const handleStop = async (port: number) => {
    try {
      await stopPortForward(port);
      await refresh();
    } catch (e) {
      setError(String(e));
    }
  };

  return (
    <div className="p-4" data-testid="port-forward-panel">
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-sm font-semibold">Port Forward Sessions</h2>
        <div className="flex gap-2">
          <button onClick={refresh} className="rounded-md border p-1.5 hover:bg-accent" data-testid="port-forward-refresh">
            <RefreshCw className="h-3.5 w-3.5" />
          </button>
          <button
            onClick={() => setShowForm(!showForm)}
            className="flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
            data-testid="port-forward-add"
          >
            <Plus className="h-3.5 w-3.5" />
            New
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-3 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive" data-testid="port-forward-error">
          {error}
        </div>
      )}

      {showForm && (
        <div className="mb-4 rounded-md border p-3 space-y-2" data-testid="port-forward-form">
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="text-xs font-medium">Pod</label>
              <input
                type="text"
                value={pod}
                onChange={(e) => setPod(e.target.value)}
                placeholder="pod-name"
                className="mt-1 w-full rounded border bg-card px-2 py-1 text-xs"
                data-testid="port-forward-pod"
              />
            </div>
            <div>
              <label className="text-xs font-medium">Remote Port</label>
              <input
                type="number"
                value={remotePort}
                onChange={(e) => setRemotePort(Number(e.target.value))}
                className="mt-1 w-full rounded border bg-card px-2 py-1 text-xs"
                data-testid="port-forward-remote-port"
              />
            </div>
            <div>
              <label className="text-xs font-medium">Local Port (0 = auto)</label>
              <input
                type="number"
                value={localPort}
                onChange={(e) => setLocalPort(Number(e.target.value))}
                className="mt-1 w-full rounded border bg-card px-2 py-1 text-xs"
                data-testid="port-forward-local-port"
              />
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <button onClick={() => setShowForm(false)} className="rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="port-forward-cancel">Cancel</button>
            <button
              onClick={handleStart}
              disabled={!pod || !remotePort || loading}
              className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground disabled:opacity-50"
              data-testid="port-forward-start"
            >
              {loading ? "Starting..." : "Start"}
            </button>
          </div>
        </div>
      )}

      {sessions.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-8 text-sm text-muted-foreground" data-testid="port-forward-empty">
          <Terminal className="mb-2 h-8 w-8 opacity-50" />
          No active port-forward sessions
        </div>
      ) : (
        <div className="rounded-md border overflow-hidden" data-testid="port-forward-list">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Namespace</th>
                <th className="px-3 py-2 text-left">Pod</th>
                <th className="px-3 py-2 text-left">Local</th>
                <th className="px-3 py-2 text-left">Remote</th>
                <th className="px-3 py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {sessions.map((s) => (
                <tr key={s.localPort} className="border-b last:border-0">
                  <td className="px-3 py-2 font-mono text-xs">{s.namespace}</td>
                  <td className="px-3 py-2 font-mono text-xs">{s.pod}</td>
                  <td className="px-3 py-2 font-mono text-xs text-primary">localhost:{s.localPort}</td>
                  <td className="px-3 py-2 font-mono text-xs">{s.remotePort}</td>
                  <td className="px-3 py-2 text-right">
                    <button
                      onClick={() => handleStop(s.localPort)}
                      className="rounded p-1 text-destructive hover:bg-destructive/10"
                      data-testid={`port-forward-stop-${s.localPort}`}
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
