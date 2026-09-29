import { useState, type JSX } from "react";
import { X } from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";
import { startPortForward } from "@/lib/tauri-bridge";
import { useAksContainerDetails } from "@/lib/hooks";
import type { PodInfo } from "@/lib/types";

interface PortForwardDialogProps {
    /** Fixed target when opened from a pod action; null shows a pod picker. */
    pod: PodInfo | null;
    /** Candidate pods for the picker when `pod` is null. */
    pods: PodInfo[];
    context?: string | null;
    kubeconfig?: string | null;
    onStarted: (result: {
        pod: PodInfo;
        remotePort: number;
        localPort: number;
    }) => void;
    onClose: () => void;
}

const PORT_RE = /^\d{1,5}$/;

function parsePort(raw: string): number | null {
    if (!PORT_RE.test(raw.trim())) return null;
    const n = Number(raw.trim());
    return n >= 1 && n <= 65535 ? n : null;
}

/**
 * The single "forward a pod" flow: pod fixed or picked, remote port suggested from the
 * container ports the pod actually declares (datalist keeps free entry for undeclared
 * ones), local port optional. Starting returns the bound local port so the caller can
 * point the user at `localhost:port` with proof.
 */
export function PortForwardDialog({
    pod: fixedPod,
    pods,
    context,
    kubeconfig,
    onStarted,
    onClose,
}: PortForwardDialogProps): JSX.Element {
    const [picked, setPicked] = useState<PodInfo | null>(fixedPod);
    const [remoteRaw, setRemoteRaw] = useState("");
    const [localRaw, setLocalRaw] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [didPrefill, setDidPrefill] = useState(false);

    const pod = fixedPod ?? picked;
    const podKey = pod ? `${pod.namespace}/${pod.name}` : null;

    const details = useAksContainerDetails(
        pod?.namespace ?? null,
        pod?.name ?? null,
    );
    // `?? []` on ports too — a stale dev sidecar predating the ports field would
    // otherwise crash the dialog on `.map`.
    const declaredPorts = (details.data ?? [])
        .flatMap((c) =>
            (c.ports ?? []).map((p) => ({ ...p, container: c.name })),
        )
        .filter((p) => p.protocol.toUpperCase() === "TCP");

    // Adjust-state-during-render for the two prop-driven resets: a pod change clears the
    // form, and the first declared port prefills exactly once per pod — not on every
    // render while the field is empty, or clearing it to retype would instantly refill.
    const [prevPodKey, setPrevPodKey] = useState(podKey);
    if (prevPodKey !== podKey) {
        setPrevPodKey(podKey);
        setRemoteRaw("");
        setError(null);
        setDidPrefill(false);
    }
    if (
        !didPrefill &&
        podKey !== null &&
        !details.isLoading &&
        declaredPorts.length > 0 &&
        remoteRaw === ""
    ) {
        setDidPrefill(true);
        setRemoteRaw(String(declaredPorts[0].port));
    }

    const remotePort = parsePort(remoteRaw);
    const localPort = localRaw.trim() === "" ? 0 : parsePort(localRaw);
    const canStart =
        pod !== null && remotePort !== null && localPort !== null && !loading;

    const handleStart = async () => {
        if (!pod || remotePort === null || localPort === null) return;
        setLoading(true);
        setError(null);
        try {
            const bound = await startPortForward(
                pod.namespace,
                pod.name,
                remotePort,
                localPort === 0 ? undefined : localPort,
                context,
                kubeconfig,
            );
            onStarted({ pod, remotePort, localPort: bound });
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
            setLoading(false);
        }
    };

    return (
        <Dialog
            onClose={onClose}
            label="Port-forward"
            testId="port-forward-dialog"
            widthClassName="w-[440px]"
        >
            <div className="flex items-start justify-between gap-3 border-b px-4 py-3">
                <div className="min-w-0">
                    <h2 className="text-sm font-semibold">Forward a port</h2>
                    <p className="truncate text-xs text-muted-foreground">
                        Reach the pod at a localhost port on this machine
                    </p>
                </div>
                <button
                    onClick={onClose}
                    className="shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Close"
                    data-testid="port-forward-dialog-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="space-y-3 p-4" data-testid="port-forward-form">
                {fixedPod ? (
                    <div>
                        <span className="text-xs font-medium">Pod</span>
                        <p
                            className="mt-1 truncate rounded-md border bg-muted/40 px-2 py-1.5 font-mono text-xs"
                            title={`${fixedPod.namespace}/${fixedPod.name}`}
                            data-testid="port-forward-pod"
                        >
                            {fixedPod.namespace}/{fixedPod.name}
                        </p>
                    </div>
                ) : (
                    <div>
                        <label
                            className="text-xs font-medium"
                            htmlFor="port-forward-pod"
                        >
                            Pod
                        </label>
                        <select
                            id="port-forward-pod"
                            value={
                                picked
                                    ? `${picked.namespace}/${picked.name}`
                                    : ""
                            }
                            onChange={(e) => {
                                const p = pods.find(
                                    (x) =>
                                        `${x.namespace}/${x.name}` ===
                                        e.target.value,
                                );
                                setPicked(p ?? null);
                            }}
                            className="mt-1 w-full rounded-md border bg-card px-2 py-1.5 text-xs"
                            data-testid="port-forward-pod"
                        >
                            <option value="" disabled>
                                Pick a pod…
                            </option>
                            {pods.map((p) => (
                                <option
                                    key={`${p.namespace}/${p.name}`}
                                    value={`${p.namespace}/${p.name}`}
                                >
                                    {p.namespace}/{p.name}
                                </option>
                            ))}
                        </select>
                    </div>
                )}

                <div className="grid grid-cols-2 gap-3">
                    <div>
                        <label
                            className="text-xs font-medium"
                            htmlFor="port-forward-remote-port"
                        >
                            Remote port
                        </label>
                        <input
                            id="port-forward-remote-port"
                            type="text"
                            inputMode="numeric"
                            list="port-forward-port-options"
                            value={remoteRaw}
                            onChange={(e) => setRemoteRaw(e.target.value)}
                            placeholder={
                                details.isLoading
                                    ? "Loading ports…"
                                    : "e.g. 8080"
                            }
                            autoFocus={!!fixedPod}
                            className="mt-1 w-full rounded-md border bg-card px-2 py-1.5 text-xs tabular-nums"
                            data-testid="port-forward-remote-port"
                        />
                        <datalist id="port-forward-port-options">
                            {declaredPorts.map((p) => (
                                <option
                                    key={`${p.container}-${p.port}`}
                                    value={String(p.port)}
                                >
                                    {p.container}
                                    {p.name ? ` · ${p.name}` : ""}
                                </option>
                            ))}
                        </datalist>
                        {pod &&
                            !details.isLoading &&
                            declaredPorts.length === 0 && (
                                <p className="mt-1 text-[11px] text-muted-foreground">
                                    This pod declares no container ports — enter
                                    the port it listens on.
                                </p>
                            )}
                        {remoteRaw !== "" && remotePort === null && (
                            <p className="mt-1 text-[11px] text-destructive">
                                Enter a port between 1 and 65535.
                            </p>
                        )}
                    </div>
                    <div>
                        <label
                            className="text-xs font-medium"
                            htmlFor="port-forward-local-port"
                        >
                            Local port
                        </label>
                        <input
                            id="port-forward-local-port"
                            type="text"
                            inputMode="numeric"
                            value={localRaw}
                            onChange={(e) => setLocalRaw(e.target.value)}
                            placeholder="auto"
                            className="mt-1 w-full rounded-md border bg-card px-2 py-1.5 text-xs tabular-nums"
                            data-testid="port-forward-local-port"
                        />
                        {localRaw.trim() !== "" && localPort === null && (
                            <p className="mt-1 text-[11px] text-destructive">
                                Enter a port between 1 and 65535, or leave
                                empty.
                            </p>
                        )}
                    </div>
                </div>

                {pod && remotePort !== null && (
                    <p
                        className="rounded-md bg-accent/40 px-3 py-2 text-xs text-muted-foreground"
                        data-testid="port-forward-summary"
                    >
                        {pod.namespace}/{pod.name}:{remotePort} →{" "}
                        <span className="font-medium text-foreground">
                            localhost:{localPort === 0 ? "<auto>" : localPort}
                        </span>
                    </p>
                )}

                {error && (
                    <p
                        className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive"
                        data-testid="port-forward-error"
                    >
                        {error}
                    </p>
                )}
            </div>

            <div className="flex justify-end gap-2 border-t px-4 py-3">
                <button
                    onClick={onClose}
                    className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
                    data-testid="port-forward-cancel"
                >
                    Cancel
                </button>
                <button
                    onClick={handleStart}
                    disabled={!canStart}
                    title={
                        loading
                            ? "Starting…"
                            : !pod
                              ? "Pick a pod first"
                              : remotePort === null
                                ? "Enter a remote port"
                                : localPort === null
                                  ? "Fix the local port"
                                  : undefined
                    }
                    className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                    data-testid="port-forward-start"
                >
                    {loading ? "Starting…" : "Start forwarding"}
                </button>
            </div>
        </Dialog>
    );
}
