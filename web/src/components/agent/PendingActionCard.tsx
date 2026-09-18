import { useEffect, useState } from "react";
import { useConfirmAction, useRejectAction } from "@/lib/hooks/useAgent";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
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

/**
 * Maps a `PendingAction.type` (a backend `AgentActionType` enum member name) to the feature area
 * that proposes it, so the card can show "Proposed from: <area> · <target>" (ux-interaction-
 * consistency unit 7.2) without the backend having to carry a new `featureArea`/`sessionId` field —
 * this plan's non-goals limit backend changes to surfacing data that already exists, and the action
 * `type` (feature-specific by construction) plus the existing `target` (already a specific,
 * human-readable instance description — a request name+id, a Redis key, a blob path) together give
 * the same "which conversation proposed this" answer a dedicated attribution field would.
 */
const FEATURE_AREA_BY_ACTION_TYPE: Record<string, string> = {
  CreateRequest: "API Client",
  UpdateRequest: "API Client",
  DeleteRequest: "API Client",
  DuplicateRequest: "API Client",
  MoveRequest: "API Client",
  RenameFolder: "API Client",
  DeleteFolder: "API Client",
  ExecuteHttpRequest: "API Client",
  DeleteRedisKey: "Redis",
  SetRedisKeyTtl: "Redis",
  CopyBlob: "Storage",
  ExecuteSql: "SQL",
  ApplyAksYaml: "AKS",
};

export function describePendingActionOrigin(action: Pick<PendingAction, "type" | "target">): string {
  const area = FEATURE_AREA_BY_ACTION_TYPE[action.type] ?? "Agent";
  return `${area} · ${action.target}`;
}

/** Formats the time remaining until `expiresAt` as a short, live-updating countdown ("expires in
 * 47s") instead of a static clock time that never changes on screen (unit 7.6) — `now` is passed in
 * (rather than read internally) so this stays a pure, easily-testable function. */
export function formatExpiryCountdown(expiresAt: string, now: number): string {
  const remainingMs = new Date(expiresAt).getTime() - now;
  if (remainingMs <= 0) return "expired";

  const totalSeconds = Math.ceil(remainingMs / 1000);
  if (totalSeconds < 60) return `expires in ${totalSeconds}s`;

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `expires in ${minutes}m ${seconds.toString().padStart(2, "0")}s`;
}

/** Ticks once a second so the countdown above actually counts down on screen instead of being
 * computed once and going stale. */
function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}

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
  const [highRiskConfirming, setHighRiskConfirming] = useState(false);
  const now = useNow();

  const isBusy = confirm.isPending || reject.isPending;
  const applyResult = confirm.data;

  // A failed confirm/reject must render as a distinct, sticky error state — not just a toast that
  // disappears — so it never looks identical to "nothing happened yet" (unit 7.1). Disabled until
  // the user explicitly retries or dismisses it, rather than silently re-showing the normal
  // Confirm/Reject buttons as if the previous attempt never happened.
  const failure: { source: "confirm" | "reject"; error: unknown } | null = confirm.isError
    ? { source: "confirm", error: confirm.error }
    : reject.isError
      ? { source: "reject", error: reject.error }
      : null;

  const runConfirm = () => confirm.mutate(action.id, { onSuccess: (result) => onApplied?.(result) });
  const runReject = () => reject.mutate(action.id);

  const handleConfirmClick = () => {
    // High-risk actions get an extra explicit step before anything is sent, proportional to what's
    // being approved (scale/delete/resubmit-class actions) — reusing the same shared `ConfirmBar`
    // every other destructive flow in the app uses, rather than a one-off pattern.
    if (action.risk === "High" && !highRiskConfirming) {
      setHighRiskConfirming(true);
      return;
    }
    setHighRiskConfirming(false);
    runConfirm();
  };

  const handleRetry = () => {
    if (failure?.source === "confirm") {
      confirm.reset();
      runConfirm();
    } else if (failure?.source === "reject") {
      reject.reset();
      runReject();
    }
  };

  const handleDismissFailure = () => {
    confirm.reset();
    reject.reset();
  };

  const confirmButtonClassName =
    action.risk === "High"
      ? "rounded-md bg-destructive px-3 py-1 text-xs font-medium text-destructive-foreground hover:opacity-90 disabled:opacity-50"
      : "rounded-md bg-primary px-3 py-1 text-xs font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50";

  return (
    <div
      className="space-y-2 rounded-lg border border-primary/30 bg-primary/5 p-3"
      data-testid={`pending-action-${action.id}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-sm font-medium" data-testid={`pending-action-summary-${action.id}`}>
            {action.summary}
          </p>
          <p className="truncate text-xs text-muted-foreground" data-testid={`pending-action-origin-${action.id}`}>
            Proposed from: {describePendingActionOrigin(action)}
          </p>
        </div>
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
      ) : failure ? (
        <div className="space-y-2" data-testid={`pending-action-error-${action.id}`}>
          <div className="rounded-md border border-destructive/40 bg-destructive/10 p-2 text-xs text-destructive">
            Couldn&apos;t {failure.source === "confirm" ? "confirm" : "reject"} this action:{" "}
            {failure.error instanceof Error ? failure.error.message : String(failure.error)}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={handleRetry}
              disabled={isBusy}
              title={isBusy ? "An action is in progress" : undefined}
              className="rounded-md border px-3 py-1 text-xs hover:bg-accent disabled:opacity-50"
              data-testid={`pending-action-retry-${action.id}`}
            >
              Retry
            </button>
            <button
              onClick={handleDismissFailure}
              disabled={isBusy}
              title={isBusy ? "An action is in progress" : undefined}
              className="rounded-md border px-3 py-1 text-xs hover:bg-accent disabled:opacity-50"
              data-testid={`pending-action-dismiss-error-${action.id}`}
            >
              Dismiss
            </button>
          </div>
        </div>
      ) : highRiskConfirming ? (
        <ConfirmBar
          message={`Apply this high-risk action? ${action.summary}`}
          confirmLabel={confirm.isPending ? "Applying…" : "Yes, apply"}
          confirmDisabled={confirm.isPending}
          onConfirm={runConfirm}
          onCancel={() => setHighRiskConfirming(false)}
          testId={`pending-action-high-risk-confirm-${action.id}`}
          confirmTestId={`pending-action-high-risk-confirm-yes-${action.id}`}
          cancelTestId={`pending-action-high-risk-confirm-cancel-${action.id}`}
        />
      ) : (
        <div className="flex items-center gap-2">
          <button
            onClick={handleConfirmClick}
            disabled={isBusy}
            title={isBusy ? "An action is in progress" : undefined}
            className={confirmButtonClassName}
            data-testid={`pending-action-confirm-${action.id}`}
          >
            {confirm.isPending ? "Applying…" : "Confirm"}
          </button>
          <button
            onClick={runReject}
            disabled={isBusy}
            title={isBusy ? "An action is in progress" : undefined}
            className="rounded-md border px-3 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid={`pending-action-reject-${action.id}`}
          >
            Reject
          </button>
          <span className="text-xs text-muted-foreground" data-testid={`pending-action-expiry-${action.id}`}>
            {formatExpiryCountdown(action.expiresAt, now)}
          </span>
        </div>
      )}
    </div>
  );
}

interface PendingActionExpiredNoticeProps {
  action: PendingAction;
  onDismiss: () => void;
}

/**
 * Rendered in place of a {@link PendingActionCard} when a poll shows a previously-listed action
 * gone from the list without ever producing an `applyResult` — i.e. it timed out server-side rather
 * than being confirmed or rejected by the user. Unit 7.6: this must be an explicit, sticky notice,
 * not a silent removal indistinguishable from "the user dealt with it already".
 */
export function PendingActionExpiredNotice({ action, onDismiss }: PendingActionExpiredNoticeProps) {
  return (
    <div
      className="flex items-center justify-between gap-2 rounded-lg border border-dashed bg-muted/40 p-3 text-xs text-muted-foreground"
      data-testid={`pending-action-expired-${action.id}`}
    >
      <span>
        This proposal expired before it was confirmed or rejected: <strong>{action.summary}</strong>
      </span>
      <button
        onClick={onDismiss}
        className="shrink-0 rounded-md border px-2 py-1 hover:bg-accent"
        data-testid={`pending-action-expired-dismiss-${action.id}`}
      >
        Dismiss
      </button>
    </div>
  );
}
