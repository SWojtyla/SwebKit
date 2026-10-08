import {
    ArrowUpRight,
    Ban,
    Check,
    CopyPlus,
    Loader2,
    XCircle,
} from "lucide-react";
import type { SbEntityInfo, SbMessage, SbViewMode } from "@/lib/types";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { resendTargetText, sendableEntityPath } from "./resendHelpers";
import {
    requiresSessions,
    SESSIONS_NOT_SUPPORTED_TOOLTIP,
} from "./sessionHelpers";

/** The pending bulk operation awaiting user confirmation — lifted verbatim from MessageList. */
export type PendingBulkConfirm =
    | { kind: "complete"; seqNumbers: number[] }
    | { kind: "resubmit"; seqNumbers: number[] }
    | { kind: "deadletter"; seqNumbers: number[] }
    | { kind: "cancelScheduled"; seqNumbers: number[] }
    | { kind: "resend"; messages: SbMessage[] };

export interface BulkProgress {
    label: string;
    done: number;
    total: number;
}

export interface BulkActionBarProps {
    selectedCount: number;
    filteredCount: number;
    bulkProgress: BulkProgress | null;
    viewMode: SbViewMode;
    entity: SbEntityInfo | null;
    onToggleSelectAll: () => void;
    onResend: () => void;
    onResubmit: () => void;
    onDeadLetter: () => void;
    onComplete: () => void;
    onCancelScheduled: () => void;
    onClearSelection: () => void;
}

export function BulkActionBar(p: BulkActionBarProps) {
    const busy = p.bulkProgress !== null;
    // Every button in this bar is a receive-and-settle mutation — all of them fail on
    // session-required entities (plain receivers are rejected), so gate them with the same
    // explanation instead of letting the user discover the broker's error.
    const sessionBlocked = requiresSessions(p.entity);
    return (
        <div
            className="flex items-center gap-2 border-b bg-primary/10 px-3 py-1.5"
            data-testid="bulk-action-bar"
        >
            {p.bulkProgress ? (
                <span
                    className="flex items-center gap-1.5 text-xs font-medium"
                    data-testid="bulk-progress"
                >
                    <Loader2 className="h-3 w-3 animate-spin" />
                    {p.bulkProgress.label} {p.bulkProgress.done}/
                    {p.bulkProgress.total}
                </span>
            ) : (
                <span className="text-xs font-medium">
                    {p.selectedCount} selected
                </span>
            )}
            {p.bulkProgress && (
                <div
                    className="h-1.5 w-24 overflow-hidden rounded bg-muted"
                    data-testid="bulk-progress-bar"
                >
                    <div
                        className="h-full bg-primary transition-all"
                        style={{
                            width: `${(p.bulkProgress.done / Math.max(1, p.bulkProgress.total)) * 100}%`,
                        }}
                    />
                </div>
            )}
            <button
                onClick={p.onToggleSelectAll}
                className="text-xs text-muted-foreground hover:text-foreground"
                data-testid="bulk-select-all"
            >
                {p.selectedCount === p.filteredCount
                    ? "Deselect all"
                    : "Select all"}
            </button>
            <div className="ml-auto flex items-center gap-1.5">
                {/* Scheduled view: receive/settle ops can't touch a message that hasn't
                    fired — the only valid bulk op is sender-side cancel-by-sequence. */}
                {p.viewMode === "scheduled" ? (
                    <button
                        onClick={p.onCancelScheduled}
                        disabled={busy}
                        title={
                            busy
                                ? "Cancelling…"
                                : "Cancel each selected scheduled message — it never fires and is removed from the queue"
                        }
                        className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                        data-testid="bulk-cancel-scheduled"
                    >
                        <XCircle className="h-3 w-3" /> Cancel scheduled
                    </button>
                ) : (
                    <>
                        {/* Resend moves each selected message back to the queue it failed
            in (NServiceBus.FailedQ) — works on active messages (e.g. an
            NServiceBus error queue) and on the DLQ. */}
                        <button
                            onClick={p.onResend}
                            disabled={busy || sessionBlocked}
                            title={
                                sessionBlocked
                                    ? SESSIONS_NOT_SUPPORTED_TOOLTIP
                                    : busy
                                      ? "Resending…"
                                      : "Send each selected message back to the queue it originally failed in (NServiceBus.FailedQ — or this entity if unset), then remove it here"
                            }
                            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="bulk-resend"
                        >
                            <CopyPlus className="h-3 w-3" /> Resend to origin
                        </button>
                        {p.viewMode === "dlq" && p.entity && (
                            <button
                                onClick={p.onResubmit}
                                disabled={busy || sessionBlocked}
                                title={
                                    sessionBlocked
                                        ? SESSIONS_NOT_SUPPORTED_TOOLTIP
                                        : busy
                                          ? "Resubmitting…"
                                          : `Send each selected message back to ${sendableEntityPath(p.entity)} — the entity this dead-letter queue belongs to — then remove it from the DLQ`
                                }
                                className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                                data-testid="bulk-resubmit"
                            >
                                <ArrowUpRight className="h-3 w-3" /> Resubmit to{" "}
                                {sendableEntityPath(p.entity)}
                            </button>
                        )}
                        {p.viewMode === "active" && (
                            <button
                                onClick={p.onDeadLetter}
                                disabled={busy || sessionBlocked}
                                title={
                                    sessionBlocked
                                        ? SESSIONS_NOT_SUPPORTED_TOOLTIP
                                        : busy
                                          ? "Dead-lettering…"
                                          : "Move each selected message into this entity's dead-letter queue — a broker move, not a copy (a dead-letter reason is recorded)"
                                }
                                className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                                data-testid="bulk-deadletter"
                            >
                                <Ban className="h-3 w-3" /> Move to DLQ
                            </button>
                        )}
                        <button
                            onClick={p.onComplete}
                            disabled={busy || sessionBlocked}
                            title={
                                sessionBlocked
                                    ? SESSIONS_NOT_SUPPORTED_TOOLTIP
                                    : busy
                                      ? "Completing…"
                                      : `Settle each selected message — permanently removed from ${p.viewMode === "dlq" ? "the dead-letter queue" : "the queue"}, no redelivery`
                            }
                            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="bulk-complete"
                        >
                            <Check className="h-3 w-3" /> Complete
                        </button>
                    </>
                )}
                <button
                    onClick={p.onClearSelection}
                    className="rounded border px-2 py-1 text-xs hover:bg-accent"
                >
                    Cancel
                </button>
            </div>
        </div>
    );
}

export function BulkConfirmBar({
    pending,
    entity,
    onConfirm,
    onCancel,
}: {
    pending: PendingBulkConfirm;
    entity: SbEntityInfo | null;
    onConfirm: () => void;
    onCancel: () => void;
}) {
    return (
        <ConfirmBar
            message={
                pending.kind === "complete"
                    ? `Complete ${pending.seqNumbers.length} message(s)? They are settled and permanently removed — no redelivery.`
                    : pending.kind === "cancelScheduled"
                      ? `Cancel ${pending.seqNumbers.length} scheduled message(s)? They never fire and are removed from the queue — this cannot be undone.`
                      : pending.kind === "resubmit"
                        ? `Resubmit ${pending.seqNumbers.length} message(s) back to ${entity ? sendableEntityPath(entity) : "the source entity"}? Each copy gets a new Message ID and the original leaves the dead-letter queue once sent.`
                        : pending.kind === "deadletter"
                          ? `Move ${pending.seqNumbers.length} message(s) to the dead-letter queue of ${entity?.entityPath ?? "this entity"}?`
                          : `Resend ${pending.messages.length} message(s) to ${resendTargetText(pending.messages, entity?.entityPath ?? "")}? Originals are removed once each copy is sent — every copy gets a new Message ID.`
            }
            confirmLabel={
                pending.kind === "complete"
                    ? "Complete"
                    : pending.kind === "cancelScheduled"
                      ? "Cancel scheduled"
                      : pending.kind === "resubmit"
                        ? "Resubmit"
                        : pending.kind === "deadletter"
                          ? "Dead-letter"
                          : "Resend"
            }
            onConfirm={onConfirm}
            onCancel={onCancel}
            testId="bulk-action-confirm"
        />
    );
}
