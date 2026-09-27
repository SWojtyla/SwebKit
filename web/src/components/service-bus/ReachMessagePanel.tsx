import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Check, Info, X } from "lucide-react";
import {
    invalidateServiceBusQueries,
    useSbCancelOperation,
    useSbOperation,
    useSbReachMessagePreview,
    useSbReachMessageStart,
    useSbResumeOperation,
    useSbDismissOperation,
} from "@/lib/hooks";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import type {
    ReachMessagePreview,
    ReachMessageRequest,
    SbEntityInfo,
    SbOperationStatus,
    SbReachTargetAction,
} from "@/lib/types";
import {
    clampMaxParked,
    isResumableState,
    opProgressText,
    opStateLabel,
    parseTargetSequence,
    REACH_ABSOLUTE_MAX_PARKED,
    REACH_DEFAULT_MAX_PARKED,
} from "./reachOps";

interface Props {
    nsId: string;
    entity: SbEntityInfo;
    /** Pre-fills the target field — the currently selected message's sequence, when there is one. */
    defaultTarget?: number | null;
    onClose: () => void;
}

/**
 * Reach-message wizard: preview → confirm → run → poll. The preview step is the
 * honesty contract — the sidecar returns exact consequence/warning text and the
 * wizard renders it verbatim before the ConfirmBar appears. Nothing mutates
 * until that confirm, matching every other destructive flow in the feature.
 *
 * The operation itself runs on the sidecar in the background: this panel polls
 * `GET .../operations/{id}` once a second while it's running and renders the
 * same record the interrupted-op banner would after a crash — cancelled and
 * failed ops keep their parked copies recoverable via Resume / Leave in DLQ.
 */
export function ReachMessagePanel({ nsId, entity, defaultTarget, onClose }: Props) {
    const qc = useQueryClient();
    const [targetInput, setTargetInput] = useState(
        defaultTarget != null ? String(defaultTarget) : "",
    );
    const [action, setAction] = useState<SbReachTargetAction>("Resubmit");
    const [restoreBeforeTarget, setRestoreBeforeTarget] = useState(true);
    const [maxParkedInput, setMaxParkedInput] = useState(
        String(REACH_DEFAULT_MAX_PARKED),
    );
    // The preview is bound to the request it was computed for: changing an
    // input after previewing must not leave stale consequences under the
    // confirm bar, so staleness is derived (request mismatch → treated as no
    // preview) instead of cleared in an effect.
    const [previewFor, setPreviewFor] = useState<{
        request: ReachMessageRequest;
        preview: ReachMessagePreview;
    } | null>(null);
    const [showConfirm, setShowConfirm] = useState(false);
    const [operationId, setOperationId] = useState<string | null>(null);

    const previewMutation = useSbReachMessagePreview();
    const startMutation = useSbReachMessageStart();
    const cancelMutation = useSbCancelOperation();
    const resumeMutation = useSbResumeOperation();
    const dismissMutation = useSbDismissOperation();

    const operation = useSbOperation(nsId, operationId);
    const op: SbOperationStatus | null = operation.data ?? null;

    // Editing the inputs invalidates the previous preview — consequences are
    // computed for a specific target/action/window, so a stale preview behind a
    // changed input would lie about what confirm will do.
    const request = useMemo<ReachMessageRequest | null>(() => {
        const target = parseTargetSequence(targetInput);
        if (target === null) return null;
        return {
            targetSequenceNumber: target,
            action,
            restoreBeforeTarget,
            maxParked: clampMaxParked(Number(maxParkedInput) || null),
        };
    }, [targetInput, action, restoreBeforeTarget, maxParkedInput]);

    const preview =
        previewFor &&
        request &&
        JSON.stringify(previewFor.request) === JSON.stringify(request)
            ? previewFor.preview
            : null;

    // A terminal op changed the queue (or left parked copies in the DLQ) — refresh
    // the message lists once, not per poll tick.
    useEffect(() => {
        if (op && op.state !== "Running") {
            invalidateServiceBusQueries(qc, nsId, entity.entityPath);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps -- fire on the state transition only
    }, [op?.state]);

    const handlePreview = () => {
        if (!request) return;
        const req = request;
        previewMutation.mutate(
            { nsId, entityPath: entity.entityPath, request: req },
            { onSuccess: (p) => setPreviewFor({ request: req, preview: p }) },
        );
    };

    const handleStart = () => {
        if (!request) return;
        setShowConfirm(false);
        startMutation.mutate(
            { nsId, entityPath: entity.entityPath, request },
            { onSuccess: (started) => setOperationId(started.id) },
        );
    };

    const running = op?.state === "Running";
    const resumable = op != null && isResumableState(op.state);

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
            data-testid="reach-message-overlay"
        >
            <div
                className="w-2/3 max-w-2xl rounded-lg border bg-card shadow-xl"
                data-testid="reach-message-panel"
            >
                <div className="flex items-center justify-between border-b px-4 py-3">
                    <div>
                        <h2 className="text-sm font-semibold">Reach message</h2>
                        <p className="text-xs text-muted-foreground">
                            Park the messages ahead of a target into the DLQ, act on it, then
                            restore the parked set as new copies at the tail — on{" "}
                            <strong>{entity.entityPath}</strong>
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        disabled={running}
                        title={running ? "Cancel the operation first" : undefined}
                        className="text-muted-foreground hover:text-foreground disabled:opacity-40"
                        data-testid="reach-message-close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                {!op && (
                    <div className="space-y-3 px-4 py-3" data-testid="reach-message-form">
                        <div className="flex flex-wrap items-end gap-3">
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">
                                    Target sequence number
                                </span>
                                <input
                                    type="number"
                                    min={1}
                                    value={targetInput}
                                    onChange={(e) => setTargetInput(e.target.value)}
                                    className="w-36 rounded-md border bg-background px-2 py-1.5 text-xs"
                                    data-testid="reach-target-input"
                                    autoFocus
                                />
                            </label>
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">Action on target</span>
                                <select
                                    value={action}
                                    onChange={(e) =>
                                        setAction(e.target.value as SbReachTargetAction)
                                    }
                                    className="rounded-md border bg-background px-2 py-1.5 text-xs"
                                    data-testid="reach-action-select"
                                >
                                    <option value="Resubmit">Resubmit (restore as tail copy)</option>
                                    <option value="Complete">Complete (remove permanently)</option>
                                    <option value="DeadLetter">Dead-letter (leave in DLQ)</option>
                                </select>
                            </label>
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">
                                    Park cap (max {REACH_ABSOLUTE_MAX_PARKED})
                                </span>
                                <input
                                    type="number"
                                    min={1}
                                    max={REACH_ABSOLUTE_MAX_PARKED}
                                    value={maxParkedInput}
                                    onChange={(e) => setMaxParkedInput(e.target.value)}
                                    className="w-28 rounded-md border bg-background px-2 py-1.5 text-xs"
                                    data-testid="reach-max-parked"
                                />
                            </label>
                        </div>
                        {action === "Resubmit" && (
                            <label className="flex items-center gap-2 text-xs">
                                <input
                                    type="checkbox"
                                    checked={restoreBeforeTarget}
                                    onChange={(e) => setRestoreBeforeTarget(e.target.checked)}
                                    data-testid="reach-restore-before-target"
                                />
                                Restore the target's copy after the parked prefix (keeps its
                                relative position in the restored set)
                            </label>
                        )}
                        <div
                            className="flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground"
                            data-testid="reach-honesty-note"
                        >
                            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                            <span>
                                Restored messages are <strong>new copies appended at the tail</strong>{" "}
                                with fresh sequence numbers and broker metadata — relative order within
                                the restored set is preserved, queue positions are not. Peek locks that
                                expire mid-run release their messages harmlessly back to the queue.
                            </span>
                        </div>
                        <div className="flex justify-end">
                            <button
                                onClick={handlePreview}
                                disabled={!request || previewMutation.isPending}
                                className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                                data-testid="reach-preview-button"
                            >
                                {previewMutation.isPending ? "Previewing…" : "Preview consequences"}
                            </button>
                        </div>
                    </div>
                )}

                {preview && !op && (
                    <div className="space-y-3 px-4 py-3" data-testid="reach-preview-result">
                        {preview.refusalReason ? (
                            <div
                                className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/10 px-3 py-2 text-xs text-destructive"
                                data-testid="reach-preview-refusal"
                            >
                                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                                {preview.refusalReason}
                            </div>
                        ) : (
                            <>
                                <ul
                                    className="list-disc space-y-1 pl-5 text-xs"
                                    data-testid="reach-preview-consequences"
                                >
                                    {preview.consequences.map((c, i) => (
                                        <li key={i}>{c}</li>
                                    ))}
                                </ul>
                                {preview.warnings.length > 0 && (
                                    <ul
                                        className="list-disc space-y-1 pl-5 text-xs text-amber-600 dark:text-amber-400"
                                        data-testid="reach-preview-warnings"
                                    >
                                        {preview.warnings.map((w, i) => (
                                            <li key={i}>{w}</li>
                                        ))}
                                    </ul>
                                )}
                            </>
                        )}
                        {preview.canStart && (
                            <div className="flex justify-end">
                                <button
                                    onClick={() => setShowConfirm(true)}
                                    className="rounded-md bg-destructive px-4 py-1.5 text-xs text-destructive-foreground hover:opacity-90"
                                    data-testid="reach-confirm-open"
                                >
                                    Continue
                                </button>
                            </div>
                        )}
                    </div>
                )}

                {showConfirm && preview?.canStart && request && (
                    <ConfirmBar
                        message={
                            <>
                                Run reach-message on <strong>{entity.entityPath}</strong> targeting
                                sequence <strong>{request.targetSequenceNumber}</strong>? Parked
                                messages return as <strong>new tail copies</strong>, not at their
                                original positions.
                            </>
                        }
                        confirmLabel="Start operation"
                        confirmDisabled={startMutation.isPending}
                        onConfirm={handleStart}
                        onCancel={() => setShowConfirm(false)}
                        testId="reach-confirm"
                        confirmTestId="reach-confirm-yes"
                        cancelTestId="reach-confirm-cancel"
                    />
                )}

                {op && (
                    <div className="space-y-3 px-4 py-4" data-testid="reach-operation-status">
                        <div className="flex items-center gap-2 text-sm" data-testid="reach-op-state">
                            {op.state === "Completed" ? (
                                <Check className="h-4 w-4 text-success" />
                            ) : op.state === "Running" ? (
                                <span className="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" />
                            ) : (
                                <AlertTriangle className="h-4 w-4 text-destructive" />
                            )}
                            {opStateLabel(op)}
                        </div>
                        {running && (
                            <p className="text-xs text-muted-foreground" data-testid="reach-op-progress">
                                {opProgressText(op)}
                            </p>
                        )}
                        {op.error && (
                            <p className="text-xs text-destructive" data-testid="reach-op-error">
                                {op.error}
                            </p>
                        )}
                        {(op.state === "Completed" || resumable) && (
                            <p
                                className="text-xs text-muted-foreground"
                                data-testid="reach-op-honesty"
                            >
                                Restored messages are new copies at the tail of{" "}
                                <strong>{entity.entityPath}</strong> — positions, sequence numbers
                                and broker metadata are new; payload and relative order are preserved.
                                {resumable &&
                                    ` ${op.parkedInDlq ?? op.parkedCount} parked copy(s) are still stamped in the DLQ.`}
                            </p>
                        )}
                        <div className="flex justify-end gap-2">
                            {running && (
                                <button
                                    onClick={() =>
                                        cancelMutation.mutate({ nsId, operationId: op.id })
                                    }
                                    disabled={cancelMutation.isPending}
                                    className="rounded-md border px-4 py-1.5 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
                                    data-testid="reach-cancel"
                                >
                                    {cancelMutation.isPending ? "Cancelling…" : "Cancel operation"}
                                </button>
                            )}
                            {resumable && (
                                <>
                                    <button
                                        onClick={() =>
                                            resumeMutation.mutate({ nsId, operationId: op.id })
                                        }
                                        disabled={resumeMutation.isPending}
                                        className="rounded-md bg-primary px-4 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                                        data-testid="reach-resume"
                                    >
                                        {resumeMutation.isPending
                                            ? "Resuming…"
                                            : "Resume restore"}
                                    </button>
                                    <button
                                        onClick={() =>
                                            dismissMutation.mutate({ nsId, operationId: op.id })
                                        }
                                        disabled={dismissMutation.isPending}
                                        className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                                        data-testid="reach-dismiss"
                                    >
                                        Leave in DLQ
                                    </button>
                                </>
                            )}
                            {!running && !resumable && (
                                <button
                                    onClick={onClose}
                                    className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent"
                                    data-testid="reach-done-close"
                                >
                                    Close
                                </button>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
