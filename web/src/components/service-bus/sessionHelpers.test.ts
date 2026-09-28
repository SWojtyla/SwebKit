import { describe, expect, it } from "vitest";
import {
    groupMessagesBySession,
    requiresSessions,
    sessionChips,
} from "./sessionHelpers";
import type { SbEntityInfo, SbMessage, SbSessionSummary } from "@/lib/types";

function makeMessage(overrides: Partial<SbMessage> = {}): SbMessage {
    return {
        messageId: "m-1",
        correlationId: null,
        subject: null,
        contentType: "application/json",
        body: "{}",
        applicationProperties: {},
        systemProperties: null,
        deadLetterReason: null,
        deadLetterErrorDescription: null,
        enqueuedAt: "2026-01-01T00:00:00Z",
        deliveryCount: 1,
        lockToken: null,
        sequenceNumber: 1,
        sessionId: null,
        ...overrides,
    };
}

function makeEntity(overrides: Partial<SbEntityInfo> = {}): SbEntityInfo {
    return {
        name: "orders",
        entityPath: "orders",
        stats: null,
        isDisabled: false,
        isTopic: false,
        isSubscription: false,
        topicName: null,
        subscriptionDeadLetterCount: null,
        requiresSession: false,
        ...overrides,
    };
}

describe("requiresSessions", () => {
    it("is true only for session-required entities", () => {
        expect(requiresSessions(makeEntity({ requiresSession: true }))).toBe(
            true,
        );
        expect(requiresSessions(makeEntity())).toBe(false);
    });

    it("is false for a missing entity — a deep-linked selection whose topology hasn't loaded", () => {
        expect(requiresSessions(null)).toBe(false);
        expect(requiresSessions(undefined)).toBe(false);
    });
});

describe("groupMessagesBySession", () => {
    it("groups the loaded window by session id with counts and enqueue span", () => {
        const messages = [
            makeMessage({
                messageId: "a1",
                sessionId: "sess-a",
                enqueuedAt: "2026-01-01T00:05:00Z",
            }),
            makeMessage({
                messageId: "b1",
                sessionId: "sess-b",
                enqueuedAt: "2026-01-01T00:01:00Z",
            }),
            makeMessage({
                messageId: "a2",
                sessionId: "sess-a",
                enqueuedAt: "2026-01-01T00:10:00Z",
            }),
        ];

        const result = groupMessagesBySession(messages);

        expect(result).toHaveLength(2);
        const a = result.find((s) => s.sessionId === "sess-a")!;
        expect(a.messageCount).toBe(2);
        expect(a.firstEnqueuedAt).toBe("2026-01-01T00:05:00Z");
        expect(a.lastEnqueuedAt).toBe("2026-01-01T00:10:00Z");
    });

    it("skips messages without a session id — no pseudo-session is invented", () => {
        const result = groupMessagesBySession([
            makeMessage({ sessionId: null }),
            makeMessage({ sessionId: "" }),
            makeMessage({ messageId: "s1", sessionId: "sess-x" }),
        ]);

        expect(result).toHaveLength(1);
        expect(result[0].sessionId).toBe("sess-x");
    });

    it("orders sessions by most recent activity first", () => {
        const result = groupMessagesBySession([
            makeMessage({
                messageId: "old",
                sessionId: "sess-old",
                enqueuedAt: "2026-01-01T00:01:00Z",
            }),
            makeMessage({
                messageId: "new",
                sessionId: "sess-new",
                enqueuedAt: "2026-01-01T00:09:00Z",
            }),
        ]);

        expect(result.map((s) => s.sessionId)).toEqual([
            "sess-new",
            "sess-old",
        ]);
    });

    it("returns an empty list for an empty window", () => {
        expect(groupMessagesBySession([])).toEqual([]);
    });
});

describe("sessionChips", () => {
    const endpointSummaries: SbSessionSummary[] = [
        {
            sessionId: "sess-from-endpoint",
            messageCount: 7,
            firstEnqueuedAt: "2026-01-01T00:00:00Z",
            lastEnqueuedAt: "2026-01-01T00:07:00Z",
        },
    ];

    it("prefers the endpoint's peek-window summaries", () => {
        const chips = sessionChips(endpointSummaries, [
            makeMessage({ sessionId: "sess-loaded" }),
        ]);
        expect(chips).toBe(endpointSummaries);
    });

    it("falls back to grouping the loaded window when the endpoint has nothing", () => {
        // An unanswered (undefined) or empty endpoint response both mean "derive from what's
        // loaded" — the chip bar and the pin filter must never disagree about what exists.
        const messages = [makeMessage({ sessionId: "sess-loaded" })];
        expect(
            sessionChips(undefined, messages).map((s) => s.sessionId),
        ).toEqual(["sess-loaded"]);
        expect(sessionChips([], messages).map((s) => s.sessionId)).toEqual([
            "sess-loaded",
        ]);
    });
});
