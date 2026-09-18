import { useCallback, useEffect, useRef, useState } from "react";
import { X } from "lucide-react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import {
    startPodShell,
    writePodShell,
    resizePodShell,
    closePodShell,
    onPodShellOutput,
} from "@/lib/tauri-bridge";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";

interface PodShellPanelProps {
    namespace: string;
    pod: string;
    container?: string | null;
    context?: string | null;
    kubeconfig?: string | null;
    onClose: () => void;
}

type ShellStatus = "connecting" | "connected" | "closed" | "error";

const HEIGHT_PREF = "aks-pod-shell-height";
const DEFAULT_HEIGHT = 320;
const MIN_HEIGHT = 140;

function clampHeight(value: number): number {
    const max = Math.max(MIN_HEIGHT, Math.round(window.innerHeight * 0.8));
    return Math.min(max, Math.max(MIN_HEIGHT, value));
}

/**
 * Interactive shell into a pod (`kubectl exec -it`), docked at the bottom of the AKS page like
 * an integrated editor terminal — a previous version rendered it as a full-screen modal, which
 * locked the whole UI behind the session and was killed the moment you switched tabs. The dock
 * keeps the pods table (and everything else) usable while the session runs, and the session
 * survives resource-tab switches; only a cluster context change tears it down (see
 * `AksWorkspaceContext.handleContextChange`). Closing is only ever via the explicit button,
 * which also tears down the pty session server-side — Escape is deliberately not handled so a
 * real terminal session (vim, less) receives it untouched.
 */
export function PodShellPanel({
    namespace,
    pod,
    container,
    context,
    kubeconfig,
    onClose,
}: PodShellPanelProps) {
    const containerRef = useRef<HTMLDivElement | null>(null);
    const sessionIdRef = useRef<string | null>(null);
    const [status, setStatus] = useState<ShellStatus>("connecting");
    const [error, setError] = useState<string | null>(null);

    const [height, setHeight] = useState(() =>
        clampHeight(loadViewPreference(HEIGHT_PREF, DEFAULT_HEIGHT)),
    );
    const heightRef = useRef(height);
    useEffect(() => {
        heightRef.current = height;
    }, [height]);

    // Re-clamp on window resize so a shrunken window can't leave the panel taller than the view.
    useEffect(() => {
        const onResize = () => setHeight((current) => clampHeight(current));
        window.addEventListener("resize", onResize);
        return () => window.removeEventListener("resize", onResize);
    }, []);

    const [isDragging, setIsDragging] = useState(false);
    const startYRef = useRef(0);
    const startHeightRef = useRef(height);

    const handleDragStart = useCallback(
        (e: React.MouseEvent<HTMLDivElement>) => {
            e.preventDefault();
            startYRef.current = e.clientY;
            startHeightRef.current = heightRef.current;
            setIsDragging(true);
            document.body.style.userSelect = "none";
        },
        [],
    );

    useEffect(() => {
        if (!isDragging) return;

        const handleMouseMove = (e: MouseEvent) => {
            setHeight(
                clampHeight(
                    startHeightRef.current + (startYRef.current - e.clientY),
                ),
            );
        };
        const handleMouseUp = () => {
            setIsDragging(false);
            document.body.style.userSelect = "";
            saveViewPreference(HEIGHT_PREF, heightRef.current);
        };

        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);
        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, [isDragging]);

    const handleDoubleClick = useCallback(() => {
        const max = Math.max(MIN_HEIGHT, Math.round(window.innerHeight * 0.8));
        const next = heightRef.current >= max ? DEFAULT_HEIGHT : max;
        setHeight(next);
        saveViewPreference(HEIGHT_PREF, next);
    }, []);

    useEffect(() => {
        const el = containerRef.current;
        if (!el) return;

        let disposed = false;
        let unsubscribe: (() => void) | null = null;

        const term = new Terminal({
            cursorBlink: true,
            fontSize: 13,
            fontFamily:
                "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
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
                const sessionId = await startPodShell(
                    namespace,
                    pod,
                    container,
                    context,
                    kubeconfig,
                );
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
    }, [namespace, pod, container, context, kubeconfig]);

    return (
        <div
            className="relative flex shrink-0 flex-col border-t bg-card"
            style={{ height }}
            role="region"
            aria-label={`Shell in pod ${pod}`}
            data-testid="pod-shell-panel"
        >
            <div
                className="absolute -top-0.5 left-0 right-0 z-10 h-1.5 cursor-row-resize bg-transparent transition-colors hover:bg-primary/50 active:bg-primary/50"
                onMouseDown={handleDragStart}
                onDoubleClick={handleDoubleClick}
                title="Drag to resize, double-click to maximize or reset"
                data-testid="pod-shell-resize-handle"
            />
            <div className="flex items-center justify-between border-b px-4 py-2">
                <div className="flex items-center gap-2 text-sm">
                    <span
                        className="font-mono"
                        data-testid="pod-shell-pod-name"
                    >
                        {pod}
                    </span>
                    {container && (
                        <span className="text-muted-foreground">
                            / {container}
                        </span>
                    )}
                    {status === "connecting" && (
                        <span
                            className="text-xs text-muted-foreground"
                            data-testid="pod-shell-status"
                        >
                            Connecting…
                        </span>
                    )}
                    {status === "closed" && (
                        <span
                            className="text-xs text-muted-foreground"
                            data-testid="pod-shell-status"
                        >
                            Session ended
                        </span>
                    )}
                    {status === "error" && (
                        <span
                            className="text-xs text-destructive"
                            data-testid="pod-shell-status"
                        >
                            {error}
                        </span>
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
            <div
                ref={containerRef}
                className="min-h-0 flex-1 overflow-hidden bg-[#1e1e1e] p-2"
                data-testid="pod-shell-terminal"
            />
        </div>
    );
}
