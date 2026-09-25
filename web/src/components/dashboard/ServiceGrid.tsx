import { useNavigate } from "react-router";
import {
    AlertCircle,
    CheckCircle2,
    Database,
    FolderOpen,
    Loader2,
    MessageSquare,
    Pin,
    PinOff,
    Ship,
    Table2,
    XCircle,
} from "lucide-react";
import {
    useProfile,
    useTogglePinnedResource,
} from "@/lib/hooks";
import type { FavoriteResource } from "@/lib/types";
import type { ServiceConnectivity, ServiceHealth } from "./useServiceHealth";

const STATUS_META: Record<
    ServiceConnectivity,
    { label: string; className: string }
> = {
    "not-configured": { label: "Not configured", className: "text-muted-foreground" },
    checking: { label: "Checking…", className: "text-muted-foreground" },
    connected: { label: "Connected", className: "text-success" },
    degraded: { label: "Degraded", className: "text-warning" },
    unavailable: { label: "Unavailable", className: "text-destructive" },
};

function StatusIcon({ status }: { status: ServiceConnectivity }) {
    switch (status) {
        case "connected":
            return <CheckCircle2 className="h-4 w-4 text-success" />;
        case "degraded":
            return <AlertCircle className="h-4 w-4 text-warning" />;
        case "unavailable":
            return <XCircle className="h-4 w-4 text-destructive" />;
        case "checking":
            return (
                <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            );
        default:
            return <AlertCircle className="h-4 w-4 text-muted-foreground" />;
    }
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
                        className="glass-card relative cursor-pointer rounded-xl p-4 transition-all hover:border-primary hover:shadow-md"
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
                            <Icon className="h-6 w-6 text-primary" />
                            <div className="min-w-0">
                                <h3 className="font-semibold">
                                    {service.name}
                                </h3>
                                <p className="truncate text-xs text-muted-foreground">
                                    {countLabel}
                                </p>
                            </div>
                        </div>

                        <div
                            className="mt-3 flex items-center gap-2 text-xs"
                            data-testid={`health-tile-${service.key}`}
                        >
                            <StatusIcon status={status} />
                            <span className={STATUS_META[status].className}>
                                {STATUS_META[status].label}
                            </span>
                        </div>

                        {configured && entities.length > 0 && (
                            <ul className="mt-2 space-y-1">
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
