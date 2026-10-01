import {
    CheckCircle2,
    Loader2,
    Play,
    ShieldAlert,
    Sparkles,
    Undo2,
    X,
} from "lucide-react";
import type {
    InsightReportStatus,
    ProactiveInsightReport,
} from "../../lib/api";
import { SkeletonRows } from "@/components/shared/Skeleton";
import { AiReportDetail } from "./AiReportDetail";
import {
    formatInsightTime,
    insightSeverityBadge,
    insightSeverityLabel,
} from "./aiReportFormat";

interface AiReportsPanelProps {
    reports: ProactiveInsightReport[];
    isLoading: boolean;
    isError: boolean;
    error: unknown;
    selectedId: string | null;
    onSelect: (id: string | null) => void;
    onDiscuss: (report: ProactiveInsightReport) => void;
    onDelete: (report: ProactiveInsightReport) => void;
    /** Manual trigger (ai-reports-kanban): starts the model investigation for a queued card. */
    onRun: (report: ProactiveInsightReport) => void;
    /** Kanban move: Ready↔Done, Queued→Done discards. */
    onSetStatus: (
        report: ProactiveInsightReport,
        status: InsightReportStatus,
    ) => void;
    /** `ruleId|firedAt` keys whose investigation is currently in flight — those queued
     * cards render a spinner instead of the Investigate button. */
    runningKeys: ReadonlySet<string>;
    discussPending?: boolean;
    deletePending?: boolean;
    statusPending?: boolean;
    runPending?: boolean;
}

const COLUMNS: {
    status: InsightReportStatus;
    label: string;
    hint: string;
    testId: string;
}[] = [
    {
        status: "Queued",
        label: "Queued",
        hint: "Context prepared — click Investigate to spend tokens",
        testId: "ai-reports-col-queued",
    },
    {
        status: "Ready",
        label: "Ready",
        hint: "Completed investigations",
        testId: "ai-reports-col-ready",
    },
    {
        status: "Done",
        label: "Done",
        hint: "Reviewed reports",
        testId: "ai-reports-col-done",
    },
];

function cardKey(report: ProactiveInsightReport) {
    return `${report.ruleId}|${report.firedAt}`;
}

function alertSeverityBadge(severity: string | null | undefined): string {
    return severity === "Critical"
        ? "bg-destructive/15 text-destructive"
        : "bg-warning/15 text-warning";
}

/**
 * The Monitoring "AI Reports" tab (ai-insight-reports + ai-reports-kanban): a kanban
 * board over the persisted investigations. Queued holds prepared-but-never-run
 * context bundles (manual-mode rules), Ready holds finished reports, Done is the
 * reviewed archive. Selecting a card renders it on the right — the full detail for
 * Ready/Done, the prepared context summary for Queued.
 */
export function AiReportsPanel({
    reports,
    isLoading,
    isError,
    error,
    selectedId,
    onSelect,
    onDiscuss,
    onDelete,
    onRun,
    onSetStatus,
    runningKeys,
    discussPending,
    deletePending,
    statusPending,
    runPending,
}: AiReportsPanelProps) {
    if (isError) {
        return (
            <div
                className="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-3 text-sm text-destructive"
                data-testid="ai-reports-error"
            >
                {error instanceof Error ? error.message : String(error)}
            </div>
        );
    }

    if (isLoading) {
        return <SkeletonRows count={4} />;
    }

    if (reports.length === 0) {
        return (
            <div
                className="flex flex-col items-center gap-2 rounded-lg border px-3 py-10 text-center"
                data-testid="ai-reports-empty"
            >
                <Sparkles className="h-5 w-5 text-muted-foreground" />
                <p className="text-sm text-muted-foreground">
                    No AI reports yet. When a rule with AI investigation enabled
                    fires, the report lands here — queued first if the rule is
                    in manual mode, even after a restart.
                </p>
            </div>
        );
    }

    const selected = reports.find((r) => r.id === selectedId) ?? null;

    const renderCard = (report: ProactiveInsightReport) => {
        const status = report.status ?? "Ready";
        const isSelected = report.id === selected?.id;
        const running = runningKeys.has(cardKey(report));
        const isDone = status === "Done";
        return (
            <div
                key={report.id}
                role="listitem"
                onClick={() => onSelect(isSelected ? null : report.id)}
                className={`w-full rounded-lg border px-3 py-2.5 text-left transition-colors cursor-pointer ${
                    isSelected
                        ? "border-primary/50 bg-primary/5"
                        : "hover:bg-accent/50"
                } ${isDone ? "opacity-70" : ""}`}
                data-testid={`ai-report-row-${report.id}`}
            >
                <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-sm font-medium">
                        {report.ruleName}
                    </span>
                    {status === "Queued" ? (
                        <span
                            className={`shrink-0 rounded px-1.5 py-0.5 text-[11px] ${alertSeverityBadge(report.alertSeverity)}`}
                            data-testid={`ai-report-alert-severity-${report.id}`}
                        >
                            {report.alertSeverity ?? "Warning"}
                        </span>
                    ) : (
                        <span
                            className={`shrink-0 rounded px-1.5 py-0.5 text-[11px] ${insightSeverityBadge(report.severity)}`}
                        >
                            {insightSeverityLabel(report.severity)}
                        </span>
                    )}
                </div>
                <p className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
                    {status === "Queued"
                        ? report.alertMessage
                        : report.hypothesis}
                </p>
                {status === "Queued" && report.preparedContextSummary && (
                    <p
                        className="mt-1 line-clamp-1 text-[11px] text-muted-foreground/80"
                        title={report.preparedContextSummary}
                    >
                        {report.preparedContextSummary}
                    </p>
                )}
                <p className="mt-1 flex items-center gap-2 text-[11px] text-muted-foreground/70">
                    {formatInsightTime(report.firedAt)}
                    {(report.accessGaps?.length ?? 0) > 0 && (
                        <span
                            className="flex items-center gap-1 text-warning"
                            title="Investigation hit permission denials — evidence may be partial"
                            data-testid={`ai-report-access-gaps-${report.id}`}
                        >
                            <ShieldAlert className="h-3 w-3" />
                            {report.accessGaps!.length} access gap
                            {report.accessGaps!.length === 1 ? "" : "s"}
                        </span>
                    )}
                </p>
                <div className="mt-2 flex items-center gap-2">
                    {status === "Queued" && (
                        <>
                            <button
                                onClick={(e) => {
                                    e.stopPropagation();
                                    onRun(report);
                                }}
                                disabled={running || runPending}
                                className="flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-[11px] font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                                data-testid={`ai-report-run-${report.id}`}
                            >
                                {running ? (
                                    <>
                                        <Loader2 className="h-3 w-3 animate-spin" />
                                        Investigating…
                                    </>
                                ) : (
                                    <>
                                        <Play className="h-3 w-3" />
                                        Investigate
                                    </>
                                )}
                            </button>
                            <button
                                onClick={(e) => {
                                    e.stopPropagation();
                                    onSetStatus(report, "Done");
                                }}
                                disabled={statusPending}
                                className="flex items-center gap-1 rounded-md border px-2 py-1 text-[11px] text-muted-foreground hover:bg-accent"
                                title="Discard without investigating"
                                data-testid={`ai-report-discard-${report.id}`}
                            >
                                <X className="h-3 w-3" />
                                Discard
                            </button>
                        </>
                    )}
                    {status === "Ready" && (
                        <button
                            onClick={(e) => {
                                e.stopPropagation();
                                onSetStatus(report, "Done");
                            }}
                            disabled={statusPending}
                            className="flex items-center gap-1 rounded-md border px-2 py-1 text-[11px] text-muted-foreground hover:bg-accent"
                            data-testid={`ai-report-done-${report.id}`}
                        >
                            <CheckCircle2 className="h-3 w-3" />
                            Mark done
                        </button>
                    )}
                    {status === "Done" && (
                        <button
                            onClick={(e) => {
                                e.stopPropagation();
                                onSetStatus(report, "Ready");
                            }}
                            disabled={statusPending}
                            className="flex items-center gap-1 rounded-md border px-2 py-1 text-[11px] text-muted-foreground hover:bg-accent"
                            data-testid={`ai-report-reopen-${report.id}`}
                        >
                            <Undo2 className="h-3 w-3" />
                            Move to Ready
                        </button>
                    )}
                </div>
            </div>
        );
    };

    return (
        <div
            className="grid items-start gap-4 lg:grid-cols-[1fr_minmax(320px,420px)]"
            data-testid="ai-reports-panel"
        >
            <div
                className="grid items-start gap-3 sm:grid-cols-3"
                role="list"
                aria-label="AI reports board"
            >
                {COLUMNS.map((col) => {
                    const items = reports.filter(
                        (r) => (r.status ?? "Ready") === col.status,
                    );
                    return (
                        <div
                            key={col.status}
                            className="rounded-lg border bg-card/50"
                            data-testid={col.testId}
                        >
                            <div className="border-b px-3 py-2">
                                <div className="flex items-baseline justify-between gap-2">
                                    <span className="text-xs font-semibold uppercase tracking-wide">
                                        {col.label}
                                    </span>
                                    <span
                                        className="text-xs text-muted-foreground"
                                        data-testid={`${col.testId}-count`}
                                    >
                                        {items.length}
                                    </span>
                                </div>
                                <p className="mt-0.5 text-[11px] text-muted-foreground/70">
                                    {col.hint}
                                </p>
                            </div>
                            <div className="space-y-2 p-2">
                                {items.length === 0 ? (
                                    <p
                                        className="px-1 py-3 text-center text-[11px] text-muted-foreground/60"
                                        data-testid={`${col.testId}-empty`}
                                    >
                                        Nothing here
                                    </p>
                                ) : (
                                    items.map(renderCard)
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>

            <div className="min-w-0">
                {selected ? (
                    (selected.status ?? "Ready") === "Queued" ? (
                        <QueuedReportDetail
                            report={selected}
                            running={runningKeys.has(cardKey(selected))}
                            onRun={onRun}
                            onDiscard={(r) => onSetStatus(r, "Done")}
                            runPending={runPending}
                            statusPending={statusPending}
                        />
                    ) : (
                        <AiReportDetail
                            report={selected}
                            onDiscuss={onDiscuss}
                            onDelete={onDelete}
                            discussPending={discussPending}
                            deletePending={deletePending}
                        />
                    )
                ) : (
                    <div
                        className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground"
                        data-testid="ai-report-none-selected"
                    >
                        Select a card to see the details.
                    </div>
                )}
            </div>
        </div>
    );
}

/** Right-pane view of a Queued card (ai-reports-kanban): what was prepared — the
 * alert, the probe summary, and the raw gathered context — plus the Investigate
 * action the card also carries. */
function QueuedReportDetail({
    report,
    running,
    onRun,
    onDiscard,
    runPending,
    statusPending,
}: {
    report: ProactiveInsightReport;
    running: boolean;
    onRun: (report: ProactiveInsightReport) => void;
    onDiscard: (report: ProactiveInsightReport) => void;
    runPending?: boolean;
    statusPending?: boolean;
}) {
    return (
        <div className="rounded-lg border" data-testid="queued-report-detail">
            <div className="border-b px-4 py-3">
                <h2
                    className="text-base font-semibold"
                    data-testid="queued-report-name"
                >
                    {report.ruleName}
                </h2>
                <p className="text-xs text-muted-foreground">
                    Queued · {formatInsightTime(report.firedAt)}
                </p>
            </div>
            <div className="space-y-3 p-4">
                <div>
                    <div className="text-xs font-medium mb-1">Alert</div>
                    <p className="text-sm">{report.alertMessage}</p>
                    {report.alertDetail && (
                        <p className="mt-1 text-xs text-muted-foreground">
                            {report.alertDetail}
                        </p>
                    )}
                </div>
                <div>
                    <div className="text-xs font-medium mb-1">
                        Prepared context
                    </div>
                    <p className="text-xs text-muted-foreground">
                        {report.preparedContextSummary ??
                            "Alert + topology snapshot"}
                    </p>
                    {report.reportJson && (
                        <pre
                            className="mt-2 max-h-64 overflow-auto rounded-md bg-muted/50 p-2 text-[11px]"
                            data-testid="queued-report-probe"
                        >
                            {report.reportJson}
                        </pre>
                    )}
                </div>
            </div>
            <div className="flex items-center gap-2 border-t px-4 py-3">
                <button
                    onClick={() => onRun(report)}
                    disabled={running || runPending}
                    className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                    data-testid="queued-report-run"
                >
                    {running ? (
                        <>
                            <Loader2 className="h-3.5 w-3.5 animate-spin" />
                            Investigating…
                        </>
                    ) : (
                        <>
                            <Play className="h-3.5 w-3.5" />
                            Investigate
                        </>
                    )}
                </button>
                <button
                    onClick={() => onDiscard(report)}
                    disabled={statusPending}
                    className="rounded-md border px-3 py-1.5 text-sm text-muted-foreground hover:bg-accent"
                    data-testid="queued-report-discard"
                >
                    Discard
                </button>
            </div>
        </div>
    );
}
