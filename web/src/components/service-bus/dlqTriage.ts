import type { SbMessage } from "@/lib/types";

/**
 * One DLQ triage group: all loaded dead-lettered messages sharing the same
 * (deadLetterReason, deadLetterErrorDescription) pair — the two broker headers
 * that say *why* a message landed in the DLQ. The pair is the group key because
 * the same reason with different descriptions usually means different failures.
 */
export interface DlqTriageGroup {
    reason: string;
    description: string | null;
    /** Stable key for React lists and selection state. */
    key: string;
    messages: SbMessage[];
    count: number;
}

export const DLQ_TRIAGE_NO_REASON = "(no reason)";

/**
 * Groups the loaded DLQ window by (reason, description). Missing reasons bucket
 * under "(no reason)" so they're still selectable; missing descriptions stay
 * null so the requeue-by-filter call can pass "any description under this
 * reason". Sorted by group size descending — the biggest failure mode first.
 */
export function groupDlqByReason(messages: SbMessage[]): DlqTriageGroup[] {
    const groups = new Map<string, DlqTriageGroup>();
    for (const message of messages) {
        const reason = message.deadLetterReason ?? DLQ_TRIAGE_NO_REASON;
        const description = message.deadLetterErrorDescription ?? null;
        // JSON tuple as the key — a plain `reason + " " + description` concat
        // would collide ("a b" + "c" vs "a" + "b c").
        const key = JSON.stringify([reason, description]);
        let group = groups.get(key);
        if (!group) {
            group = { reason, description, key, messages: [], count: 0 };
            groups.set(key, group);
        }
        group.messages.push(message);
        group.count++;
    }
    return [...groups.values()].sort((a, b) => b.count - a.count);
}

/** Sequence numbers of a group — drives the ordinary per-selection resubmit/complete calls. */
export function groupSequenceNumbers(group: DlqTriageGroup): number[] {
    return group.messages
        .map((m) => m.sequenceNumber)
        .filter((n): n is number => n !== null);
}
