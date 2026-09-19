import { describe, it, expect } from "vitest";
import { isAlertRuleComplete } from "./alertRuleValidation";
import type { MonitoringAlertRule } from "../../lib/api";

function baseRule(overrides: Partial<MonitoringAlertRule> = {}): MonitoringAlertRule {
  return {
    id: "",
    name: "My rule",
    enabled: true,
    source: "AksPodHealth",
    severity: "Warning",
    intervalSeconds: 60,
    cooldownMinutes: 5,
    aksPodParams: null,
    serviceBusParams: null,
    redisAlertParams: null,
    aiInvestigationEnabled: true,
    ...overrides,
  };
}

describe("isAlertRuleComplete", () => {
  it("rejects a rule with no name regardless of source", () => {
    expect(isAlertRuleComplete(baseRule({ name: "" }))).toBe(false);
    expect(isAlertRuleComplete(baseRule({ name: "   " }))).toBe(false);
  });

  it("requires an AKS namespace for AKS sources", () => {
    expect(isAlertRuleComplete(baseRule({ source: "AksPodHealth", aksPodParams: null }))).toBe(false);
    expect(isAlertRuleComplete(baseRule({ source: "AksPodHealth", aksPodParams: { namespace: "" } }))).toBe(false);
    expect(
      isAlertRuleComplete(baseRule({ source: "AksNamespaceHealthScore", aksPodParams: { namespace: "default" } })),
    ).toBe(true);
  });

  it("requires both a Service Bus namespace alias and entity path", () => {
    expect(isAlertRuleComplete(baseRule({ source: "ServiceBusDlqDepth", serviceBusParams: null }))).toBe(false);
    expect(
      isAlertRuleComplete(
        baseRule({
          source: "ServiceBusDlqDepth",
          serviceBusParams: { namespaceConnectionAlias: "prod-sb", entityPath: "" },
        }),
      ),
    ).toBe(false);
    expect(
      isAlertRuleComplete(
        baseRule({
          source: "ServiceBusDlqDepth",
          serviceBusParams: { namespaceConnectionAlias: "", entityPath: "orders/queue" },
        }),
      ),
    ).toBe(false);
    expect(
      isAlertRuleComplete(
        baseRule({
          source: "ServiceBusActiveDepth",
          serviceBusParams: { namespaceConnectionAlias: "prod-sb", entityPath: "orders/queue" },
        }),
      ),
    ).toBe(true);
  });

  it("requires a Redis cache alias for Redis sources", () => {
    expect(isAlertRuleComplete(baseRule({ source: "RedisMemoryUsage", redisAlertParams: null }))).toBe(false);
    expect(
      isAlertRuleComplete(baseRule({ source: "RedisConnectedClients", redisAlertParams: { connectionAlias: "" } })),
    ).toBe(false);
    expect(
      isAlertRuleComplete(
        baseRule({ source: "RedisMemoryUsage", redisAlertParams: { connectionAlias: "prod-cache" } }),
      ),
    ).toBe(true);
  });
});
