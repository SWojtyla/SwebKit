import type { AgentFeedbackEntry, ChatMessage } from "@/lib/types";

/**
 * Pure helpers for the thumbs-down → regression-case flow (agent-colleague item 5), kept free
 * of React so they're vitest-able without a DOM.
 */

/**
 * True when an assistant message may show the thumbs-down button. Requires an `exchangeId` —
 * the correlation handle minted on the terminal "done" stream event — so messages that never
 * completed a streamed turn (errors, stopped streams, seeded transcripts, user messages) are
 * ineligible: without the id there is no server-side exchange to attach the rating to.
 */
export function isFeedbackEligible(
    message: Pick<ChatMessage, "role" | "error" | "stopped" | "exchangeId">,
): boolean {
    return (
        message.role === "assistant" &&
        !message.error &&
        !message.stopped &&
        typeof message.exchangeId === "string" &&
        message.exchangeId.length > 0
    );
}

/** One-line summary of a persisted feedback entry for the Settings list — the user's question
 * is the most recognizable anchor; falls back to the exchange id when the ring buffer had
 * already evicted the exchange (bare thumbs-down, still recorded). */
export function feedbackEntrySummary(entry: AgentFeedbackEntry): string {
    const text = entry.userMessage?.trim();
    if (text) return text.length > 120 ? `${text.slice(0, 120)}…` : text;
    return `Exchange ${entry.exchangeId.slice(0, 8)} (context expired)`;
}

/** The filename the Settings "Export JSON" button downloads. */
export const AGENT_FEEDBACK_EXPORT_FILENAME = "agent-feedback.json";
