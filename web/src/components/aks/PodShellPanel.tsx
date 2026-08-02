import { useEffect, useRef, useState } from "react";
import { X } from "lucide-react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { startPodShell, writePodShell, resizePodShell, closePodShell, onPodShellOutput } from "@/lib/tauri-bridge";

interface PodShellPanelProps {
  namespace: string;
  pod: string;
  container?: string | null;
  context?: string | null;
  kubeconfig?: string | null;
  onClose: () => void;
}

type ShellStatus = "connecting" | "connected" | "closed" | "error";

/**
 * Interactive shell into a pod (`kubectl exec -it`), rendered as a large modal rather than the
 * shared `Dialog` primitive — that primitive closes on Escape, which would swallow the Escape key
 * a real terminal session (vim, less, an interactive CLI) needs to receive. Closing here is only
 * ever via the explicit button, which also tears down the pty session server-side.
 */
export function PodShellPanel({ namespace, pod, container, context, kubeconfig, onClose }: PodShellPanelProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const [status, setStatus] = useState<ShellStatus>("connecting");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    let disposed = false;
    let unsubscribe: (() => void) | null = null;

    const term = new Terminal({
      cursorBlink: true,
      fontSize: 13,
      fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
      theme: { background: "#1e1e1e" },
    });
    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);
    term.open(el);
    fitAddon.fit();

    const encoder = new TextEncoder();
    const dataSubscription = term.onData((data) => {
      if (sessionIdRef.current) {
        void writePodShell(sessionIdRef.current, encoder.encode(data));
      }
    });

    const resizeObserver = new ResizeObserver(() => {
      fitAddon.fit();
      if (sessionIdRef.current) {
        void resizePodShell(sessionIdRef.current, term.cols, term.rows);
      }
    });
    resizeObserver.observe(el);

    void (async () => {
      try {
        const sessionId = await startPodShell(namespace, pod, container, context, kubeconfig);
        if (disposed) {
          void closePodShell(sessionId);
          return;
        }
        sessionIdRef.current = sessionId;
        setStatus("connected");
        await resizePodShell(sessionId, term.cols, term.rows);

        unsubscribe = await onPodShellOutput(
          sessionId,
          (bytes) => term.write(bytes),
          () => setStatus("closed"),
        );
      } catch (e) {
        setStatus("error");
        setError(String(e));
      }
    })();

    return () => {
      disposed = true;
      dataSubscription.dispose();
      resizeObserver.disconnect();
      unsubscribe?.();
      if (sessionIdRef.current) {
        void closePodShell(sessionIdRef.current);
      }
      term.dispose();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [namespace, pod, container, context, kubeconfig]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="pod-shell-overlay">
      <div
        role="dialog"
        aria-modal="true"
        aria-label={`Shell in pod ${pod}`}
        className="flex h-[80vh] w-[90vw] max-w-5xl flex-col rounded-lg border bg-card shadow-lg"
        data-testid="pod-shell-panel"
      >
        <div className="flex items-center justify-between border-b px-4 py-2">
          <div className="flex items-center gap-2 text-sm">
            <span className="font-mono" data-testid="pod-shell-pod-name">{pod}</span>
            {container && <span className="text-muted-foreground">/ {container}</span>}
            {status === "connecting" && (
              <span className="text-xs text-muted-foreground" data-testid="pod-shell-status">Connecting…</span>
            )}
            {status === "closed" && (
              <span className="text-xs text-muted-foreground" data-testid="pod-shell-status">Session ended</span>
            )}
            {status === "error" && (
              <span className="text-xs text-destructive" data-testid="pod-shell-status">{error}</span>
            )}
          </div>
          <button
            onClick={onClose}
            className="rounded p-1 hover:bg-accent"
            data-testid="pod-shell-close"
            aria-label="Close shell"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div ref={containerRef} className="min-h-0 flex-1 overflow-hidden bg-[#1e1e1e] p-2" data-testid="pod-shell-terminal" />
      </div>
    </div>
  );
}
