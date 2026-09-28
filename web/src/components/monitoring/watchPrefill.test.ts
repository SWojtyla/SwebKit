import { describe, expect, it } from "vitest";
import { buildWatchPrefill } from "./watchPrefill";

// "Watch this" prefill (agent-colleague item 3): evidence watch hints become
// MonitoringAlertRule drafts for the existing dialog — only dialog-supported
// sources, only dialog-known param names.

describe("buildWatchPrefill", () => {
    it("builds an AKS pod-restart draft with namespace and threshold", () => {
        expect(
            buildWatchPrefill({
                source: "AksPodRestartRate",
                params: {
                    namespace: "payments",
                    kubeconfigContext: "prod-aks",
                    restartThreshold: 3,
                },
            }),
        ).toEqual({
            source: "AksPodRestartRate",
            aksPodParams: {
                namespace: "payments",
                kubeconfigContext: "prod-aks",
                restartThreshold: 3,
                healthScoreThreshold: undefined,
            },
        });
    });

    it("maps a generic threshold to the field the source consumes", () => {
        expect(
            buildWatchPrefill({
                source: "AksNamespaceHealthScore",
                params: { namespace: "payments", threshold: 0.4 },
            })?.aksPodParams?.healthScoreThreshold,
        ).toBe(0.4);
        expect(
            buildWatchPrefill({
                source: "ServiceBusDlqDepth",
                params: {
                    namespaceConnectionAlias: "prod-sb",
                    entityPath: "orders",
                    threshold: 10,
                },
            })?.serviceBusParams?.messageCountThreshold,
        ).toBe(10);
        expect(
            buildWatchPrefill({
                source: "RedisMemoryUsage",
                params: { connectionAlias: "prod-cache", threshold: 90 },
            })?.redisAlertParams?.memoryUsageThresholdPercent,
        ).toBe(90);
        expect(
            buildWatchPrefill({
                source: "RedisConnectedClients",
                params: { connectionAlias: "prod-cache", threshold: 500 },
            })?.redisAlertParams?.clientCountLowerBound,
        ).toBe(500);
    });

    it("accepts the shorthand ns/cache/entity param aliases the prompt documents", () => {
        const prefill = buildWatchPrefill({
            source: "ServiceBusDeadSubscription",
            params: { ns: "prod-sb", entity: "topic1/sub1" },
        });
        expect(prefill?.serviceBusParams).toEqual({
            namespaceConnectionAlias: "prod-sb",
            entityPath: "topic1/sub1",
            messageCountThreshold: undefined,
        });
        expect(
            buildWatchPrefill({
                source: "RedisMemoryUsage",
                params: { cache: "prod-cache" },
            })?.redisAlertParams?.connectionAlias,
        ).toBe("prod-cache");
    });

    it("rejects unsupported and invented sources", () => {
        expect(buildWatchPrefill({ source: "StorageBlobCount" })).toBeNull();
        expect(
            buildWatchPrefill({ source: "KqlQuery", params: { q: "..." } }),
        ).toBeNull();
        expect(buildWatchPrefill({ source: "" })).toBeNull();
        expect(buildWatchPrefill(null)).toBeNull();
        expect(buildWatchPrefill(undefined)).toBeNull();
        expect(buildWatchPrefill({})).toBeNull();
    });

    it("drops unknown params and non-scalar values", () => {
        const prefill = buildWatchPrefill({
            source: "AksPodHealth",
            params: {
                namespace: "payments",
                evil: { nested: "x" },
                unknownParam: "y",
            },
        });
        expect(prefill?.aksPodParams?.namespace).toBe("payments");
        expect(prefill).not.toHaveProperty("evil");
        expect(prefill).not.toHaveProperty("unknownParam");
    });

    it("keeps name and a valid severity; ignores invalid severity", () => {
        const ok = buildWatchPrefill({
            source: "AksPodHealth",
            params: { name: "watch pods", severity: "Critical" },
        });
        expect(ok?.name).toBe("watch pods");
        expect(ok?.severity).toBe("Critical");

        const bad = buildWatchPrefill({
            source: "AksPodHealth",
            params: { severity: "Fatal" },
        });
        expect(bad?.severity).toBeUndefined();
    });
});
