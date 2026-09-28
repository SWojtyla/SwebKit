import { describe, expect, it } from "vitest";
import {
    AGENT_FEEDBACK_EXPORT_FILENAME,
    feedbackEntrySummary,
    isFeedbackEligible,
} from "./agent-feedback";
import type { AgentFeedbackEntry } from "@/lib/types";

describe("isFeedbackEligible", () => {
    const base = { role: "assistant" as const, exchangeId: "ex-1" };

    it("allows a completed assistant message with an exchangeId", () => {
        expect(isFeedbackEligible(base)).toBe(true);
    });

    it("rejects messages that can't resolve to a retained exchange", () => {
        // No exchangeId — never completed a streamed turn (or predates the mechanism).
        expect(isFeedbackEligible({ role: "assistant" })).toBe(false);
        expect(isFeedbackEligible({ ...base, exchangeId: "" })).toBe(false);
        // User messages are never rateable.
        expect(isFeedbackEligible({ ...base, role: "user" })).toBe(false);
        // Errors and user-stopped turns have no Done event → no exchange to attach to.
        expect(isFeedbackEligible({ ...base, error: true })).toBe(false);
        expect(isFeedbackEligible({ ...base, stopped: true })).toBe(false);
    });
});

describe("feedbackEntrySummary", () => {
    const entry = (over: Partial<AgentFeedbackEntry>): AgentFeedbackEntry => ({
        id: "f1",
        exchangeId: "ex-abcdef123456",
        sentiment: "down",
        tags: [],
        createdAt: "2026-01-01T00:00:00Z",
        exchangeFound: true,
        steps: [],
        toolsUsed: [],
        ...over,
    });

    it("prefers the user message, truncated at 120 chars", () => {
        expect(feedbackEntrySummary(entry({ userMessage: "why is it slow?" }))).toBe(
            "why is it slow?",
        );
        const long = "x".repeat(200);
        const summary = feedbackEntrySummary(entry({ userMessage: long }));
        expect(summary).toHaveLength(121);
        expect(summary.endsWith("…")).toBe(true);
    });

    it("falls back to the exchange id when the exchange was evicted", () => {
        expect(
            feedbackEntrySummary(entry({ exchangeFound: false, userMessage: null })),
        ).toBe("Exchange ex-abcde (context expired)");
    });
});

describe("AGENT_FEEDBACK_EXPORT_FILENAME", () => {
    it("matches the sidecar's persisted filename", () => {
        expect(AGENT_FEEDBACK_EXPORT_FILENAME).toBe("agent-feedback.json");
    });
});
