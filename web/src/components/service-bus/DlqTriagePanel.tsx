import { useMemo, useState } from "react";
import { AlertTriangle, Info, RotateCcw, X } from "lucide-react";
import {
    useSbCompleteDlq,
    useSbPeekDlq,
    useSbRequeueDlqByFilter,
    useSbResubmitDlq,
} from "@/lib/hooks";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import type { SbEntityInfo } from "@/lib/types";
import { groupDlqByReason, groupSequenceNumbers, type DlqTriageGroup } from "./dlqTriage";

interface Props {
    nsId: string;
    entity: SbEntityInfo;
    onClose: () => void;
}

/** DLQ peek window the triage groups — wider than the default list peek so groups aren't tiny. */
const TRIAGE_PEEK_COUNT = 250;
/** Server-side requeue limit — matches the endpoint's cap. */
const REQUEUE_LIMIT = 500;

type PendingAction =
    | { kind: "resubmit" | "complete"; group: DlqTriageGroup }
    | { kind: "requeueAll"; group: DlqTriageGroup };

/**
 * DLQ triage: group the peeked dead-letter window by (reason, description) — the
 * pair that says *why* messages landed there — and act per group.
 *
 * Two honest scopes, deliberately separate buttons:
 * - "visible": the group's messages inside the 250-peek window, settled by
 *   sequence number through the ordinary resubmit/complete paths;
 * - "all matching": the server's requeue-by-filter, which walks the DLQ past the
 *   window — offered only when the entity's DLQ count says more messages exist.
 */
export function DlqTriagePanel({ nsId, entity, onClose }: Props) {
    const [pending, setPending] = useState<PendingAction | null>(null);
    const { data: messages, isLoading } = useSbPeekDlq(
        nsId,
        entity.entityPath,
        TRIAGE_PEEK_COUNT,
    );
    const resubmit = useSbResubmitDlq();
    const complete = useSbCompleteDlq();
    const requeueAll = useSbRequeueDlqByFilter();

    const groups = useMemo(() => groupDlqByReason(messages ?? []), [messages]);
    const dlqTotal = entity.stats?.deadLetterMessageCount ?? null;
    const busy = resubmit.isPending || complete.isPending || requeueAll.isPending;

    const confirmMessage = (action: PendingAction) => {
        if (action.kind === "requeueAll") {
            return (
                <>
                    Resubmit <strong>every</strong> DLQ message on{" "}
                    <strong>{entity.entityPath}</strong> matching reason{" "}
                    <strong>{action.group.reason}</strong>
                    {action.group.description ? (
                        <>
                            {" "}and description <strong>{action.group.description}</strong>
                        </>
                    ) : null}
                    ? This walks the whole dead-letter queue (up to {REQUEUE_LIMIT}), not just the{" "}
                    {action.group.count} visible here.
                </>
            );
        }
        const verb = action.kind === "resubmit" ? "Resubmit" : "Complete";
        return (
            <>
                {verb} <strong>{action.group.count}</strong> dead-lettered message(s) from group{" "}
                <strong>{action.group.reason}</strong> on{" "}
                <strong>{entity.entityPath}</strong>?{" "}
                {action.kind === "resubmit"
                    ? "Each resubmit sends a new copy and settles the DLQ original."
                    : "Completing removes them permanently."}
            </>
        );
    };

    const runPending = () => {
        if (!pending) return;
        const action = pending;
        setPending(null);
        if (action.kind === "requeueAll") {
            requeueAll.mutate({
                nsId,
                entityPath: entity.entityPath,
                request: {
                    reason: action.group.reason,
                    description: action.group.description,
                    limit: REQUEUE_LIMIT,
                },
            });
            return;
        }
        const seqs = groupSequenceNumbers(action.group);
        if (seqs.length === 0) return;
        if (action.kind === "resubmit") {
            resubmit.mutate({
                nsId,
                entityPath: entity.entityPath,
                sequenceNumbers: seqs.map(String),
            });
        } else {
            complete.mutate({
                nsId,
                entityPath: entity.entityPath,
                sequenceNumbers: seqs.map(String),
            });
        }
    };

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
            data-testid="dlq-triage-overlay"
        >
            <div
                className="flex max-h-[80vh] w-2/3 max-w-3xl flex-col rounded-lg border bg-card shadow-xl"
                data-testid="dlq-triage-panel"
            >
                <div className="flex items-center justify-between border-b px-4 py-3">
                    <div>
                        <h2 className="text-sm font-semibold">DLQ Triage</h2>
                        <p className="text-xs text-muted-foreground">
                            Groups on <strong>{entity.entityPath}</strong> by dead-letter reason and
                            description
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        className="text-muted-foreground hover:text-foreground"
                        data-testid="dlq-triage-close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                <div
                    className="flex items-start gap-2 border-b bg-muted/40 px-4 py-2 text-xs text-muted-foreground"
                    data-testid="dlq-triage-window-note"
                >
                    <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                    <span>
                        Groups cover the {TRIAGE_PEEK_COUNT} peeked dead-letter messages
                        {dlqTotal != null && dlqTotal > TRIAGE_PEEK_COUNT
                            ? ` — the DLQ holds ${dlqTotal} total, so "Resubmit all matching" reaches the rest server-side`
                            : ""}
                        . Resubmit sends new tail copies and settles the originals.
                    </span>
                </div>

                <div className="min-h-0 flex-1 overflow-auto" data-testid="dlq-triage-groups">
                    {isLoading && (
                        <div className="py-8 text-center text-sm text-muted-foreground">
                            Loading dead-lettered messages…
                        </div>
                    )}
                    {!isLoading && groups.length === 0 && (
                        <div
                            className="py-8 text-center text-sm text-muted-foreground"
                            data-testid="dlq-triage-empty"
                        >
                            No dead-lettered messages in the peek window
                        </div>
                    )}
                    {groups.map((group, i) => {
                        const moreBeyondWindow =
                            dlqTotal != null && dlqTotal > group.count;
                        return (
                            <div
                                key={group.key}
                                className="flex items-center gap-3 border-b px-4 py-2.5"
                                data-testid={`dlq-triage-group-${i}`}
                                data-group-key={group.key}
                            >
                                <div className="min-w-0 flex-1">
                                    <div className="flex items-baseline gap-2">
                                        <span className="truncate text-xs font-medium text-destructive">
                                            {group.reason}
                                        </span>
                                        <span
                                            className="text-xs text-muted-foreground"
                                            data-testid={`dlq-triage-count-${i}`}
                                        >
                                            {group.count} in window
                                        </span>
                                    </div>
                                    {group.description && (
                                        <div className="truncate text-xs text-muted-foreground">
                                            {group.description}
                                        </div>
                                    )}
                                </div>
                                <button
                                    onClick={() => setPending({ kind: "resubmit", group })}
                                    disabled={busy}
                                    className="shrink-0 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                                    data-testid={`dlq-triage-resubmit-${i}`}
                                >
                                    Resubmit {group.count}
                                </button>
                                <button
                                    onClick={() => setPending({ kind: "complete", group })}
                                    disabled={busy}
                                    className="shrink-0 rounded-md border px-2 py-1 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
                                    data-testid={`dlq-triage-complete-${i}`}
                                >
                                    Complete {group.count}
                                </button>
                                {moreBeyondWindow && (
                                    <button
                                        onClick={() =>
                                            setPending({ kind: "requeueAll", group })
                                        }
                                        disabled={busy}
                                        title="Resubmit every DLQ message matching this reason/description, beyond the peek window"
                                        className="flex shrink-0 items-center gap-1 rounded-md border border-primary/50 px-2 py-1 text-xs text-primary hover:bg-primary/10 disabled:opacity-50"
                                        data-testid={`dlq-triage-requeue-all-${i}`}
                                    >
                                        <RotateCcw className="h-3 w-3" />
                                        All matching
                                    </button>
                                )}
                            </div>
                        );
                    })}
                </div>

                {pending && (
                    <ConfirmBar
                        message={confirmMessage(pending)}
                        confirmLabel={
                            pending.kind === "complete" ? "Complete" : "Resubmit"
                        }
                        confirmDisabled={busy}
                        onConfirm={runPending}
                        onCancel={() => setPending(null)}
                        testId="dlq-triage-confirm"
                        confirmTestId="dlq-triage-confirm-yes"
                        cancelTestId="dlq-triage-confirm-cancel"
                    />
                )}

                {(resubmit.isError || complete.isError || requeueAll.isError) && (
                    <div
                        className="flex items-center gap-2 border-t bg-destructive/10 px-4 py-2 text-xs text-destructive"
                        data-testid="dlq-triage-error"
                    >
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                        {String(
                            resubmit.error ?? complete.error ?? requeueAll.error,
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
