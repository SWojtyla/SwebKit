import { describe, it, expect } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { findCachedEntityStats, invalidateServiceBusQueries } from "./useServiceBus";
import type { SbEntityInfo, SbEntityStats } from "../types";

/// `invalidateServiceBusQueries` is the fix for the Service Bus Refresh command's
/// `invalidateQueries({ queryKey: ["sb-"] })`, which matched nothing — TanStack Query compares
/// query keys element by element, not by string prefix, and every real key here is
/// `["sb-peek", nsId, entityPath, count]` and friends. This is the same class of bug documented
/// in `lib/aks-query-keys.ts` for `"aks-"`. Exporting the helper (previously module-private) lets
/// `ServiceBusPage.tsx` reuse the real key set instead of re-deriving it.

function seed(qc: QueryClient, keys: unknown[][]) {
  for (const key of keys) {
    qc.setQueryData(key, { seeded: true });
  }
}

function isInvalidated(qc: QueryClient, key: unknown[]): boolean {
  return qc.getQueryState(key)?.isInvalidated ?? false;
}

describe("invalidateServiceBusQueries", () => {
  const topologyKeys = (nsId: string) => [
    ["sb-queues", nsId],
    ["sb-topics", nsId],
    ["sb-subs", nsId, "some-topic"],
  ];

  it("invalidates every query scoped to the given entity", () => {
    const qc = new QueryClient();
    const nsId = "ns-1";
    const entityPath = "orders";

    seed(qc, [
      ["sb-peek", nsId, entityPath, 50],
      ["sb-dlq", nsId, entityPath, 50],
      ["sb-entity-stats", nsId, entityPath],
      ["sb-scheduled", nsId, entityPath],
    ]);

    invalidateServiceBusQueries(qc, nsId, entityPath);

    expect(isInvalidated(qc, ["sb-peek", nsId, entityPath, 50])).toBe(true);
    expect(isInvalidated(qc, ["sb-dlq", nsId, entityPath, 50])).toBe(true);
    expect(isInvalidated(qc, ["sb-entity-stats", nsId, entityPath])).toBe(true);
    expect(isInvalidated(qc, ["sb-scheduled", nsId, entityPath])).toBe(true);
  });

  it("leaves the entity tree alone by default", () => {
    // Sending, completing, purging and resubmitting change message *counts*, not which entities
    // exist. Invalidating `sb-subs` in particular re-fires one request per topic in the tree, so a
    // single message send used to replay the whole namespace fan-out.
    const qc = new QueryClient();
    const nsId = "ns-1";
    seed(qc, topologyKeys(nsId));

    invalidateServiceBusQueries(qc, nsId, "orders");

    for (const key of topologyKeys(nsId)) {
      expect(isInvalidated(qc, key)).toBe(false);
    }
  });

  it("invalidates the entity tree when the caller asks for it", () => {
    // The explicit Refresh action opts in: a deployment may have added or removed entities since
    // the tree loaded, and that is the one moment worth paying the fan-out for.
    const qc = new QueryClient();
    const nsId = "ns-1";
    seed(qc, topologyKeys(nsId));

    invalidateServiceBusQueries(qc, nsId, "orders", { includeTopology: true });

    for (const key of topologyKeys(nsId)) {
      expect(isInvalidated(qc, key)).toBe(true);
    }
  });

  it("does not invalidate an unrelated namespace's queries or non-Service-Bus queries", () => {
    const qc = new QueryClient();
    const nsId = "ns-1";
    const entityPath = "orders";

    seed(qc, [
      ["sb-peek", "other-ns", entityPath, 50],
      ["sb-templates"],
      ["aks-pods", "default"],
    ]);

    invalidateServiceBusQueries(qc, nsId, entityPath);

    expect(isInvalidated(qc, ["sb-peek", "other-ns", entityPath, 50])).toBe(false);
    expect(isInvalidated(qc, ["sb-templates"])).toBe(false);
    expect(isInvalidated(qc, ["aks-pods", "default"])).toBe(false);
  });

  it("is exactly what the literal-key bug ([\"sb-\"]) failed to match", () => {
    // Regression guard: `invalidateQueries({ queryKey: ["sb-"] })` never matched anything because
    // "sb-" as a full array element never equals "sb-peek" — this asserts the real fix does.
    const qc = new QueryClient();
    seed(qc, [["sb-peek", "ns-1", "orders", 50]]);

    qc.invalidateQueries({ queryKey: ["sb-"] });
    expect(isInvalidated(qc, ["sb-peek", "ns-1", "orders", 50])).toBe(false);

    invalidateServiceBusQueries(qc, "ns-1", "orders");
    expect(isInvalidated(qc, ["sb-peek", "ns-1", "orders", 50])).toBe(true);
  });
});

describe("findCachedEntityStats", () => {
  const stats = (active: number, dlq = 0): SbEntityStats => ({
    activeMessageCount: active,
    deadLetterMessageCount: dlq,
    scheduledMessageCount: 0,
    transferCount: 0,
    updatedAt: null,
  });

  const entity = (entityPath: string, entityStats: SbEntityStats | null): SbEntityInfo => ({
    name: entityPath,
    entityPath,
    stats: entityStats,
    isDisabled: false,
    isTopic: false,
    isSubscription: false,
    topicName: null,
    subscriptionDeadLetterCount: null,
  });

  it("finds a queue's counts already loaded by the tree", () => {
    const qc = new QueryClient();
    qc.setQueryData(["sb-queues", "ns-1"], [entity("orders", stats(7))]);

    expect(findCachedEntityStats(qc, "ns-1", "orders")?.activeMessageCount).toBe(7);
  });

  it("finds a subscription's counts, whose list is keyed per topic", () => {
    // `sb-subs` keys carry the topic as a third element, so this only works via prefix matching —
    // the caller does not know which topic's list an entity path came from.
    const qc = new QueryClient();
    qc.setQueryData(
      ["sb-subs", "ns-1", "orders"],
      [entity("orders/subscriptions/audit", stats(3, 2))],
    );

    expect(findCachedEntityStats(qc, "ns-1", "orders/subscriptions/audit")?.deadLetterMessageCount).toBe(2);
  });

  it("returns undefined when nothing is cached, so the query just fetches", () => {
    expect(findCachedEntityStats(new QueryClient(), "ns-1", "orders")).toBeUndefined();
  });

  it("ignores another namespace's entity of the same name", () => {
    const qc = new QueryClient();
    qc.setQueryData(["sb-queues", "ns-other"], [entity("orders", stats(99))]);

    expect(findCachedEntityStats(qc, "ns-1", "orders")).toBeUndefined();
  });

  it("skips a listed entity whose counts failed to load rather than reporting zeros", () => {
    const qc = new QueryClient();
    qc.setQueryData(["sb-queues", "ns-1"], [entity("orders", null)]);

    expect(findCachedEntityStats(qc, "ns-1", "orders")).toBeUndefined();
  });
});
