import { useMemo, useState } from "react";
import { AlertCircle } from "lucide-react";
import { useAksEvents } from "@/lib/hooks";
import { useAksWorkspace, type TabId } from "./shared/AksWorkspaceContext";
import { formatLocalTime } from "@/lib/datetime";

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

export function EventsTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
    const { data: events, isLoading, error } = useAksEvents(ns);
    const ws = useAksWorkspace();
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
        return (
            <div className="p-4 text-sm text-muted-foreground">Loading...</div>
        );

    if (error) {
        return (
            <div
                className="flex items-center gap-2 p-4 text-sm text-destructive"
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
            <label className="mb-2 flex items-center gap-2 text-xs">
                <input
                    type="checkbox"
                    checked={warningsOnly}
                    onChange={(e) => setWarningsOnly(e.target.checked)}
                    data-testid="events-warnings-only"
                />
                Warnings only
            </label>
            {!events || events.length === 0 ? (
                <div className="text-sm text-muted-foreground">
                    No events found
                </div>
            ) : visibleEvents.length === 0 ? (
                <div className="text-sm text-muted-foreground">
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
                                key={`${evt.namespace}/${evt.name}-${evt.involvedObjectName}`}
                                data-testid={`event-item-${evt.name}`}
                                className="flex items-start gap-3 rounded-md border p-2 text-sm"
                            >
                                <span
                                    className={`mt-0.5 shrink-0 rounded px-1.5 py-0.5 text-xs font-medium ${
                                        evt.type === "Warning"
                                            ? "bg-warning/20 text-warning"
                                            : "bg-success/20 text-success"
                                    }`}
                                >
                                    {evt.type}
                                </span>
                                <div className="flex-1">
                                    <div className="flex items-center gap-2">
                                        {isMulti && (
                                            <span className="text-xs text-muted-foreground">
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
                                                    className="text-xs text-primary hover:underline"
                                                    data-testid={`event-target-${evt.name}`}
                                                    title={`Go to ${evt.involvedObjectKind} in the ${targetTab} tab`}
                                                >
                                                    {evt.involvedObjectKind}/
                                                    {evt.involvedObjectName}
                                                </button>
                                            ) : (
                                                <span className="text-xs text-muted-foreground">
                                                    {evt.involvedObjectKind}/
                                                    {evt.involvedObjectName}
                                                </span>
                                            ))}
                                        {evt.count > 1 && (
                                            <span className="text-xs text-muted-foreground">
                                                ×{evt.count}
                                            </span>
                                        )}
                                    </div>
                                    <p className="mt-0.5 text-xs text-muted-foreground">
                                        {evt.message}
                                    </p>
                                </div>
                                <span className="shrink-0 text-xs text-muted-foreground">
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
