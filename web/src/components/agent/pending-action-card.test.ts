import { describe, expect, it } from "vitest";
import { describePendingActionOrigin, formatExpiryCountdown } from "./pending-actions";

describe("describePendingActionOrigin", () => {
  it("maps each known AgentActionType to its proposing feature area", () => {
    expect(describePendingActionOrigin({ type: "DeleteRequest", target: "Request 'Get token' (r1)" })).toBe(
      "API Client · Request 'Get token' (r1)",
    );
    expect(describePendingActionOrigin({ type: "DeleteRedisKey", target: "session:42" })).toBe(
      "Redis · session:42",
    );
    expect(describePendingActionOrigin({ type: "CopyBlob", target: "container/blob.txt" })).toBe(
      "Storage · container/blob.txt",
    );
    expect(describePendingActionOrigin({ type: "ExecuteSql", target: "orders" })).toBe(
      "SQL · orders",
    );
    expect(describePendingActionOrigin({ type: "ApplyAksYaml", target: "dev/Deployment/orders" })).toBe(
      "AKS · dev/Deployment/orders",
    );
  });

  it("falls back to a generic 'Agent' area for an unrecognized action type", () => {
    expect(describePendingActionOrigin({ type: "SomeFutureActionType", target: "widget-1" })).toBe(
      "Agent · widget-1",
    );
  });

  it("maps the monitoring-closed-loop remediation types to their feature areas", () => {
    expect(
      describePendingActionOrigin({ type: "RestartAksDeployment", target: "prod/Deployment/api" }),
    ).toBe("AKS · prod/Deployment/api");
    expect(
      describePendingActionOrigin({ type: "DeleteAksPod", target: "prod/Pod/api-7c9f" }),
    ).toBe("AKS · prod/Pod/api-7c9f");
    expect(
      describePendingActionOrigin({ type: "PurgeServiceBusDeadLetters", target: "orders · dead-letter" }),
    ).toBe("Service Bus · orders · dead-letter");
    expect(
      describePendingActionOrigin({ type: "ResubmitServiceBusDeadLetters", target: "orders · dead-letter" }),
    ).toBe("Service Bus · orders · dead-letter");
    expect(
      describePendingActionOrigin({ type: "FlushRedisDatabase", target: "cache-prod" }),
    ).toBe("Redis · cache-prod");
  });

  it("prefixes 'Alert investigation' when an opted-in background run parked the action", () => {
    // The provenance stamp is what distinguishes "the AI proposed this" from a chat proposal.
    expect(
      describePendingActionOrigin({
        type: "PurgeServiceBusDeadLetters",
        target: "orders · dead-letter",
        origin: "investigation",
      }),
    ).toBe("Alert investigation · Service Bus · orders · dead-letter");
  });

  it("treats any other (or absent) origin as an interactive proposal", () => {
    expect(
      describePendingActionOrigin({ type: "DeleteAksPod", target: "prod/Pod/x", origin: undefined }),
    ).toBe("AKS · prod/Pod/x");
  });
});

describe("formatExpiryCountdown", () => {
  const now = Date.parse("2026-01-01T00:00:00.000Z");

  it("renders whole seconds under a minute", () => {
    expect(formatExpiryCountdown(new Date(now + 47_000).toISOString(), now)).toBe("expires in 47s");
  });

  it("rounds up a partial second so it never shows 0s while still pending", () => {
    expect(formatExpiryCountdown(new Date(now + 200).toISOString(), now)).toBe("expires in 1s");
  });

  it("renders minutes and zero-padded seconds at or above a minute", () => {
    expect(formatExpiryCountdown(new Date(now + 90_000).toISOString(), now)).toBe("expires in 1m 30s");
    expect(formatExpiryCountdown(new Date(now + 125_000).toISOString(), now)).toBe("expires in 2m 05s");
  });

  it("reports 'expired' once the deadline has passed, never a negative countdown", () => {
    expect(formatExpiryCountdown(new Date(now - 1_000).toISOString(), now)).toBe("expired");
    expect(formatExpiryCountdown(new Date(now).toISOString(), now)).toBe("expired");
  });
});
