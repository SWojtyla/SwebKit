import { useMemo } from "react";
import { Link } from "react-router";
import { Activity, FolderOpen, Ship, TrendingUp } from "lucide-react";
import {
    useAksDeployments,
    useAksNamespaces,
    useAksPods,
    useProfile,
} from "@/lib/hooks";
import type { ServiceHealth } from "./useServiceHealth";

/**
 * Small live-metric tiles. Counts aggregate across every configured entity
 * (all storage accounts' containers, every Redis cache's hit rate) — the
 * previous version silently sampled only `entities[0]` and ignored the rest.
 */
export function WatchTiles({
    health,
}: {
    health: Record<string, ServiceHealth>;
}) {
    const { data: profile } = useProfile();

    const configuredAksNs =
        profile?.config.aksConfig?.defaultNamespace?.trim() || null;
    const aksNamespaces = useAksNamespaces(!!profile && !configuredAksNs);
    const activeAksNs = configuredAksNs ?? aksNamespaces.data?.[0] ?? null;
    const aksDeployments = useAksDeployments(activeAksNs ?? null);
    const aksPods = useAksPods(activeAksNs ?? null);

    // Per-entity details already fetched by useServiceHealth share the same
    // query cache — these reads add no new requests.
    const containerCount = useMemo(() => {
        const entities = health.storage?.entities ?? [];
        let total = 0;
        let known = false;
        for (const e of entities) {
            const match = e.detail?.match(/^(\d+) containers$/);
            if (match) {
                total += parseInt(match[1], 10);
                known = true;
            }
        }
        return known ? total : null;
    }, [health.storage]);

    const meanHitRatio = useMemo(() => {
        const ratios = (health.redis?.entities ?? [])
            .map((e) => e.hitRatio)
            .filter((r): r is number => r !== undefined);
        if (ratios.length === 0) return null;
        const mean = ratios.reduce((a, b) => a + b, 0) / ratios.length;
        return (mean * 100).toFixed(1);
    }, [health.redis]);

    const tiles = [
        {
            label: "Deployments",
            value: aksDeployments.data?.length ?? 0,
            to: "/aks?tab=deployments",
            icon: Ship,
            testId: "watch-tile-deployments",
        },
        {
            label: "Pods",
            value: aksPods.data?.length ?? 0,
            to: "/aks?tab=pods",
            icon: Activity,
            testId: "watch-tile-pods",
        },
        {
            label: "Containers",
            value: containerCount ?? "-",
            to: "/storage",
            icon: FolderOpen,
            testId: "watch-tile-containers",
        },
        {
            label: "Cache Hit Rate",
            value:
                meanHitRatio !== null ? `${meanHitRatio}%` : "-",
            to: "/redis?tab=info",
            icon: TrendingUp,
            testId: "watch-tile-cache-hit-rate",
        },
    ];

    return (
        <div
            className="grid grid-cols-2 gap-3 lg:grid-cols-4"
            data-testid="watch-tiles"
        >
            {tiles.map((tile) => {
                const Icon = tile.icon;
                return (
                    <Link
                        key={tile.label}
                        to={tile.to}
                        data-testid={tile.testId}
                        className="glass-card rounded-xl p-3 transition-all hover:border-primary hover:shadow-md"
                    >
                        <div className="flex items-center gap-2">
                            <Icon className="h-4 w-4 text-primary" />
                            <span className="text-xs text-muted-foreground">
                                {tile.label}
                            </span>
                        </div>
                        <div className="mt-1 text-xl font-semibold">
                            {tile.value}
                        </div>
                    </Link>
                );
            })}
        </div>
    );
}
