import { useState } from "react";
import { useAksContainerDetails, useAksPodMetrics } from "@/lib/hooks";

interface ContainerDetailPanelProps {
    ns: string;
    podName: string;
    context?: string | null;
}

export function ContainerDetailPanel({
    ns,
    podName,
    context,
}: ContainerDetailPanelProps) {
    const {
        data: containers,
        isLoading,
        error,
    } = useAksContainerDetails(ns, podName, context ?? undefined);
    const { data: metrics } = useAksPodMetrics(
        ns ? [{ context: context ?? "", ns }] : [],
    );
    const [activeContainer, setActiveContainer] = useState(0);
    const [revealedSecrets, setRevealedSecrets] = useState<Set<string>>(
        new Set(),
    );

    const podMetrics = metrics?.find((m) => m.podName === podName);

    if (isLoading) {
        return (
            <div className="p-4 text-sm text-fg-muted">
                Loading container details...
            </div>
        );
    }

    if (error) {
        return (
            <div className="p-4 text-sm text-state-blocked-ink">
                Error: {error.message}
            </div>
        );
    }

    if (!containers || containers.length === 0) {
        return (
            <div className="p-4 text-sm text-fg-muted">
                No container details available
            </div>
        );
    }

    const container = containers[activeContainer] ?? containers[0];
    const containerMetric = podMetrics?.containers.find(
        (c) => c.name === container.name,
    );

    const toggleSecret = (name: string) => {
        setRevealedSecrets((prev) => {
            const next = new Set(prev);
            if (next.has(name)) next.delete(name);
            else next.add(name);
            return next;
        });
    };

    return (
        <div
            className="flex h-full flex-col overflow-auto"
            data-testid="container-detail-panel"
        >
            {/* Container tabs */}
            {containers.length > 1 && (
                <div className="flex border-b px-2">
                    {containers.map((c, i) => (
                        <button
                            key={c.name}
                            onClick={() => setActiveContainer(i)}
                            className={`-mb-px whitespace-nowrap border-b-2 px-3 py-1.5 text-xs font-medium ${activeContainer === i ? "border-primary text-fg" : "border-transparent text-fg-muted hover:text-fg"}`}
                        >
                            {c.name}
                        </button>
                    ))}
                </div>
            )}

            {/* Container info */}
            <div className="p-4 space-y-4">
                {/* Basic info */}
                <div className="space-y-1">
                    <h3 className="text-sm font-semibold">{container.name}</h3>
                    <div className="text-xs text-fg-muted">
                        <span className="font-medium">Image:</span>{" "}
                        {container.image}
                        {container.imageTag && (
                            <span className="ml-1">:{container.imageTag}</span>
                        )}
                    </div>
                </div>

                {/* Metrics */}
                {containerMetric && (
                    <div className="rounded-lg border bg-surface-1 p-4 shadow">
                        <div className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-muted">
                            Metrics
                        </div>
                        <div className="grid grid-cols-2 gap-2 text-xs">
                            <div>
                                <span className="text-fg-muted">
                                    CPU:
                                </span>{" "}
                                <span className="font-mono">
                                    {(containerMetric.cpuCores * 1000).toFixed(
                                        0,
                                    )}
                                    m
                                </span>
                            </div>
                            <div>
                                <span className="text-fg-muted">
                                    Memory:
                                </span>{" "}
                                <span className="font-mono">
                                    {Math.round(
                                        containerMetric.memoryBytes /
                                            (1024 * 1024),
                                    )}
                                    Mi
                                </span>
                            </div>
                        </div>
                    </div>
                )}

                {/* Resources */}
                <div className="rounded-lg border bg-surface-1 p-4 shadow">
                    <div className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-muted">
                        Resources
                    </div>
                    <div className="grid grid-cols-2 gap-2 text-xs">
                        <div>
                            <span className="text-fg-muted">
                                CPU Request:
                            </span>{" "}
                            <span className="font-mono">
                                {container.resources.cpuRequest ?? "—"}
                            </span>
                        </div>
                        <div>
                            <span className="text-fg-muted">
                                CPU Limit:
                            </span>{" "}
                            <span className="font-mono">
                                {container.resources.cpuLimit ?? "—"}
                            </span>
                        </div>
                        <div>
                            <span className="text-fg-muted">
                                Memory Request:
                            </span>{" "}
                            <span className="font-mono">
                                {container.resources.memoryRequest ?? "—"}
                            </span>
                        </div>
                        <div>
                            <span className="text-fg-muted">
                                Memory Limit:
                            </span>{" "}
                            <span className="font-mono">
                                {container.resources.memoryLimit ?? "—"}
                            </span>
                        </div>
                    </div>
                </div>

                {/* Environment Variables */}
                {container.envVars.length > 0 && (
                    <div className="rounded-lg border bg-surface-1 p-4 shadow">
                        <div className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-muted">
                            Environment Variables ({container.envVars.length})
                        </div>
                        <div className="space-y-1">
                            {container.envVars.map((env) => (
                                <div
                                    key={env.name}
                                    className="flex items-start gap-2 text-xs"
                                >
                                    <span className="font-mono font-medium min-w-[160px]">
                                        {env.name}
                                    </span>
                                    <span className="text-fg-muted">
                                        =
                                    </span>
                                    <div className="flex-1">
                                        {env.source === "Plain" ? (
                                            <span className="font-mono break-all">
                                                {env.value ?? "—"}
                                            </span>
                                        ) : (
                                            <div className="flex items-center gap-2">
                                                <span className="rounded bg-accent-soft px-1.5 py-0.5 text-[10px] text-accent-ink">
                                                    {env.source}
                                                </span>
                                                {env.sourceName && (
                                                    <span className="font-mono text-fg-muted">
                                                        {env.sourceName}
                                                        {env.sourceKey &&
                                                            `/${env.sourceKey}`}
                                                    </span>
                                                )}
                                                {env.isResolved &&
                                                    env.value && (
                                                        <button
                                                            onClick={() =>
                                                                toggleSecret(
                                                                    env.name,
                                                                )
                                                            }
                                                            className="text-accent-ink hover:underline"
                                                        >
                                                            {revealedSecrets.has(
                                                                env.name,
                                                            )
                                                                ? env.value
                                                                : "••••••••"}
                                                        </button>
                                                    )}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
