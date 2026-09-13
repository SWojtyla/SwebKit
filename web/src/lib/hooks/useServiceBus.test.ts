import { describe, it, expect } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { invalidateServiceBusQueries } from "./useServiceBus";

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
  it("invalidates every query scoped to the given entity, and the namespace's entity lists", () => {
    const qc = new QueryClient();
    const nsId = "ns-1";
    const entityPath = "orders";

    seed(qc, [
      ["sb-peek", nsId, entityPath, 50],
      ["sb-dlq", nsId, entityPath, 50],
      ["sb-entity-stats", nsId, entityPath],
      ["sb-queues", nsId],
      ["sb-topics", nsId],
      ["sb-subs", nsId, "some-topic"],
      ["sb-scheduled", nsId, entityPath],
    ]);

    invalidateServiceBusQueries(qc, nsId, entityPath);

    expect(isInvalidated(qc, ["sb-peek", nsId, entityPath, 50])).toBe(true);
    expect(isInvalidated(qc, ["sb-dlq", nsId, entityPath, 50])).toBe(true);
    expect(isInvalidated(qc, ["sb-entity-stats", nsId, entityPath])).toBe(true);
    expect(isInvalidated(qc, ["sb-queues", nsId])).toBe(true);
    expect(isInvalidated(qc, ["sb-topics", nsId])).toBe(true);
    expect(isInvalidated(qc, ["sb-subs", nsId, "some-topic"])).toBe(true);
    expect(isInvalidated(qc, ["sb-scheduled", nsId, entityPath])).toBe(true);
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
