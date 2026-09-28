import { describe, expect, it } from "vitest";
import {
    isFavoritePinned,
    makeFavoriteResource,
    pinAksNamespaces,
    pinRedisCache,
    pinServiceBusEntity,
    pinSqlConnection,
    pinStorageAccount,
    resolveFavoriteTarget,
} from "./pinned-resources";
import type { FavoriteResource } from "./types";

// The pin contract that PinnedRail, the palette and PinnedShortcuts all share:
// `displayPath` is a complete in-app URL, so navigating a pin is a verbatim
// `navigate(displayPath)` — never reconstructed, never appended to.

describe("makeFavoriteResource", () => {
    it("produces a snapshot with the resource fields and timestamps", () => {
        const fav = makeFavoriteResource({
            key: "k",
            area: "redis",
            kind: "cache",
            name: "My cache",
            displayPath: "/redis?cache=c1",
            metadata: { cacheId: "c1" },
        });
        expect(fav.snapshot.resource.key).toBe("k");
        expect(fav.snapshot.resource.displayPath).toBe("/redis?cache=c1");
        expect(fav.snapshot.resource.metadata).toEqual({ cacheId: "c1" });
        expect(fav.pinnedAt).toBeTruthy();
        expect(fav.snapshot.capturedAt).toBeTruthy();
    });
});

describe("isFavoritePinned", () => {
    const fav = makeFavoriteResource({
        key: "service-bus:ns1:queue-a",
        area: "service-bus",
        kind: "entity",
        name: "queue-a",
        displayPath: "/service-bus?ns=ns1&entity=queue-a",
    });
    it("matches on snapshot.resource.key", () => {
        expect(isFavoritePinned([fav], "service-bus:ns1:queue-a")).toBe(true);
        expect(isFavoritePinned([fav], "other")).toBe(false);
        expect(isFavoritePinned(undefined, "k")).toBe(false);
        expect(isFavoritePinned(null, "k")).toBe(false);
    });
});

describe("surface pin factories", () => {
    it("pinServiceBusEntity writes the canonical ns/entity URL", () => {
        const fav = pinServiceBusEntity("ns-1", "Prod", {
            entityPath: "orders",
            name: "orders",
        });
        expect(fav.snapshot.resource.key).toBe("service-bus:ns-1:orders");
        expect(resolveFavoriteTarget(fav)?.to).toBe(
            "/service-bus?ns=ns-1&entity=orders&entityName=orders",
        );
    });

    it("pinServiceBusEntity encodes subscription entity paths", () => {
        const fav = pinServiceBusEntity("ns-1", "Prod", {
            entityPath: "topic/subscriptions/sub",
            name: "sub",
        });
        expect(resolveFavoriteTarget(fav)?.to).toBe(
            "/service-bus?ns=ns-1&entity=topic%2Fsubscriptions%2Fsub&entityName=sub",
        );
    });

    it("pinRedisCache writes ?cache=", () => {
        const fav = pinRedisCache({ id: "c-1", displayName: "Main cache" });
        expect(resolveFavoriteTarget(fav)?.to).toBe("/redis?cache=c-1");
    });

    it("pinStorageAccount writes ?account=", () => {
        const fav = pinStorageAccount({ id: "a-1", accountName: "stprod" });
        expect(resolveFavoriteTarget(fav)?.to).toBe("/storage?account=a-1");
    });

    it("pinAksNamespaces joins multi-namespace selections", () => {
        const fav = pinAksNamespaces("prod-ctx", ["default", "app"]);
        expect(resolveFavoriteTarget(fav)?.to).toBe("/aks?ns=default%2Capp");
        expect(fav.snapshot.resource.metadata.context).toBe("prod-ctx");
    });

    it("pinSqlConnection writes ?connection=", () => {
        const fav = pinSqlConnection({ id: "s-1", displayName: "Reporting" });
        expect(resolveFavoriteTarget(fav)?.to).toBe("/sql?connection=s-1");
    });
});

describe("resolveFavoriteTarget", () => {
    it("uses a URL-shaped displayPath verbatim", () => {
        const fav = makeFavoriteResource({
            key: "x",
            area: "service-bus",
            kind: "entity",
            name: "n",
            displayPath: "/service-bus?ns=ns-9&entity=q",
        });
        expect(resolveFavoriteTarget(fav)).toEqual({
            to: "/service-bus?ns=ns-9&entity=q",
        });
    });

    it("reconstructs a canonical URL for legacy service-bus pins", () => {
        // Legacy favorites migrated server-side carry a human display path
        // ("alias/queue") — resolution falls back to snapshot metadata.
        const fav: FavoriteResource = {
            name: "orders",
            pinnedAt: "2024-01-01T00:00:00Z",
            snapshot: {
                resource: {
                    key: "service-bus:ns-1:orders",
                    area: "service-bus",
                    kind: "entity",
                    displayName: "orders",
                    displayPath: "Prod/orders",
                    metadata: { namespaceId: "ns-1", entityPath: "orders" },
                },
                restoreState: {},
                capturedAt: "2024-01-01T00:00:00Z",
            },
        };
        expect(resolveFavoriteTarget(fav)?.to).toBe(
            "/service-bus?ns=ns-1&entity=orders&entityName=orders",
        );
    });

    it("falls back to the area route when nothing else resolves", () => {
        const fav = makeFavoriteResource({
            key: "x",
            area: "sql",
            kind: "other",
            name: "n",
            displayPath: "not-a-url",
        });
        expect(resolveFavoriteTarget(fav)).toEqual({ to: "/sql" });
    });

    it("returns null for an unknown area with no usable path", () => {
        const fav = makeFavoriteResource({
            key: "x",
            area: "unknown-thing",
            kind: "other",
            name: "n",
            displayPath: "not-a-url",
        });
        expect(resolveFavoriteTarget(fav)).toBeNull();
    });
});
