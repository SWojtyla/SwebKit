import type { ProactiveInsightReport } from "../../lib/api";
import type { PendingAction } from "../../lib/types";

/**
 * Proposal/report linkage helpers (monitoring-closed-loop 1c). Two selectors join the live
 * pending-approvals list to the investigation that parked each action:
 *
 * - {@link proposalsLinkedToReport}: a persisted report carries the action ids it produced —
 *   resolved (confirmed/rejected/expired) proposals have already dropped out of the live list,
 *   so callers should compare the filtered count to `pendingActionIds.length`.
 * - {@link proposalsForSession}: a transient insight card has no report yet, but the session id
 *   is stamped on every parked action as `originSessionId`.
 */
export function proposalsLinkedToReport(
    report: Pick<ProactiveInsightReport, "pendingActionIds">,
    actions: PendingAction[],
): PendingAction[] {
    const linkedIds = report.pendingActionIds ?? [];
    return actions.filter((a) => linkedIds.includes(a.id));
}

export function proposalsForSession(
    sessionId: string,
    actions: PendingAction[],
): PendingAction[] {
    return actions.filter((a) => a.originSessionId === sessionId);
}
