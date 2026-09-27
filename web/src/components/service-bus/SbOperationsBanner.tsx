import { AlertTriangle, Loader2 } from "lucide-react";
import {
    useSbCancelOperation,
    useSbDismissOperation,
    useSbEntityOperations,
    useSbResumeOperation,
} from "@/lib/hooks";
import type { SbEntityInfo, SbOperationStatus } from "@/lib/types";
import { isResumableState, opProgressText, opStateLabel } from "./reachOps";

interface Props {
    nsId: string;
    entity: SbEntityInfo;
}

/**
 * Per-entity operation banner: renders a running reach-message op's progress
 * (with cancel), and — more importantly — resumable ops. An interrupted op means
 * the sidecar restarted mid-run and found a still-running journal entry; the
 * parked count comes from a live DLQ stamp scan (`parkedInDlq`), not the journal,
 * so the number is the broker's truth. Resume re-runs restore; "Leave in DLQ"
 * dismisses it terminally — the parked copies keep their stamps as a record.
 */
export function SbOperationsBanner({ nsId, entity }: Props) {
    const operations = useSbEntityOperations(nsId, entity.entityPath);
    const cancel = useSbCancelOperation();
    const resume = useSbResumeOperation();
    const dismiss = useSbDismissOperation();

    const ops = operations.data ?? [];
    const visible = ops.filter(
        (op) => op.state === "Running" || isResumableState(op.state),
    );
    if (visible.length === 0) return null;

    return (
        <div className="border-b" data-testid="sb-operations-banner">
            {visible.map((op) => (
                <OperationRow
                    key={op.id}
                    op={op}
                    busy={cancel.isPending || resume.isPending || dismiss.isPending}
                    onCancel={() => cancel.mutate({ nsId, operationId: op.id })}
                    onResume={() => resume.mutate({ nsId, operationId: op.id })}
                    onDismiss={() => dismiss.mutate({ nsId, operationId: op.id })}
                />
            ))}
        </div>
    );
}

function OperationRow({
    op,
    busy,
    onCancel,
    onResume,
    onDismiss,
}: {
    op: SbOperationStatus;
    busy: boolean;
    onCancel: () => void;
    onResume: () => void;
    onDismiss: () => void;
}) {
    const resumable = isResumableState(op.state);
    const parked = op.parkedInDlq ?? op.parkedCount;

    return (
        <div
            className="flex items-center gap-3 px-4 py-2 text-xs"
            data-testid={`sb-operation-${op.id}`}
        >
            {op.state === "Running" ? (
                <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin text-primary" />
            ) : (
                <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-amber-600 dark:text-amber-400" />
            )}
            <div className="min-w-0 flex-1">
                <span className="font-medium">{opStateLabel(op)}</span>
                <span className="text-muted-foreground">
                    {" "}
                    — reach-message on seq {op.targetSequenceNumber}
                </span>
                {op.state === "Running" && (
                    <span className="block text-muted-foreground">
                        {opProgressText(op)}
                    </span>
                )}
                {resumable && (
                    <span
                        className="block text-muted-foreground"
                        data-testid={`sb-operation-parked-${op.id}`}
                    >
                        {parked} parked copy(s) stamped in the DLQ — restore them as tail copies or
                        leave them parked.
                    </span>
                )}
            </div>
            {op.state === "Running" && (
                <button
                    onClick={onCancel}
                    disabled={busy}
                    className="shrink-0 rounded-md border px-2 py-1 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
                    data-testid={`sb-operation-cancel-${op.id}`}
                >
                    Cancel
                </button>
            )}
            {resumable && (
                <>
                    <button
                        onClick={onResume}
                        disabled={busy}
                        className="shrink-0 rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid={`sb-operation-resume-${op.id}`}
                    >
                        Resume restore
                    </button>
                    <button
                        onClick={onDismiss}
                        disabled={busy}
                        className="shrink-0 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                        data-testid={`sb-operation-dismiss-${op.id}`}
                    >
                        Leave in DLQ
                    </button>
                </>
            )}
        </div>
    );
}
