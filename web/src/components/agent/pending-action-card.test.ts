import { describe, expect, it } from "vitest";
import { describePendingActionOrigin, formatExpiryCountdown } from "./PendingActionCard";

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
