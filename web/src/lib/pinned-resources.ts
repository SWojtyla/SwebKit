import {
    MessageSquare,
    Ship,
    Database,
    Table2,
    FolderOpen,
    Pin,
    type LucideIcon,
} from "lucide-react";
import type { FavoriteResource } from "./types";

// ── Pinned resources (ux-power-pack §2) ──────────────────────────────────────
//
// `profile.config.favoriteResources` is the one persisted pin model — the
// dashboard's PinnedShortcuts, the sidebar's PinnedRail and the command palette
// all read it. The contract settled here: `snapshot.resource.displayPath` is a
// *complete in-app URL* (path + query), so a pin navigates back to exactly the
// resource it was pinned from. Legacy pins (migrated server-side from
// `favoriteEntities`/`serviceBusEntityLinks`) carry a human display path like
// `alias/queue` instead — `resolveFavoriteTarget` reconstructs a canonical URL
// for those from the snapshot's area + metadata rather than dropping them.

/** What a pin resolves to: `to` feeds react-router's navigate; `state` carries
 *  the deep-link payloads the same pages already consume (state.cacheId etc.). */
export interface FavoriteTarget {
    to: string;
    state?: Record<string, unknown>;
}

export function isFavoritePinned(
    favorites: FavoriteResource[] | undefined | null,
    key: string,
): boolean {
    return (favorites ?? []).some((f) => f.snapshot.resource.key === key);
}

/** Builds a `FavoriteResource` whose `displayPath` is a complete URL — the form
 *  PinnedRail/palette navigate to directly without reconstruction. */
export function makeFavoriteResource(opts: {
    key: string;
    area: string;
    kind: string;
    name: string;
    displayPath: string;
    summary?: string | null;
    icon?: string | null;
    metadata?: Record<string, string>;
    restoreState?: Record<string, string>;
}): FavoriteResource {
    const now = new Date().toISOString();
    return {
        name: opts.name,
        pinnedAt: now,
        snapshot: {
            resource: {
                key: opts.key,
                area: opts.area,
                kind: opts.kind,
                displayName: opts.name,
                displayPath: opts.displayPath,
                summary: opts.summary ?? null,
                icon: opts.icon ?? null,
                metadata: opts.metadata ?? {},
            },
            restoreState: opts.restoreState ?? {},
            capturedAt: now,
        },
    };
}

// ── Per-surface pin factories ────────────────────────────────────────────────

export function pinServiceBusEntity(
    nsId: string,
    nsLabel: string,
    entity: { entityPath: string; name: string },
): FavoriteResource {
    return makeFavoriteResource({
        key: `service-bus:${nsId}:${entity.entityPath}`,
        area: "service-bus",
        kind: "entity",
        name: entity.name || entity.entityPath,
        displayPath: `/service-bus?ns=${encodeURIComponent(nsId)}&entity=${encodeURIComponent(entity.entityPath)}&entityName=${encodeURIComponent(entity.name || entity.entityPath)}`,
        summary: nsLabel,
        icon: "📨",
        metadata: { namespaceId: nsId, entityPath: entity.entityPath },
        restoreState: { namespaceId: nsId, entityPath: entity.entityPath },
    });
}

export function pinRedisCache(cache: {
    id: string;
    displayName?: string;
    cacheName?: string;
}): FavoriteResource {
    const name = cache.displayName || cache.cacheName || cache.id;
    return makeFavoriteResource({
        key: `redis:cache:${cache.id}`,
        area: "redis",
        kind: "cache",
        name,
        displayPath: `/redis?cache=${encodeURIComponent(cache.id)}`,
        icon: "🗄",
        metadata: { cacheId: cache.id },
        restoreState: { cacheId: cache.id },
    });
}

export function pinStorageAccount(account: {
    id: string;
    displayName?: string;
    accountName?: string;
}): FavoriteResource {
    const name = account.displayName || account.accountName || account.id;
    return makeFavoriteResource({
        key: `storage:account:${account.id}`,
        area: "storage",
        kind: "account",
        name,
        displayPath: `/storage?account=${encodeURIComponent(account.id)}`,
        icon: "📦",
        metadata: { accountId: account.id },
        restoreState: { accountId: account.id },
    });
}

/** Pins the AKS namespace selection — a single namespace or the joined
 *  multi-select. Selections are context-scoped (`contexts[0]` is the primary):
 *  the deep link carries the composite `ns` param plus `ctxs` for attached
 *  clusters, so a pin restores the merged multi-context view it came from. */
export function pinAksNamespaces(
    contexts: string[],
    selections: { context: string; namespace: string }[],
): FavoriteResource {
    const primary = contexts[0] ?? null;
    const attached = contexts.slice(1);
    const label = selections
        .map((s) =>
            s.context === primary ? s.namespace : `${s.context}:${s.namespace}`,
        )
        .join(", ");
    // Whole-value encoding: `ns=a,b%3Actx` decodes once at the page's URL parser
    // into the codec's token list — identical to how the workspace serializes it.
    const nsParam = encodeURIComponent(
        selections
            .map((s) =>
                !s.context || s.context === primary
                    ? s.namespace
                    : `${s.context}:${s.namespace}`,
            )
            .join(","),
    );
    const ctxsParam = attached.length
        ? `&ctxs=${attached.map(encodeURIComponent).join(",")}`
        : "";
    const keySel = selections
        .map((s) => `${s.context}/${s.namespace}`)
        .join(",");
    return makeFavoriteResource({
        key: `aks:namespaces:${contexts.join("+")}:${keySel}`,
        area: "aks",
        kind: "namespaces",
        name: label,
        displayPath: `/aks?ns=${nsParam}${ctxsParam}`,
        summary: contexts.join(" + ") || undefined,
        icon: "☸",
        metadata: {
            namespaces: keySel,
            ...(contexts.length ? { contexts: contexts.join(",") } : {}),
        },
        restoreState: { namespaces: keySel },
    });
}

export function pinSqlConnection(connection: {
    id: string;
    displayName?: string;
    server?: string;
}): FavoriteResource {
    const name = connection.displayName || connection.server || connection.id;
    return makeFavoriteResource({
        key: `sql:connection:${connection.id}`,
        area: "sql",
        kind: "connection",
        name,
        displayPath: `/sql?connection=${encodeURIComponent(connection.id)}`,
        summary: connection.server ?? undefined,
        icon: "🗃",
        metadata: { connectionId: connection.id },
        restoreState: { connectionId: connection.id },
    });
}

// ── Resolution ───────────────────────────────────────────────────────────────

const AREA_ROUTES: Record<string, string> = {
    "service-bus": "/service-bus",
    aks: "/aks",
    redis: "/redis",
    sql: "/sql",
    storage: "/storage",
};

/**
 * Where a pin navigates. URL-shaped `displayPath` (starts with `/`) is used
 * verbatim — that's the contract new pins are written with. Legacy pins carry a
 * human display path (`alias/queue`) instead; those are reconstructed from the
 * snapshot's area + metadata/restoreState, so a pin written before the URL
 * convention still lands on the right resource.
 */
export function resolveFavoriteTarget(
    favorite: FavoriteResource,
): FavoriteTarget | null {
    const resource = favorite.snapshot.resource;
    const displayPath = resource.displayPath?.trim();
    if (displayPath?.startsWith("/")) return { to: displayPath };

    const meta = resource.metadata ?? {};
    const restore = favorite.snapshot.restoreState ?? {};
    const area = resource.area?.toLowerCase();

    if (area === "service-bus") {
        const nsId = meta.namespaceId ?? restore.namespaceId;
        const entityPath = meta.entityPath ?? restore.entityPath;
        if (nsId && entityPath) {
            const name =
                entityPath.split("/").filter(Boolean).pop() ?? entityPath;
            return {
                to: `/service-bus?ns=${encodeURIComponent(nsId)}&entity=${encodeURIComponent(entityPath)}&entityName=${encodeURIComponent(name)}`,
            };
        }
        if (nsId) {
            return { to: `/service-bus?ns=${encodeURIComponent(nsId)}` };
        }
        return { to: "/service-bus" };
    }
    if (area === "aks") {
        const namespaces = meta.namespaces ?? restore.namespaces;
        if (namespaces)
            return { to: `/aks?ns=${encodeURIComponent(namespaces)}` };
        const ns = meta.namespace ?? restore.namespace;
        return { to: `/aks${ns ? `?ns=${encodeURIComponent(ns)}` : ""}` };
    }
    if (area === "redis") {
        const cacheId = meta.cacheId ?? restore.cacheId;
        return {
            to: `/redis${cacheId ? `?cache=${encodeURIComponent(cacheId)}` : ""}`,
        };
    }
    if (area === "sql") {
        const connectionId = meta.connectionId ?? restore.connectionId;
        return {
            to: `/sql${connectionId ? `?connection=${encodeURIComponent(connectionId)}` : ""}`,
        };
    }
    if (area === "storage") {
        const accountId = meta.accountId ?? restore.accountId;
        return {
            to: `/storage${accountId ? `?account=${encodeURIComponent(accountId)}` : ""}`,
        };
    }

    const route = area ? AREA_ROUTES[area] : undefined;
    return route ? { to: route } : null;
}

/** Icon for a pin's resource — keyed on `snapshot.resource.area` (the legacy
 *  `resource.icon` field holds emoji like "📨" and stays display-only). */
export function pinnedAreaIcon(area: string | undefined): LucideIcon {
    switch (area?.toLowerCase()) {
        case "service-bus":
            return MessageSquare;
        case "aks":
            return Ship;
        case "redis":
            return Database;
        case "sql":
            return Table2;
        case "storage":
            return FolderOpen;
        default:
            return Pin;
    }
}
