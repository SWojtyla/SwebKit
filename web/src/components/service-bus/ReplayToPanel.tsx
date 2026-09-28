import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Check, Info, X } from "lucide-react";
import {
    invalidateServiceBusQueries,
    useSbCancelOperation,
    useSbDismissOperation,
    useSbOperation,
    useSbQueues,
    useSbResumeOperation,
    useSbReplayToPreview,
    useSbReplayToStart,
    useSbTopics,
} from "@/lib/hooks";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { SearchableSelect } from "@/components/shared/SearchableSelect";
import type {
    ReplayToPreview,
    ReplayToRequest,
    SbEntityInfo,
    SbOperationStatus,
    ServiceBusNamespace,
} from "@/lib/types";
import {
    isResumableState,
    opProgressText,
    opStateLabel,
    parseSequenceList,
} from "./reachOps";

interface Props {
    nsId: string;
    entity: SbEntityInfo;
    /** Every configured namespace — the target picker is the whole point of this panel. */
    namespaces: ServiceBusNamespace[];
    /** Which source view the panel was opened from — preselects the dead-letter source. */
    defaultDeadLetter?: boolean;
    /** Selected message sequence numbers to prefill, when the message list had a selection. */
    defaultSequences?: number[];
    onClose: () => void;
}

/**
 * Cross-environment replay wizard: preview → confirm → run → poll — the same
 * shape as reach-message, except the target can be a different namespace/entity
 * entirely. The honesty contract is the whole UX: every replayed message is a
 * NEW message on the target — tail-appended, fresh sequence number, delivery
 * count reset — stamped SwebKit.ReplayedFrom so provenance survives. Positions
 * are never "restored": the source either keeps its copies (copy semantics) or
 * loses them once the target accepts the clone (remove-source / move).
 *
 * No transaction can span two namespaces, so a crash between send and source
 * settle is at-least-once — the journaled processed-set is what resume skips,
 * and the preview says so verbatim.
 */
export function ReplayToPanel({
    nsId,
    entity,
    namespaces,
    defaultDeadLetter = false,
    defaultSequences,
    onClose,
}: Props) {
    const qc = useQueryClient();
    const [seqsInput, setSeqsInput] = useState(
        (defaultSequences ?? []).join(", "),
    );
    const [deadLetter, setDeadLetter] = useState(defaultDeadLetter);
    const [targetNsId, setTargetNsId] = useState<string | null>(
        namespaces.find((n) => n.id !== nsId)?.id ?? null,
    );
    const [targetPath, setTargetPath] = useState("");
    const [scrub, setScrub] = useState(false);
    const [stripSession, setStripSession] = useState(false);
    const [removeSource, setRemoveSource] = useState(false);
    // The preview is bound to the request it was computed for — a changed input
    // must not leave stale consequences under the confirm bar.
    const [previewFor, setPreviewFor] = useState<{
        request: ReplayToRequest;
        preview: ReplayToPreview;
    } | null>(null);
    const [showConfirm, setShowConfirm] = useState(false);
    const [operationId, setOperationId] = useState<string | null>(null);

    const previewMutation = useSbReplayToPreview();
    const startMutation = useSbReplayToStart();
    const cancelMutation = useSbCancelOperation();
    const resumeMutation = useSbResumeOperation();
    const dismissMutation = useSbDismissOperation();

    const operation = useSbOperation(nsId, operationId);
    const op: SbOperationStatus | null = operation.data ?? null;

    // Target entity suggestions — queues and topics on the TARGET namespace are
    // the sendable paths (subscriptions are receive-only). Free text is still
    // allowed for paths topology doesn't list.
    const targetQueues = useSbQueues(targetNsId);
    const targetTopics = useSbTopics(targetNsId);
    const targetEntity = useMemo(
        () =>
            [...(targetQueues.data ?? []), ...(targetTopics.data ?? [])].find(
                (e) => e.entityPath === targetPath.trim(),
            ) ?? null,
        [targetQueues.data, targetTopics.data, targetPath],
    );

    const request = useMemo<ReplayToRequest | null>(() => {
        const seqs = parseSequenceList(seqsInput);
        if (!seqs || !targetNsId || !targetPath.trim()) return null;
        return {
            sequenceNumbers: seqs,
            deadLetter,
            targetNsId,
            targetEntityPath: targetPath.trim(),
            scrubProperties: scrub,
            stripSessionId: stripSession,
            removeSource,
        };
    }, [seqsInput, deadLetter, targetNsId, targetPath, scrub, stripSession, removeSource]);

    const preview =
        previewFor &&
        request &&
        JSON.stringify(previewFor.request) === JSON.stringify(request)
            ? previewFor.preview
            : null;

    // A terminal op changed the source (and possibly the target) — refresh both.
    useEffect(() => {
        if (op && op.state !== "Running") {
            invalidateServiceBusQueries(qc, nsId, entity.entityPath);
            if (op.targetNamespaceId && op.targetEntityPath) {
                invalidateServiceBusQueries(qc, op.targetNamespaceId, op.targetEntityPath);
            }
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
    const targetNs = namespaces.find((n) => n.id === targetNsId);

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
            data-testid="replay-to-overlay"
        >
            <div
                className="w-2/3 max-w-2xl rounded-lg border bg-card shadow-xl"
                data-testid="replay-to-panel"
            >
                <div className="flex items-center justify-between border-b px-4 py-3">
                    <div>
                        <h2 className="text-sm font-semibold">Replay to…</h2>
                        <p className="text-xs text-muted-foreground">
                            Send selected messages from <strong>{entity.entityPath}</strong> to
                            another namespace or entity as new tail-appended copies
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        disabled={running}
                        title={running ? "Cancel the operation first" : undefined}
                        className="text-muted-foreground hover:text-foreground disabled:opacity-40"
                        data-testid="replay-to-close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                {!op && (
                    <div className="space-y-3 px-4 py-3" data-testid="replay-to-form">
                        <div className="flex flex-wrap items-end gap-3">
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">
                                    Source sequences (comma-separated)
                                </span>
                                <input
                                    type="text"
                                    value={seqsInput}
                                    onChange={(e) => setSeqsInput(e.target.value)}
                                    placeholder="4501, 4502, …"
                                    className="w-48 rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
                                    data-testid="replay-seqs-input"
                                    autoFocus
                                />
                            </label>
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">Source view</span>
                                <select
                                    value={deadLetter ? "dlq" : "active"}
                                    onChange={(e) => setDeadLetter(e.target.value === "dlq")}
                                    className="rounded-md border bg-background px-2 py-1.5 text-xs"
                                    data-testid="replay-source-view"
                                >
                                    <option value="active">Active messages</option>
                                    <option value="dlq">Dead-letter queue</option>
                                </select>
                            </label>
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">Target namespace</span>
                                <SearchableSelect
                                    items={namespaces.map((n) => ({
                                        value: n.id,
                                        label: n.alias || n.fullyQualifiedNamespace,
                                        subtitle: n.alias ? n.fullyQualifiedNamespace : undefined,
                                    }))}
                                    value={targetNsId}
                                    onChange={(item) => setTargetNsId(item.value || null)}
                                    placeholder="Select namespace..."
                                    filterPlaceholder="Filter namespaces..."
                                    testId="replay-target-ns"
                                    listAriaLabel="Target namespaces"
                                    buttonClassName="min-w-[11rem]"
                                />
                            </label>
                            <label className="flex flex-col gap-1 text-xs">
                                <span className="text-muted-foreground">Target entity</span>
                                <input
                                    type="text"
                                    value={targetPath}
                                    onChange={(e) => setTargetPath(e.target.value)}
                                    placeholder="queue or topic path"
                                    list="replay-target-entities"
                                    className="w-48 rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
                                    data-testid="replay-target-entity-input"
                                />
                                <datalist id="replay-target-entities">
                                    {(targetQueues.data ?? []).map((e) => (
                                        <option
                                            key={e.entityPath}
                                            value={e.entityPath}
                                            label={`queue${e.requiresSession ? " (sessions)" : ""}`}
                                        />
                                    ))}
                                    {(targetTopics.data ?? []).map((e) => (
                                        <option
                                            key={e.entityPath}
                                            value={e.entityPath}
                                            label="topic"
                                        />
                                    ))}
                                </datalist>
                            </label>
                        </div>

                        {targetEntity?.requiresSession && (
                            <p
                                className="rounded-md border border-amber-500/50 bg-amber-500/10 px-3 py-1.5 text-xs text-amber-600 dark:text-amber-400"
                                data-testid="replay-target-session-hint"
                            >
                                {targetEntity.entityPath} requires sessions — the broker rejects
                                sessionless sends there. Session ids on the copies will be kept;
                                stripping them is refused.
                            </p>
                        )}

                        <div className="flex flex-wrap gap-x-6 gap-y-2">
                            <label className="flex items-center gap-2 text-xs">
                                <input
                                    type="checkbox"
                                    checked={scrub}
                                    onChange={(e) => setScrub(e.target.checked)}
                                    data-testid="replay-scrub-properties"
                                />
                                Scrub application properties
                                <span className="text-muted-foreground">
                                    (drop framework/routing metadata)
                                </span>
                            </label>
                            <label className="flex items-center gap-2 text-xs">
                                <input
                                    type="checkbox"
                                    checked={stripSession}
                                    onChange={(e) => setStripSession(e.target.checked)}
                                    disabled={targetEntity?.requiresSession === true}
                                    data-testid="replay-strip-session"
                                />
                                Strip session ids
                                <span className="text-muted-foreground">
                                    (needed when the target isn&apos;t session-enabled)
                                </span>
                            </label>
                            <label className="flex items-center gap-2 text-xs">
                                <input
                                    type="checkbox"
                                    checked={removeSource}
                                    onChange={(e) => setRemoveSource(e.target.checked)}
                                    data-testid="replay-remove-source"
                                />
                                Remove source after send
                                <span className="text-muted-foreground">
                                    (move semantics — source copies are settled)
                                </span>
                            </label>
                        </div>

                        <div
                            className="flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground"
                            data-testid="replay-honesty-note"
                        >
                            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                            <span>
                                Every replayed message is a <strong>new message</strong> on the
                                target — appended at the tail with a fresh sequence number and a
                                reset delivery count, never returned to its original position. Each
                                copy is stamped{" "}
                                <code className="font-mono">SwebKit.ReplayedFrom</code> with the
                                source namespace and entity. Send and source-settle can&apos;t be
                                transactional across namespaces — a crash between them can duplicate
                                a copy on the target.
                            </span>
                        </div>
                        <div className="flex justify-end">
                            <button
                                onClick={handlePreview}
                                disabled={!request || previewMutation.isPending}
                                className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                                data-testid="replay-preview-button"
                            >
                                {previewMutation.isPending ? "Previewing…" : "Preview consequences"}
                            </button>
                        </div>
                    </div>
                )}

                {preview && !op && (
                    <div className="space-y-3 px-4 py-3" data-testid="replay-preview-result">
                        {preview.refusalReason ? (
                            <div
                                className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/10 px-3 py-2 text-xs text-destructive"
                                data-testid="replay-preview-refusal"
                            >
                                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                                {preview.refusalReason}
                            </div>
                        ) : (
                            <>
                                <ul
                                    className="list-disc space-y-1 pl-5 text-xs"
                                    data-testid="replay-preview-consequences"
                                >
                                    {preview.consequences.map((c, i) => (
                                        <li key={i}>{c}</li>
                                    ))}
                                </ul>
                                {preview.warnings.length > 0 && (
                                    <ul
                                        className="list-disc space-y-1 pl-5 text-xs text-amber-600 dark:text-amber-400"
                                        data-testid="replay-preview-warnings"
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
                                    data-testid="replay-confirm-open"
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
                                Replay {request.sequenceNumbers.length} message(s) from{" "}
                                <strong>
                                    {entity.entityPath}
                                    {request.deadLetter ? " (DLQ)" : ""}
                                </strong>{" "}
                                to{" "}
                                <strong>
                                    {targetNs?.alias ?? request.targetNsId}/{request.targetEntityPath}
                                </strong>
                                ? Each lands as a <strong>new tail-appended copy</strong>
                                {request.removeSource
                                    ? ", and the source copy is removed once the target accepts it."
                                    : " — the source keeps its copies."}
                            </>
                        }
                        confirmLabel="Start replay"
                        confirmDisabled={startMutation.isPending}
                        onConfirm={handleStart}
                        onCancel={() => setShowConfirm(false)}
                        testId="replay-confirm"
                        confirmTestId="replay-confirm-yes"
                        cancelTestId="replay-confirm-cancel"
                    />
                )}

                {op && (
                    <div className="space-y-3 px-4 py-4" data-testid="replay-operation-status">
                        <div className="flex items-center gap-2 text-sm" data-testid="replay-op-state">
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
                            <p className="text-xs text-muted-foreground" data-testid="replay-op-progress">
                                {opProgressText(op)}
                            </p>
                        )}
                        {op.error && (
                            <p className="text-xs text-destructive" data-testid="replay-op-error">
                                {op.error}
                            </p>
                        )}
                        {(op.state === "Completed" || resumable) && (
                            <p
                                className="text-xs text-muted-foreground"
                                data-testid="replay-op-honesty"
                            >
                                Copies on the target are new messages — fresh sequence numbers,
                                reset delivery counts, stamped{" "}
                                <code className="font-mono">SwebKit.ReplayedFrom</code>. Nothing was
                                parked in the source&apos;s dead-letter queue.
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
                                    data-testid="replay-cancel"
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
                                        data-testid="replay-resume"
                                    >
                                        {resumeMutation.isPending ? "Resuming…" : "Resume replay"}
                                    </button>
                                    <button
                                        onClick={() =>
                                            dismissMutation.mutate({ nsId, operationId: op.id })
                                        }
                                        disabled={dismissMutation.isPending}
                                        className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                                        data-testid="replay-dismiss"
                                    >
                                        Dismiss
                                    </button>
                                </>
                            )}
                            {!running && !resumable && (
                                <button
                                    onClick={onClose}
                                    className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent"
                                    data-testid="replay-done-close"
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
