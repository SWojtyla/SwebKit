import { useState } from "react";
import { Link, useNavigate } from "react-router";
import {
    Bell,
    Copy,
    ExternalLink,
    Loader2,
    MessageSquare,
    Trash2,
    Wrench,
    Zap,
} from "lucide-react";
import type { EvidenceItem, ProactiveInsightReport } from "../../lib/api";
import { resolveEvidenceView } from "../../lib/evidence-links";
import { useNotification } from "../layout/notification-context";
import { usePendingApprovals } from "@/lib/hooks/useAgent";
import { PendingActionCard } from "../agent/PendingActionCard";
import { proposalsLinkedToReport } from "./proposalLinks";
import { AccessGapsCard } from "./AccessGapsCard";
import { buildWatchPrefill } from "./watchPrefill";
import {
    formatInsightTime,
    insightSeverityBadge,
    insightSeverityLabel,
} from "./aiReportFormat";

interface AiReportDetailProps {
    report: ProactiveInsightReport;
    onDiscuss: (report: ProactiveInsightReport) => void;
    onDelete: (report: ProactiveInsightReport) => void;
    discussPending?: boolean;
    deletePending?: boolean;
}

/**
 * Formatted view of one persisted AI investigation report (ai-insight-reports) —
 * the readable counterpart to the raw JSON the pipeline produces: hypothesis up
 * front, then evidence, ordered next steps, and the concrete proposed fix when the
 * root cause was a misconfiguration.
 */
export function AiReportDetail({
    report,
    onDiscuss,
    onDelete,
    discussPending,
    deletePending,
}: AiReportDetailProps) {
    const { notify } = useNotification();
    const navigate = useNavigate();
    // Two-click inline confirm — window.confirm is unreliable under automation
    // (see swebkit-ui-ux-guardrails escape hatches).
    const [confirmingDelete, setConfirmingDelete] = useState(false);

    // Structured evidence when the report carries it (agent-colleague item 1); the
    // legacy string list stays the fallback for reports persisted by older builds.
    const evidenceItems = report.evidenceItems ?? [];

    const watchThis = (item: EvidenceItem) => {
        const prefillRule = buildWatchPrefill(item.watch);
        if (!prefillRule) return;
        navigate("/monitoring", { state: { prefillRule } });
    };

    // Remediation proposals this investigation parked (monitoring-closed-loop 1c) — the report
    // carries their ids, the live approvals list carries the confirmable card. Actions confirmed,
    // rejected, or expired drop out of the live list and render as a resolved note instead.
    const pendingApprovals = usePendingApprovals();
    const linkedIds = report.pendingActionIds ?? [];
    const liveProposals = proposalsLinkedToReport(report, pendingApprovals.data ?? []);

    const copyFix = () => {
        if (!report.proposedFix?.snippet) return;
        navigator.clipboard
            .writeText(report.proposedFix.snippet)
            .then(() => notify("success", "Copied", "Proposed fix copied to clipboard."))
            .catch(() => notify("error", "Copy failed", "Couldn't write to the clipboard."));
    };

    return (
        <div
            className="space-y-4 rounded-lg border p-4"
            data-testid="ai-report-detail"
        >
            <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                        <span
                            className={`rounded px-2 py-0.5 text-xs ${insightSeverityBadge(report.severity)}`}
                            data-testid="ai-report-severity"
                        >
                            {insightSeverityLabel(report.severity)}
                        </span>
                        <h3
                            className="truncate text-sm font-semibold"
                            data-testid="ai-report-rule-name"
                        >
                            {report.ruleName}
                        </h3>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">
                        Fired {formatInsightTime(report.firedAt)}
                        {report.alertMessage ? ` — ${report.alertMessage}` : ""}
                    </p>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                    <button
                        onClick={() => onDiscuss(report)}
                        disabled={discussPending}
                        className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-xs font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid="ai-report-discuss"
                    >
                        {discussPending ? (
                            <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                            <MessageSquare className="h-3.5 w-3.5" />
                        )}
                        Discuss in chat
                    </button>
                    {confirmingDelete ? (
                        <>
                            <button
                                onClick={() => onDelete(report)}
                                disabled={deletePending}
                                className="rounded-md bg-destructive px-2.5 py-1.5 text-xs font-medium text-destructive-foreground hover:opacity-90 disabled:opacity-50"
                                data-testid="ai-report-delete-confirm"
                            >
                                Delete?
                            </button>
                            <button
                                onClick={() => setConfirmingDelete(false)}
                                className="rounded-md px-2 py-1.5 text-xs hover:bg-accent"
                                data-testid="ai-report-delete-cancel"
                            >
                                Cancel
                            </button>
                        </>
                    ) : (
                        <button
                            onClick={() => setConfirmingDelete(true)}
                            className="rounded-md p-1.5 text-muted-foreground hover:bg-accent hover:text-destructive"
                            title="Delete report"
                            data-testid="ai-report-delete"
                        >
                            <Trash2 className="h-3.5 w-3.5" />
                        </button>
                    )}
                </div>
            </div>

            <section data-testid="ai-report-hypothesis">
                <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    Hypothesis
                </h4>
                <p className="mt-1 text-sm">{report.hypothesis}</p>
            </section>

            {evidenceItems.length > 0 ? (
                <section data-testid="ai-report-evidence">
                    <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        Evidence
                    </h4>
                    <ul className="mt-1 space-y-1.5 pl-1 text-sm">
                        {evidenceItems.map((item, i) => {
                            const viewRoute = resolveEvidenceView(item.view);
                            const hasWatch =
                                buildWatchPrefill(item.watch) !== null;
                            return (
                                <li key={i} data-testid={`evidence-item-${i}`}>
                                    <div className="flex flex-wrap items-start gap-x-2 gap-y-1">
                                        <span className="min-w-0 flex-1">
                                            {item.text}
                                        </span>
                                        {item.tool && (
                                            <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
                                                {item.tool}
                                            </span>
                                        )}
                                        {viewRoute && (
                                            <Link
                                                to={viewRoute}
                                                className="flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
                                                data-testid={`evidence-view-${i}`}
                                            >
                                                <ExternalLink className="h-3 w-3" />
                                                View
                                            </Link>
                                        )}
                                        {hasWatch && (
                                            <button
                                                onClick={() => watchThis(item)}
                                                className="flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
                                                title="Create a monitoring alert rule watching this"
                                                data-testid={`evidence-watch-${i}`}
                                            >
                                                <Bell className="h-3 w-3" />
                                                Watch this
                                            </button>
                                        )}
                                    </div>
                                </li>
                            );
                        })}
                    </ul>
                </section>
            ) : (
                report.evidence.length > 0 && (
                    <section data-testid="ai-report-evidence">
                        <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                            Evidence
                        </h4>
                        <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
                            {report.evidence.map((item, i) => (
                                <li key={i}>{item}</li>
                            ))}
                        </ul>
                    </section>
                )
            )}

            <AccessGapsCard gaps={report.accessGaps ?? []} />

            {linkedIds.length > 0 && (
                <section data-testid="ai-report-proposed-actions">
                    <h4 className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        <Zap className="h-3 w-3" /> Proposed actions
                    </h4>
                    <p className="mt-1 text-xs text-muted-foreground">
                        The investigation proposed these remediations — nothing
                        runs until you confirm one.
                    </p>
                    <div className="mt-2 space-y-2">
                        {liveProposals.map((action) => (
                            <PendingActionCard
                                key={action.id}
                                action={action}
                            />
                        ))}
                        {liveProposals.length < linkedIds.length && (
                            <p
                                className="text-xs text-muted-foreground"
                                data-testid="ai-report-proposals-resolved-note"
                            >
                                {linkedIds.length - liveProposals.length} of{" "}
                                {linkedIds.length} proposal(s) already resolved
                                or expired.
                            </p>
                        )}
                    </div>
                </section>
            )}

            {report.suggestedNextSteps.length > 0 && (
                <section data-testid="ai-report-next-steps">
                    <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        Suggested next steps
                    </h4>
                    <ol className="mt-1 list-decimal space-y-1 pl-5 text-sm">
                        {report.suggestedNextSteps.map((step, i) => (
                            <li key={i}>{step}</li>
                        ))}
                    </ol>
                </section>
            )}

            {report.proposedFix && report.proposedFix.snippet && (
                <section data-testid="ai-report-proposed-fix">
                    <div className="flex items-center justify-between">
                        <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                            Proposed fix
                        </h4>
                        <button
                            onClick={copyFix}
                            className="flex items-center gap-1 rounded-md px-2 py-1 text-xs text-muted-foreground hover:bg-accent"
                            data-testid="ai-report-copy-fix"
                        >
                            <Copy className="h-3 w-3" /> Copy
                        </button>
                    </div>
                    {report.proposedFix.explanation && (
                        <p className="mt-1 text-sm text-muted-foreground">
                            {report.proposedFix.explanation}
                        </p>
                    )}
                    <pre className="mt-2 overflow-x-auto rounded-md bg-muted p-3 text-xs">
                        <code>{report.proposedFix.snippet}</code>
                    </pre>
                </section>
            )}

            {report.toolsUsed.length > 0 && (
                <section data-testid="ai-report-tools-used">
                    <h4 className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                        <Wrench className="h-3 w-3" /> Tools used
                    </h4>
                    <div className="mt-1.5 flex flex-wrap gap-1.5">
                        {report.toolsUsed.map((tool) => (
                            <span
                                key={tool}
                                className="rounded bg-muted px-2 py-0.5 font-mono text-[11px] text-muted-foreground"
                            >
                                {tool}
                            </span>
                        ))}
                    </div>
                    {report.hitMaxRounds && (
                        <p className="mt-1 text-xs text-muted-foreground">
                            The investigation hit its tool-round limit — evidence
                            may be partial.
                        </p>
                    )}
                </section>
            )}
        </div>
    );
}
