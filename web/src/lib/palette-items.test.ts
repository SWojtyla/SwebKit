import { describe, expect, it, vi } from "vitest";
import {
    buildPaletteItems,
    staticCommandPaletteItems,
    type CommandPaletteItem,
} from "./palette-items";
import type {
    KubeContextInfo,
    ProfileData,
    SavedSqlQuery,
    SbEntityInfo,
    ServiceBusNamespace,
} from "./types";
import { makeFavoriteResource } from "./pinned-resources";

function sbNamespace(id: string, alias: string): ServiceBusNamespace {
    return {
        id,
        alias,
        fullyQualifiedNamespace: `${alias}.servicebus.windows.net`,
        authMode: "DefaultAzureCredential",
        credentialKey: "",
        transportType: "Amqp",
        createdAt: "2024-01-01T00:00:00Z",
    };
}

function sbEntity(entityPath: string, overrides?: Partial<SbEntityInfo>): SbEntityInfo {
    return {
        name: entityPath,
        entityPath,
        stats: null,
        isDisabled: false,
        isTopic: false,
        isSubscription: entityPath.includes("/subscriptions/"),
        topicName: null,
        subscriptionDeadLetterCount: null,
        requiresSession: false,
        ...overrides,
    };
}

function profileWith(overrides: Record<string, unknown>): ProfileData {
    return {
        schemaVersion: 1,
        serviceBusNamespaces: [],
        messageTemplates: [],
        config: {
            name: "test",
            isProduction: false,
            aksConfig: null,
            redisConfig: null,
            sqlConfig: null,
            storageAccounts: [],
            observabilityConfig: null,
            favoriteEntities: [],
            favoriteResources: [],
            keyVaults: [],
            maps: [],
            topology: { nodes: [], edges: [] },
            ...overrides,
        },
    } as unknown as ProfileData;
}

function findById(items: CommandPaletteItem[], id: string) {
    return items.find((i) => i.id === id);
}

describe("buildPaletteItems", () => {
    it("always includes the static nav entries", () => {
        const items = buildPaletteItems({});
        expect(items.length).toBeGreaterThanOrEqual(
            staticCommandPaletteItems.length,
        );
        expect(findById(items, "dashboard")?.to).toBe("/");
        expect(findById(items, "settings-sql")?.state).toEqual({ tab: "sql" });
    });

    it("emits canonical /service-bus?ns=&entity= links for fan-out entities", () => {
        const ns = sbNamespace("ns-1", "Prod");
        const items = buildPaletteItems({
            sbEntities: [
                {
                    namespace: ns,
                    entities: [
                        sbEntity("orders"),
                        sbEntity("billing", { isTopic: true }),
                        sbEntity("billing/subscriptions/audit", {
                            isTopic: false,
                        }),
                    ],
                },
            ],
        });
        const queue = findById(items, "sb-entity-ns-1-orders");
        expect(queue?.to).toBe(
            "/service-bus?ns=ns-1&entity=orders&entityName=orders",
        );
        const topic = findById(items, "sb-entity-ns-1-billing");
        expect(topic?.to).toBe(
            "/service-bus?ns=ns-1&entity=billing&entityName=billing",
        );
        const sub = findById(
            items,
            "sb-entity-ns-1-billing/subscriptions/audit",
        );
        expect(sub?.to).toBe(
            "/service-bus?ns=ns-1&entity=billing%2Fsubscriptions%2Faudit&entityName=audit",
        );
    });

    it("marks DLQ-bearing entities in the subtitle", () => {
        const items = buildPaletteItems({
            sbEntities: [
                {
                    namespace: sbNamespace("ns-1", "Prod"),
                    entities: [
                        sbEntity("dead", {
                            stats: {
                                activeMessageCount: 0,
                                deadLetterMessageCount: 7,
                                scheduledMessageCount: 0,
                                transferCount: 0,
                                updatedAt: null,
                            },
                        }),
                    ],
                },
            ],
        });
        expect(findById(items, "sb-entity-ns-1-dead")?.subtitle).toContain(
            "7 in DLQ",
        );
    });

    it("emits AKS context-switch items carrying state.context, skipping the current one", () => {
        const contexts: KubeContextInfo[] = [
            { name: "prod", cluster: "c", user: "u", namespace: "web", isCurrent: false },
            { name: "dev", cluster: "c2", user: "u2", namespace: null, isCurrent: true },
        ];
        const items = buildPaletteItems({ aksContexts: contexts });
        const prod = findById(items, "aks-context-prod");
        expect(prod?.to).toBe("/aks");
        expect(prod?.state).toEqual({ context: "prod" });
        expect(findById(items, "aks-context-dev")).toBeUndefined();
    });

    it("emits saved SQL queries that load text via state.sql and target their connection", () => {
        const savedQueries: SavedSqlQuery[] = [
            {
                id: "q1",
                name: "Top orders",
                folder: "reports",
                sql: "SELECT TOP 10 * FROM orders",
                connectionId: "conn-9",
                createdAt: "2024-01-01T00:00:00Z",
                updatedAt: "2024-01-01T00:00:00Z",
            },
        ];
        const items = buildPaletteItems({ savedQueries });
        const item = findById(items, "sql-query-q1");
        expect(item?.to).toBe("/sql?connection=conn-9");
        expect(item?.state).toEqual({
            sql: { text: "SELECT TOP 10 * FROM orders" },
        });
        expect(item?.subtitle).toContain("reports");
    });

    it("emits pinned resources resolved to their displayPath targets", () => {
        const pinned = makeFavoriteResource({
            key: "redis:cache:c-1",
            area: "redis",
            kind: "cache",
            name: "Main cache",
            displayPath: "/redis?cache=c-1",
        });
        const items = buildPaletteItems({ pinnedResources: [pinned] });
        const item = findById(items, "pin-redis:cache:c-1");
        expect(item?.to).toBe("/redis?cache=c-1");
        expect(item?.subtitle).toContain("Pinned");
    });

    it("skips pinned resources that resolve to nothing", () => {
        const orphan = makeFavoriteResource({
            key: "x",
            area: "nope",
            kind: "thing",
            name: "orphan",
            displayPath: "not-a-url",
        });
        const items = buildPaletteItems({ pinnedResources: [orphan] });
        expect(findById(items, "pin-x")).toBeUndefined();
    });

    it("emits action items whose run() delegates to the injected actions", () => {
        const navigate = vi.fn();
        const toggleDemoMode = vi.fn();
        const items = buildPaletteItems(
            {},
            { navigate, toggleDemoMode, isDemoMode: false },
        );
        const demo = findById(items, "action-toggle-demo");
        expect(demo?.type).toBe("action");
        expect(demo?.label).toBe("Enable demo mode");
        demo?.run?.();
        expect(toggleDemoMode).toHaveBeenCalledOnce();

        const newReq = findById(items, "action-new-api-request");
        newReq?.run?.();
        expect(navigate).toHaveBeenCalledWith("/api-client", {
            state: { newRequest: true },
        });

        const sbMsg = findById(items, "action-new-sb-message");
        sbMsg?.run?.();
        expect(navigate).toHaveBeenCalledWith("/service-bus", {
            state: { compose: true },
        });

        const sql = findById(items, "action-run-sql");
        sql?.run?.();
        expect(navigate).toHaveBeenCalledWith("/sql");
    });

    it("labels the demo-mode action for the current state and omits actions without deps", () => {
        const on = buildPaletteItems({}, {
            navigate: vi.fn(),
            toggleDemoMode: vi.fn(),
            isDemoMode: true,
        });
        expect(findById(on, "action-toggle-demo")?.label).toBe(
            "Disable demo mode",
        );
        const off = buildPaletteItems({});
        expect(findById(off, "action-toggle-demo")).toBeUndefined();
    });

    it("keeps the state.cacheId fallback alongside the canonical ?cache= param", () => {
        const profile = profileWith({
            redisConfig: {
                caches: [
                    { id: "c-1", displayName: "Main", connectionString: "x" },
                ],
                activeCacheId: "c-1",
                namespaceSeparator: ":",
            },
        });
        const items = buildPaletteItems({ profile });
        const item = findById(items, "redis-cache-c-1");
        expect(item?.to).toBe("/redis?cache=c-1");
        expect(item?.state).toEqual({ cacheId: "c-1" });
    });
});
