import { useNavigate } from "react-router";
import {
    Database,
    FolderOpen,
    Loader2,
    MessageSquare,
    Pin,
    PinOff,
    Ship,
    Table2,
} from "lucide-react";
import { useProfile, useTogglePinnedResource } from "@/lib/hooks";
import type { FavoriteResource } from "@/lib/types";
import type { ServiceConnectivity, ServiceHealth } from "./useServiceHealth";

// Status pill per connectivity state, on the shared state vocabulary
// (done / needs / blocked / working / queued) so every theme colours it.
const STATUS_META: Record<
    ServiceConnectivity,
    { label: string; className: string }
> = {
    "not-configured": {
        label: "Not configured",
        className: "bg-state-queued-soft text-state-queued-ink",
    },
    "on-demand": {
        label: "On demand",
        className: "bg-state-queued-soft text-state-queued-ink",
    },
    checking: {
        label: "Checking…",
        className: "bg-state-working-soft text-state-working-ink",
    },
    connecting: {
        label: "Connecting…",
        className: "bg-state-working-soft text-state-working-ink",
    },
    connected: {
        label: "Connected",
        className: "bg-state-done-soft text-state-done-ink",
    },
    degraded: {
        label: "Degraded",
        className: "bg-state-needs-soft text-state-needs-ink",
    },
    unavailable: {
        label: "Unavailable",
        className: "bg-state-blocked-soft text-state-blocked-ink",
    },
};

function StatusBadge({
    status,
    testId,
}: {
    status: ServiceConnectivity;
    testId: string;
}) {
    const { label, className } = STATUS_META[status];
    const busy = status === "checking" || status === "connecting";
    return (
        <span
            className={`inline-flex h-[22px] shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full px-2 text-xs font-semibold ${className}`}
            data-testid={testId}
        >
            {busy ? (
                <Loader2 className="h-3 w-3 animate-spin" />
            ) : (
                <span className="h-1.5 w-1.5 rounded-full bg-current" />
            )}
            {label}
        </span>
    );
}

interface ServiceDef {
    key: string;
    name: string;
    to: string;
    icon: typeof Ship;
    emptyHint: string;
}

const SERVICES: ServiceDef[] = [
    {
        key: "service-bus",
        name: "Service Bus",
        to: "/service-bus",
        icon: MessageSquare,
        emptyHint: "Configure a namespace",
    },
    {
        key: "aks",
        name: "AKS",
        to: "/aks",
        icon: Ship,
        emptyHint: "Configure a cluster connection",
    },
    {
        key: "redis",
        name: "Redis",
        to: "/redis",
        icon: Database,
        emptyHint: "Configure a cache",
    },
    {
        key: "storage",
        name: "Storage",
        to: "/storage",
        icon: FolderOpen,
        emptyHint: "Configure an account",
    },
    {
        key: "sql",
        name: "SQL",
        to: "/sql",
        icon: Table2,
        emptyHint: "Configure a connection",
    },
];

/**
 * One card per service — merges what used to be three overlapping sections
 * (health tiles, resource rows, tool cards) into a single per-service surface:
 * connectivity status, every configured entity (not just the first), and a pin.
 */
export function ServiceGrid({
    health,
}: {
    health: Record<string, ServiceHealth>;
}) {
    const { data: profile } = useProfile();
    const togglePinned = useTogglePinnedResource();
    const navigate = useNavigate();

    const pinnedResources = profile?.config.favoriteResources ?? [];
    const isPinned = (key: string) =>
        pinnedResources.some((f) => f.snapshot.resource.key === key);

    const handlePin = (service: ServiceDef) => {
        if (!profile) return;
        const favorite: FavoriteResource = {
            name: service.name,
            pinnedAt: new Date().toISOString(),
            snapshot: {
                resource: {
                    key: service.key,
                    area: service.key,
                    kind: "service",
                    displayName: service.name,
                    displayPath: service.to,
                    icon: service.key,
                    metadata: {},
                },
                restoreState: {},
                capturedAt: new Date().toISOString(),
            },
        };
        togglePinned.mutate({
            resource: favorite,
            pinned: !isPinned(service.key),
        });
    };

    return (
        <div data-testid="health-tiles">
            <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-2"
                data-testid="service-cards"
            >
                {SERVICES.map((service) => {
                    const svc = health[service.key];
                    const status = svc?.connectivity ?? "checking";
                    const entities = svc?.entities ?? [];
                    const configured = status !== "not-configured";
                    const Icon = service.icon;
                    const pinned = isPinned(service.key);
                    const countLabel =
                        service.key === "aks"
                            ? configured
                                ? "Cluster configured"
                                : service.emptyHint
                            : `${entities.length} ${countUnit(service.key, entities.length)}`;

                    return (
                        <div
                            key={service.key}
                            role="link"
                            tabIndex={0}
                            onClick={() => navigate(service.to)}
                            onKeyDown={(e) =>
                                e.key === "Enter" && navigate(service.to)
                            }
                            className="glass-card relative cursor-pointer rounded-xl p-4 transition-all hover:-translate-y-px hover:border-border-strong hover:shadow-md"
                            data-testid={`service-card-${service.key}`}
                        >
                            <button
                                type="button"
                                aria-label={
                                    pinned
                                        ? `Unpin ${service.name}`
                                        : `Pin ${service.name} to dashboard`
                                }
                                title={
                                    pinned
                                        ? "Unpin from dashboard"
                                        : "Pin to dashboard"
                                }
                                data-testid={`pin-resource-${service.key}`}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    handlePin(service);
                                }}
                                disabled={togglePinned.isPending}
                                className={`absolute right-3 top-3 rounded-md p-1.5 transition-colors hover:bg-accent ${pinned ? "text-primary" : "text-muted-foreground"}`}
                            >
                                {pinned ? (
                                    <PinOff className="h-4 w-4" />
                                ) : (
                                    <Pin className="h-4 w-4" />
                                )}
                            </button>

                            <div className="flex items-center gap-3 pr-8">
                                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-[var(--radius-md)] bg-icon-tile text-icon-tile-ink">
                                    <Icon className="h-[18px] w-[18px]" />
                                </span>
                                <div className="min-w-0 flex-1">
                                    <h3 className="font-semibold">
                                        {service.name}
                                    </h3>
                                    <p className="truncate text-xs text-muted-foreground">
                                        {countLabel}
                                    </p>
                                </div>
                                <StatusBadge
                                    status={status}
                                    testId={`health-tile-${service.key}`}
                                />
                            </div>

                            {configured && entities.length > 0 && (
                                <ul className="mt-3 space-y-1">
                                    {entities.slice(0, 4).map((entity) => (
                                        <li
                                            key={entity.id}
                                            className="flex items-center gap-1.5 truncate text-xs text-muted-foreground"
                                            title={entity.label}
                                        >
                                            <span
                                                className={`h-1.5 w-1.5 shrink-0 rounded-full ${
                                                    entity.connected === null
                                                        ? "bg-muted-foreground"
                                                        : entity.connected
                                                          ? "bg-success"
                                                          : "bg-destructive"
                                                }`}
                                            />
                                            <span className="truncate">
                                                {entity.label}
                                            </span>
                                            {entity.detail && (
                                                <span className="shrink-0 text-muted-foreground/70">
                                                    · {entity.detail}
                                                </span>
                                            )}
                                        </li>
                                    ))}
                                    {entities.length > 4 && (
                                        <li className="text-xs text-primary">
                                            +{entities.length - 4} more
                                        </li>
                                    )}
                                </ul>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
}

function countUnit(key: string, count: number): string {
    const units: Record<string, [string, string]> = {
        "service-bus": ["namespace", "namespaces"],
        aks: ["entity", "entities"],
        redis: ["cache", "caches"],
        storage: ["account", "accounts"],
        sql: ["connection", "connections"],
    };
    const [singular, plural] = units[key] ?? ["entity", "entities"];
    return count === 1 ? singular : plural;
}
