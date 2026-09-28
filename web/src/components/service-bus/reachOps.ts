import { SB_OP_KIND_REPLAY_TO, type SbOperationStatus } from "@/lib/types";

/**
 * Park-cap contract mirrored from SbOperationService: 1,000 by default, 5,000
 * hard ceiling. The server clamps regardless — these exist so the wizard's
 * input field and warnings match what the op will actually allow before the
 * round-trip, not to enforce anything.
 */
export const REACH_DEFAULT_MAX_PARKED = 1_000;
export const REACH_ABSOLUTE_MAX_PARKED = 5_000;

/** Clamp the wizard's maxParked input to the server-side contract. */
export function clampMaxParked(value: number | null | undefined): number {
    if (value == null || Number.isNaN(value)) return REACH_DEFAULT_MAX_PARKED;
    return Math.min(Math.max(Math.trunc(value), 1), REACH_ABSOLUTE_MAX_PARKED);
}

/**
 * Parse the target-sequence input. Returns the sequence number or null when the
 * field is empty/non-numeric/non-positive — the wizard's Preview button stays
 * disabled on null rather than sending a request the sidecar will 400.
 */
export function parseTargetSequence(text: string): number | null {
    const trimmed = text.trim();
    if (!trimmed) return null;
    const value = Number(trimmed);
    if (!Number.isFinite(value) || !Number.isInteger(value) || value <= 0) {
        return null;
    }
    return value;
}

/** States with parked messages that may still need restoring — the banner's filter. */
export function isResumableState(state: SbOperationStatus["state"]): boolean {
    return (
        state === "Interrupted" || state === "Failed" || state === "Cancelled"
    );
}

export function isTerminalState(state: SbOperationStatus["state"]): boolean {
    return (
        state === "Completed" ||
        state === "Dismissed" ||
        isResumableState(state)
    );
}

/** One-line progress text for the running op — what phase and how far it got. */
export function opProgressText(op: SbOperationStatus): string {
    if (op.kind === SB_OP_KIND_REPLAY_TO) {
        return `Replaying to ${op.targetEntityPath ?? "the target"} — ${op.replayedCount} of ${op.requestedCount} sent so far`;
    }
    if (op.phase === "Parking") {
        return `Parking prefix messages into the DLQ — ${op.parkedCount} parked so far`;
    }
    if (op.phase === "Restoring") {
        return `Restoring parked copies at the tail — ${op.restoredCount} restored so far`;
    }
    return "Starting…";
}

/** Human label for the post-run states the wizard and banner render. */
export function opStateLabel(op: SbOperationStatus): string {
    if (op.kind === SB_OP_KIND_REPLAY_TO) {
        return replayStateLabel(op);
    }
    switch (op.state) {
        case "Running":
            return "Running";
        case "Completed":
            return `Completed — ${op.parkedCount} parked, ${op.restoredCount} restored as tail copies`;
        case "Cancelled":
            return `Cancelled — ${op.parkedInDlq ?? op.parkedCount} parked copy(s) left in the DLQ`;
        case "Failed":
            return "Failed — parked copies may remain in the DLQ";
        case "Interrupted":
            return "Interrupted — the sidecar restarted mid-operation";
        case "Dismissed":
            return "Dismissed — parked copies left in the DLQ by choice";
    }
}

/**
 * Replay labels name the honest counts — how many copies the target accepted, how many
 * requested sequences were never found (consumed/expired) or failed (left for resume).
 */
function replayStateLabel(op: SbOperationStatus): string {
    const tail =
        op.failedCount > 0 || op.missingCount > 0
            ? ` (${op.failedCount} failed, ${op.missingCount} not found)`
            : "";
    switch (op.state) {
        case "Running":
            return "Running";
        case "Completed":
            return `Completed — ${op.replayedCount} of ${op.requestedCount} message(s) replayed as new copies${tail}`;
        case "Cancelled":
            return `Cancelled — ${op.replayedCount} of ${op.requestedCount} replayed; the rest stay in the source`;
        case "Failed":
            return `Failed — ${op.replayedCount} of ${op.requestedCount} replayed; unprocessed messages stay in the source`;
        case "Interrupted":
            return "Interrupted — the sidecar restarted mid-replay";
        case "Dismissed":
            return "Dismissed — remaining source messages left untouched by choice";
    }
}

/**
 * Parse a comma/space-separated sequence-number list ("4501, 4502 4503") into
 * the request's array. Returns null when anything doesn't parse as a positive
 * integer — the wizard's Preview button stays disabled on null rather than
 * sending a request the sidecar will 400.
 */
export function parseSequenceList(text: string): number[] | null {
    const trimmed = text.trim();
    if (!trimmed) return null;
    const seqs: number[] = [];
    for (const part of trimmed.split(/[\s,]+/)) {
        const value = Number(part);
        if (!Number.isFinite(value) || !Number.isInteger(value) || value <= 0) {
            return null;
        }
        seqs.push(value);
    }
    return seqs.length > 0 ? seqs : null;
}
