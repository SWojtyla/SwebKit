import { useState, useEffect, useCallback } from "react";
import {
    ArrowRightLeft,
    Copy,
    ExternalLink,
    Plus,
    RefreshCw,
    Square,
} from "lucide-react";
import {
    listPortForwards,
    stopPortForward,
    probeLocalPort,
    openExternal,
    type PortForwardSessionInfo,
} from "@/lib/tauri-bridge";
import { useNotification } from "@/components/layout/notification-context";
import { PortForwardDialog } from "./PortForwardDialog";
import type { PodInfo } from "@/lib/types";

type ProbeState = "checking" | "up" | "down";

interface Props {
    /** Pod the "Port-forward…" action targeted — opens the forward dialog for it, then
     * the caller clears its selection through `onPodConsumed`. */
    selectedPod: PodInfo | null;
    onPodConsumed?: () => void;
    context?: string | null;
    kubeconfig?: string | null;
    /** Pods offered in the dialog's picker when no pod is preselected. */
    pods?: PodInfo[];
}

const PROBE_INTERVAL_MS = 10_000;

const PROBE_LABEL: Record<ProbeState, string> = {
    checking: "Checking…",
    up: "Listening",
    down: "Not accepting",
};

const PROBE_DOT_CLASS: Record<ProbeState, string> = {
    checking: "bg-warning animate-pulse",
    up: "bg-success",
    down: "bg-destructive",
};

/**
 * Active port-forward sessions as a proof-oriented list: the localhost endpoint is the
 * headline, a live TCP probe answers "does it work", and Copy/Open/Stop are one click
 * each. New forwards — whether from this panel's button or a pod's Port-forward action —
 * all go through PortForwardDialog.
 */
export function PortForwardPanel({
    selectedPod,
    onPodConsumed,
    context,
    kubeconfig,
    pods = [],
}: Props) {
    const [sessions, setSessions] = useState<PortForwardSessionInfo[]>([]);
    const [probes, setProbes] = useState<Record<number, ProbeState>>({});
    // pod: null means the picker variant; PodInfo means the fixed-target variant.
    const [dialog, setDialog] = useState<{ pod: PodInfo | null } | null>(null);
    const [error, setError] = useState<string | null>(null);
    const { notify } = useNotification();

    const refresh = useCallback(async () => {
        try {
            const list = await listPortForwards();
            setSessions(list);
            // Probe opportunistically after every list — a row that renders before its
            // probe lands shows "Checking…" rather than a stale verdict.
            setProbes((prev) => {
                const next: Record<number, ProbeState> = {};
                for (const s of list)
                    next[s.localPort] = prev[s.localPort] ?? "checking";
                return next;
            });
            for (const s of list) {
                probeLocalPort(s.localPort).then((up) =>
                    setProbes((prev) => ({
                        ...prev,
                        [s.localPort]: up ? "up" : "down",
                    })),
                );
            }
        } catch {
            // Not in Tauri — show empty
            setSessions([]);
            setProbes({});
        }
    }, []);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- async initial load into local state; fetching is the point of the effect
        refresh();
        const id = setInterval(refresh, PROBE_INTERVAL_MS);
        return () => clearInterval(id);
    }, [refresh]);

    // The pod-level Port-forward action lands here as a selection — opening the dialog is
    // the whole point of the navigation. Own-state setDialog uses the render-adjust
    // pattern; consuming the selection touches parent context state and stays an effect
    // (setState on another component during render is illegal).
    // Init to null rather than selectedPod: a deep-link that lands here with the pod
    // param already set must still open the dialog on first render.
    const [prevSelected, setPrevSelected] = useState<PodInfo | null>(null);
    if (prevSelected !== selectedPod) {
        setPrevSelected(selectedPod);
        if (selectedPod) setDialog({ pod: selectedPod });
    }

    useEffect(() => {
        if (selectedPod) onPodConsumed?.();
    }, [selectedPod, onPodConsumed]);

    const handleStarted = useCallback(
        async (result: {
            pod: PodInfo;
            remotePort: number;
            localPort: number;
        }) => {
            setDialog(null);
            await refresh();
            const url = `http://localhost:${result.localPort}`;
            notify(
                "success",
                "Port forward started",
                `${result.pod.namespace}/${result.pod.name}:${result.remotePort} → ${url}`,
                { label: "Open in browser", onClick: () => openExternal(url) },
            );
        },
        [notify, refresh],
    );

    const handleStop = async (s: PortForwardSessionInfo) => {
        try {
            await stopPortForward(s.localPort);
            await refresh();
            notify(
                "info",
                "Port forward stopped",
                `localhost:${s.localPort} closed`,
            );
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        }
    };

    return (
        <div className="p-4" data-testid="port-forward-panel">
            <div className="mb-4 flex items-center justify-between">
                <h2 className="text-sm font-semibold">Port forwards</h2>
                <div className="flex gap-2">
                    <button
                        onClick={refresh}
                        title="Refresh sessions"
                        className="rounded-md border p-1.5 hover:bg-accent"
                        data-testid="port-forward-refresh"
                    >
                        <RefreshCw className="h-3.5 w-3.5" />
                    </button>
                    <button
                        onClick={() => setDialog({ pod: null })}
                        className="flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
                        data-testid="port-forward-add"
                    >
                        <Plus className="h-3.5 w-3.5" />
                        New forward
                    </button>
                </div>
            </div>

            {error && (
                <div
                    className="mb-3 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive"
                    data-testid="port-forward-error"
                >
                    {error}
                </div>
            )}

            {sessions.length === 0 ? (
                <div
                    className="flex flex-col items-center justify-center py-8 text-sm text-muted-foreground"
                    data-testid="port-forward-empty"
                >
                    <ArrowRightLeft className="mb-2 h-8 w-8 opacity-50" />
                    No active port forwards
                    <span className="mt-1 max-w-sm text-center text-xs">
                        Forward a pod port to localhost from Pods → ⋯ →
                        Port-forward, or start one here.
                    </span>
                </div>
            ) : (
                <div className="space-y-2" data-testid="port-forward-list">
                    {sessions.map((s) => {
                        const probe = probes[s.localPort] ?? "checking";
                        const url = `http://localhost:${s.localPort}`;
                        return (
                            <div
                                key={s.localPort}
                                className="flex items-center gap-3 rounded-md border px-3 py-2"
                                data-testid={`port-forward-session-${s.localPort}`}
                            >
                                <span
                                    className={`h-2 w-2 shrink-0 rounded-full ${PROBE_DOT_CLASS[probe]}`}
                                    data-testid={`port-forward-status-${s.localPort}`}
                                    title={PROBE_LABEL[probe]}
                                    aria-label={PROBE_LABEL[probe]}
                                />
                                <div className="min-w-0 flex-1">
                                    <div className="flex items-baseline gap-2 font-mono text-xs">
                                        <span className="font-semibold text-primary">
                                            localhost:{s.localPort}
                                        </span>
                                        <span className="text-muted-foreground">
                                            →
                                        </span>
                                        <span
                                            className="truncate"
                                            title={`${s.namespace}/${s.pod}:${s.remotePort}`}
                                        >
                                            {s.pod}:{s.remotePort}
                                        </span>
                                    </div>
                                    <div className="mt-0.5 truncate text-[11px] text-muted-foreground">
                                        {PROBE_LABEL[probe]} · {s.namespace}
                                        {s.context ? ` · ${s.context}` : ""}
                                    </div>
                                </div>
                                <button
                                    onClick={() =>
                                        navigator.clipboard
                                            .writeText(url)
                                            .catch(() => {})
                                    }
                                    title="Copy localhost URL"
                                    aria-label="Copy localhost URL"
                                    className="shrink-0 rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
                                    data-testid={`port-forward-copy-${s.localPort}`}
                                >
                                    <Copy className="h-3.5 w-3.5" />
                                </button>
                                <button
                                    onClick={() => openExternal(url)}
                                    title="Open in browser"
                                    aria-label="Open in browser"
                                    className="shrink-0 rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
                                    data-testid={`port-forward-open-${s.localPort}`}
                                >
                                    <ExternalLink className="h-3.5 w-3.5" />
                                </button>
                                <button
                                    onClick={() => handleStop(s)}
                                    title="Stop forward"
                                    aria-label="Stop forward"
                                    className="shrink-0 rounded p-1 text-destructive hover:bg-destructive/10"
                                    data-testid={`port-forward-stop-${s.localPort}`}
                                >
                                    <Square className="h-3.5 w-3.5" />
                                </button>
                            </div>
                        );
                    })}
                </div>
            )}

            {dialog && (
                <PortForwardDialog
                    pod={dialog.pod}
                    pods={pods}
                    context={context}
                    kubeconfig={kubeconfig}
                    onStarted={handleStarted}
                    onClose={() => setDialog(null)}
                />
            )}
        </div>
    );
}
