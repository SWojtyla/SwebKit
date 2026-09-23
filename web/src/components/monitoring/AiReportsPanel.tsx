import { Sparkles } from "lucide-react";
import type { ProactiveInsightReport } from "../../lib/api";
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
    discussPending?: boolean;
    deletePending?: boolean;
}

/**
 * The Monitoring "AI Reports" tab (ai-insight-reports): master list of every
 * persisted background investigation on the left, the selected report rendered
 * in full on the right. This is the permanent archive — the header insight cards
 * stay the transient "a new report just landed" surface.
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
    discussPending,
    deletePending,
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
                    fires, the generated report lands here — even after a restart.
                </p>
            </div>
        );
    }

    const selected = reports.find((r) => r.id === selectedId) ?? null;

    return (
        <div
            className="grid items-start gap-4 lg:grid-cols-[minmax(280px,340px)_1fr]"
            data-testid="ai-reports-panel"
        >
            <div className="space-y-2" role="list" aria-label="AI reports">
                {reports.map((report) => {
                    const isSelected = report.id === selected?.id;
                    return (
                        <button
                            key={report.id}
                            role="listitem"
                            aria-pressed={isSelected}
                            onClick={() =>
                                onSelect(isSelected ? null : report.id)
                            }
                            className={`w-full rounded-lg border px-3 py-2.5 text-left transition-colors ${
                                isSelected
                                    ? "border-primary/50 bg-primary/5"
                                    : "hover:bg-accent/50"
                            }`}
                            data-testid={`ai-report-row-${report.id}`}
                        >
                            <div className="flex items-center justify-between gap-2">
                                <span className="truncate text-sm font-medium">
                                    {report.ruleName}
                                </span>
                                <span
                                    className={`shrink-0 rounded px-1.5 py-0.5 text-[11px] ${insightSeverityBadge(report.severity)}`}
                                >
                                    {insightSeverityLabel(report.severity)}
                                </span>
                            </div>
                            <p className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
                                {report.hypothesis}
                            </p>
                            <p className="mt-1 text-[11px] text-muted-foreground/70">
                                {formatInsightTime(report.firedAt)}
                            </p>
                        </button>
                    );
                })}
            </div>

            <div className="min-w-0">
                {selected ? (
                    <AiReportDetail
                        report={selected}
                        onDiscuss={onDiscuss}
                        onDelete={onDelete}
                        discussPending={discussPending}
                        deletePending={deletePending}
                    />
                ) : (
                    <div
                        className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground"
                        data-testid="ai-report-none-selected"
                    >
                        Select a report to read the full investigation.
                    </div>
                )}
            </div>
        </div>
    );
}
