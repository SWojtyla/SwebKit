import { useConfirmAction, useRejectAction } from "@/lib/hooks/useAgent";
import type { PendingAction } from "@/lib/types";

const riskLabel: Record<PendingAction["risk"], string> = {
  None: "No risk",
  Low: "Low risk",
  High: "High risk",
};

const riskClassName: Record<PendingAction["risk"], string> = {
  None: "bg-muted text-muted-foreground",
  Low: "bg-secondary text-secondary-foreground",
  High: "bg-destructive/15 text-destructive",
};

interface PendingActionCardProps {
  action: PendingAction;
  onApplied?: (result: { isSuccess: boolean; resultSummary: string | null; errorMessage: string | null }) => void;
}

/**
 * The confirm/reject surface for an "Ask & do" proposal — shared by the global /agent page and
 * every contextual assistant panel (ai-augmented-app technical-plan.md Module 3/6), so a
 * mutating action always renders identically regardless of where it was proposed from.
 */
export function PendingActionCard({ action, onApplied }: PendingActionCardProps) {
  const confirm = useConfirmAction();
  const reject = useRejectAction();

  const isBusy = confirm.isPending || reject.isPending;
  const applyResult = confirm.data;

  return (
    <div
      className="space-y-2 rounded-lg border border-primary/30 bg-primary/5 p-3"
      data-testid={`pending-action-${action.id}`}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="text-sm font-medium" data-testid={`pending-action-summary-${action.id}`}>
          {action.summary}
        </p>
        <span
          className={`shrink-0 rounded px-1.5 py-0.5 text-xs ${riskClassName[action.risk]}`}
          data-testid={`pending-action-risk-${action.id}`}
        >
          {riskLabel[action.risk]}
        </span>
      </div>

      <pre
        className="whitespace-pre-wrap rounded-md bg-card p-2 text-xs text-muted-foreground"
        data-testid={`pending-action-preview-${action.id}`}
      >
        {action.preview}
      </pre>

      {applyResult ? (
        <div
          className={`text-xs ${applyResult.isSuccess ? "text-muted-foreground" : "text-destructive"}`}
          data-testid={`pending-action-result-${action.id}`}
        >
          {applyResult.isSuccess
            ? (applyResult.resultSummary ?? "Applied.")
            : (applyResult.errorMessage ?? "Failed to apply.")}
        </div>
      ) : (
        <div className="flex items-center gap-2">
          <button
            onClick={() =>
              confirm.mutate(action.id, {
                onSuccess: (result) => onApplied?.(result),
              })
            }
            disabled={isBusy}
            className="rounded-md bg-primary px-3 py-1 text-xs font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid={`pending-action-confirm-${action.id}`}
          >
            {confirm.isPending ? "Applying…" : "Confirm"}
          </button>
          <button
            onClick={() => reject.mutate(action.id)}
            disabled={isBusy}
            className="rounded-md border px-3 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid={`pending-action-reject-${action.id}`}
          >
            Reject
          </button>
          <span className="text-xs text-muted-foreground">
            Expires {new Date(action.expiresAt).toLocaleTimeString()}
          </span>
        </div>
      )}
    </div>
  );
}
