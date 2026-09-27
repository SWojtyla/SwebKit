import type { SbEntityInfo, SbMessage, SbSessionSummary } from "@/lib/types";

/**
 * Tooltip text shown on every mutating button a session-required entity disables. Today those
 * actions fail with an opaque broker error (502 at the HTTP edge) because session entities only
 * accept session receivers — none of the client's receive paths use them yet.
 */
export const SESSIONS_NOT_SUPPORTED_TOOLTIP =
    "This entity requires sessions — settle actions (complete, dead-letter, resubmit, resend, purge) aren't supported yet because they need session receivers";

/** Whether receive-and-settle mutations are unavailable on this entity. */
export function requiresSessions(
    entity: Pick<SbEntityInfo, "requiresSession"> | null | undefined,
): boolean {
    return entity?.requiresSession === true;
}

/**
 * Client-side mirror of the sidecar's peek-window session grouping, used as the chip bar's data
 * while `useSbSessions` is still loading (or if it failed): group the already-loaded message
 * window by `sessionId`, most recently active session first. Messages without a session id are
 * skipped — a mixed window stays honest rather than inventing a pseudo-session.
 */
export function groupMessagesBySession(messages: SbMessage[]): SbSessionSummary[] {
    const groups = new Map<string, SbSessionSummary>();
    for (const message of messages) {
        if (!message.sessionId) continue;
        let summary = groups.get(message.sessionId);
        if (!summary) {
            summary = {
                sessionId: message.sessionId,
                messageCount: 0,
                firstEnqueuedAt: message.enqueuedAt,
                lastEnqueuedAt: message.enqueuedAt,
            };
            groups.set(message.sessionId, summary);
        }
        summary.messageCount++;
        if (message.enqueuedAt < summary.firstEnqueuedAt) {
            summary.firstEnqueuedAt = message.enqueuedAt;
        }
        if (message.enqueuedAt > summary.lastEnqueuedAt) {
            summary.lastEnqueuedAt = message.enqueuedAt;
        }
    }
    return [...groups.values()].sort((a, b) =>
        b.lastEnqueuedAt.localeCompare(a.lastEnqueuedAt),
    );
}

/**
 * Chip-bar source: prefer the endpoint's peek-window summaries; fall back to grouping the
 * loaded messages when the endpoint returned nothing (or hasn't answered yet), so the chips and
 * the pin filter they feed always agree on which sessions exist in the visible window.
 */
export function sessionChips(
    summaries: SbSessionSummary[] | undefined,
    loadedMessages: SbMessage[],
): SbSessionSummary[] {
    if (summaries && summaries.length > 0) return summaries;
    return groupMessagesBySession(loadedMessages);
}
