import { useMemo, useState } from "react";
import { AlertCircle } from "lucide-react";
import { useAksEvents } from "@/lib/hooks";
import { useAksNav, type TabId } from "./shared/aks-workspace-context";
import { formatLocalTime } from "@/lib/datetime";
import type { AksQueryTarget } from "@/lib/types";

/** Kubernetes `involvedObject.kind` -> the AKS tab that shows that resource, for click-through.
 * Kinds with no dedicated tab (ReplicaSet, Node, Endpoints, ...) are left unmapped and simply
 * aren't clickable, rather than guessing at a tab that doesn't actually show them. */
const KIND_TO_TAB: Partial<Record<string, TabId>> = {
    Pod: "pods",
    Deployment: "deployments",
    StatefulSet: "statefulsets",
    Job: "jobs",
    CronJob: "cronjobs",
    Service: "services",
    Ingress: "ingresses",
    ConfigMap: "configmaps",
    Secret: "secrets",
    HorizontalPodAutoscaler: "hpa",
    Gateway: "gateways",
    GatewayClass: "gatewayclasses",
    HTTPRoute: "httproutes",
};

export function EventsTab({
    targets,
    isMulti,
    showContext,
}: {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}) {
    const {
        data: events,
        isLoading,
        error,
        contextErrors,
    } = useAksEvents(targets);
    const ws = useAksNav();
    const [warningsOnly, setWarningsOnly] = useState(false);

    const visibleEvents = useMemo(() => {
        const base = warningsOnly
            ? (events ?? []).filter((e) => e.type === "Warning")
            : (events ?? []);
        // Most recent first — the backend's own order isn't guaranteed to be chronological, and for
        // "what's going wrong right now" the newest events are what matters most.
        return [...base].sort((a, b) => {
            const at = a.lastTimestamp
                ? new Date(a.lastTimestamp).getTime()
                : 0;
            const bt = b.lastTimestamp
                ? new Date(b.lastTimestamp).getTime()
                : 0;
            return bt - at;
        });
    }, [events, warningsOnly]);

    if (isLoading)
        return <div className="p-4 text-sm text-fg-muted">Loading...</div>;

    if (error) {
        return (
            <div
                className="flex items-center gap-2 p-4 text-sm text-state-blocked-ink"
                data-testid="events-error"
            >
                <AlertCircle className="h-4 w-4 shrink-0" />
                <span>
                    {error instanceof Error ? error.message : String(error)}
                </span>
            </div>
        );
    }

    return (
        <div className="p-4" data-testid="events-list">
            <label className="mb-2 flex items-center gap-2 text-xs text-fg-muted">
                <input
                    type="checkbox"
                    checked={warningsOnly}
                    onChange={(e) => setWarningsOnly(e.target.checked)}
                    className="h-3.5 w-3.5 accent-primary"
                    data-testid="events-warnings-only"
                />
                Warnings only
            </label>
            {contextErrors.length > 0 && (
                <div
                    className="mb-2 flex flex-col gap-1"
                    data-testid="events-context-errors"
                >
                    {contextErrors.map((e) => (
                        <div
                            key={e.context}
                            className="flex items-center gap-2 rounded-md border border-state-blocked/30 bg-state-blocked-soft px-2 py-1.5 text-xs text-state-blocked-ink"
                            data-testid={`events-context-error-${e.context}`}
                        >
                            <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                            <span>
                                {e.context}:{" "}
                                {e.error instanceof Error
                                    ? e.error.message
                                    : String(e.error)}
                            </span>
                        </div>
                    ))}
                </div>
            )}
            {!events || events.length === 0 ? (
                <div className="text-sm text-fg-muted">No events found</div>
            ) : visibleEvents.length === 0 ? (
                <div className="text-sm text-fg-muted">
                    No warning events found
                </div>
            ) : (
                <div className="space-y-1">
                    {visibleEvents.map((evt) => {
                        const targetTab = evt.involvedObjectKind
                            ? KIND_TO_TAB[evt.involvedObjectKind]
                            : undefined;
                        return (
                            <div
                                key={`${evt.context}:${evt.namespace}/${evt.name}-${evt.involvedObjectName}`}
                                data-testid={`event-item-${evt.name}`}
                                className="flex items-start gap-3 rounded-lg border bg-surface-1 p-3 text-sm shadow"
                            >
                                <span
                                    className={`mt-0.5 inline-flex h-5 shrink-0 items-center rounded-full px-2 text-xs font-semibold ${
                                        evt.type === "Warning"
                                            ? "bg-state-needs-soft text-state-needs-ink"
                                            : "bg-state-done-soft text-state-done-ink"
                                    }`}
                                >
                                    {evt.type}
                                </span>
                                <div className="flex-1">
                                    <div className="flex items-center gap-2">
                                        {showContext && evt.context && (
                                            <span
                                                className="rounded-md bg-accent-soft px-1.5 py-0.5 text-[10px] font-medium text-accent-ink"
                                                data-testid={`event-context-${evt.name}`}
                                            >
                                                {evt.context}
                                            </span>
                                        )}
                                        {isMulti && (
                                            <span className="text-xs text-fg-muted">
                                                {evt.namespace}
                                            </span>
                                        )}
                                        <span className="font-medium">
                                            {evt.reason}
                                        </span>
                                        {evt.involvedObjectName &&
                                            (targetTab ? (
                                                <button
                                                    onClick={() =>
                                                        ws.setActiveTab(
                                                            targetTab,
                                                        )
                                                    }
                                                    className="text-xs text-accent-ink hover:underline"
                                                    data-testid={`event-target-${evt.name}`}
                                                    title={`Go to ${evt.involvedObjectKind} in the ${targetTab} tab`}
                                                >
                                                    {evt.involvedObjectKind}/
                                                    {evt.involvedObjectName}
                                                </button>
                                            ) : (
                                                <span className="text-xs text-fg-muted">
                                                    {evt.involvedObjectKind}/
                                                    {evt.involvedObjectName}
                                                </span>
                                            ))}
                                        {evt.count > 1 && (
                                            <span className="text-xs text-fg-muted">
                                                ×{evt.count}
                                            </span>
                                        )}
                                    </div>
                                    <p className="mt-0.5 text-xs text-fg-muted">
                                        {evt.message}
                                    </p>
                                </div>
                                <span className="shrink-0 text-xs text-fg-muted">
                                    {evt.lastTimestamp
                                        ? formatLocalTime(evt.lastTimestamp)
                                        : ""}
                                </span>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}
