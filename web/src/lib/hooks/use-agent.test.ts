import { describe, expect, it } from "vitest";
import { describeAgentToolEvent, isAbortError, reconcilePendingActionsFeed } from "./useAgent";
import type { PendingAction } from "@/lib/types";

// Fixed, wall-clock-independent default so two `action()` calls in the same test always produce
// identical objects for `toEqual` — using `Date.now()` here would make that comparison flaky.
const action = (overrides: Partial<PendingAction> = {}): PendingAction => ({
  id: "a1",
  type: "DeleteRequest",
  summary: "Delete request 'Get token'",
  target: "Request 'Get token' (r1)",
  risk: "High",
  preview: "Name: Get token",
  expiresAt: "2026-06-01T00:00:00.000Z",
  ...overrides,
});

describe("reconcilePendingActionsFeed", () => {
  const now = Date.parse("2026-01-01T00:00:00.000Z");

  it("returns the previous feed unchanged while a poll is still in flight (latest undefined)", () => {
    const previous = [{ action: action(), expired: false }];
    expect(reconcilePendingActionsFeed(previous, undefined, now)).toBe(previous);
  });

  it("marks a still-live action from the latest poll as not expired", () => {
    const feed = reconcilePendingActionsFeed([], [action({ id: "a1" })], now);
    expect(feed).toEqual([{ action: action({ id: "a1" }), expired: false }]);
  });

  it("flags an action as expired only once it's missing from the poll AND past its own expiresAt", () => {
    const expiredAction = action({ id: "a1", expiresAt: new Date(now - 1_000).toISOString() });
    const feed = reconcilePendingActionsFeed([{ action: expiredAction, expired: false }], [], now);
    expect(feed).toEqual([{ action: expiredAction, expired: true }]);
  });

  it("drops an action missing from the poll before its own expiry with no notice (confirmed/rejected by the user)", () => {
    const resolvedAction = action({ id: "a1", expiresAt: new Date(now + 60_000).toISOString() });
    const feed = reconcilePendingActionsFeed([{ action: resolvedAction, expired: false }], [], now);
    expect(feed).toEqual([]);
  });

  it("keeps an already-expired entry in the feed until the caller explicitly dismisses it", () => {
    const expiredAction = action({ id: "a1", expiresAt: new Date(now - 5_000).toISOString() });
    const feed = reconcilePendingActionsFeed([{ action: expiredAction, expired: true }], [], now);
    expect(feed).toEqual([{ action: expiredAction, expired: true }]);
  });
});

describe("describeAgentToolEvent", () => {
  it("turns a known verb prefix into a present-tense phrase", () => {
    expect(describeAgentToolEvent({ toolName: "get_pod_logs" })).toBe("fetching pod logs");
    expect(describeAgentToolEvent({ toolName: "list_redis_keys" })).toBe("listing redis keys");
  });

  it("falls back to 'running' for an unrecognized verb prefix", () => {
    expect(describeAgentToolEvent({ toolName: "purge_dead_letter_queue" })).toBe("running dead letter queue");
  });

  it("returns an empty string for a missing/blank tool name", () => {
    expect(describeAgentToolEvent({ toolName: undefined })).toBe("");
    expect(describeAgentToolEvent({ toolName: "  " })).toBe("");
  });
});

describe("isAbortError", () => {
  it("recognizes a real AbortError", () => {
    expect(isAbortError(new DOMException("The operation was aborted.", "AbortError"))).toBe(true);
  });

  it("recognizes a plain object shaped like an AbortError (for tests that don't construct a real one)", () => {
    expect(isAbortError({ name: "AbortError" })).toBe(true);
  });

  it("rejects a genuine network/stream failure", () => {
    expect(isAbortError(new Error("network error"))).toBe(false);
    expect(isAbortError("not an error at all")).toBe(false);
    expect(isAbortError(null)).toBe(false);
  });
});
