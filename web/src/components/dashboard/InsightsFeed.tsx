import { useNavigate } from "react-router";
import { Sparkles, X } from "lucide-react";
import type { ProactiveInsightReadyEvent } from "@/lib/api";

/**
 * Live proactive-insights feed. "Investigate" deep-links to the persisted AI
 * report on the Monitoring page (`/monitoring?tab=reports&report=<id>`), where
 * the real report → chat handoff lives — this card only routes to it, it does
 * not fabricate a conversation like the previous version did.
 */
export function InsightsFeed({
    insights,
    onDismiss,
}: {
    insights: ProactiveInsightReadyEvent[];
    onDismiss: (insight: ProactiveInsightReadyEvent) => void;
}) {
    const navigate = useNavigate();

    const investigate = (insight: ProactiveInsightReadyEvent) => {
        // Report ids are `proactive-{ruleId}-{firedAt ms}` — see
        // ProactiveInsightService.cs's deterministic id assignment.
        const reportId = `proactive-${insight.ruleId}-${new Date(insight.firedAt).getTime()}`;
        onDismiss(insight);
        navigate(`/monitoring?tab=reports&report=${encodeURIComponent(reportId)}`);
    };

    return (
        <div
            className="glass-card rounded-xl p-4"
            data-testid="cockpit-insights"
        >
            <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold">
                <Sparkles className="h-4 w-4 text-primary" /> Proactive Insights
            </h2>
            {insights.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                    No insights yet — they appear here when an alert triggers an
                    AI investigation.
                </p>
            ) : (
                <div className="space-y-2">
                    {insights.slice(0, 5).map((insight) => (
                        <div
                            key={`${insight.ruleId}|${insight.firedAt}`}
                            className="rounded-lg border border-primary/30 bg-primary/5 p-3"
                            data-testid={`cockpit-insight-${insight.ruleId}`}
                        >
                            <div className="flex items-start gap-2">
                                <div className="min-w-0 flex-1">
                                    <div className="text-xs font-medium">
                                        {insight.ruleName}
                                    </div>
                                    <div className="text-xs text-muted-foreground line-clamp-2">
                                        {insight.summary}
                                    </div>
                                </div>
                                <button
                                    type="button"
                                    aria-label={`Dismiss ${insight.ruleName} insight`}
                                    title="Dismiss"
                                    onClick={() => onDismiss(insight)}
                                    className="rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
                                    data-testid={`cockpit-insight-dismiss-${insight.ruleId}`}
                                >
                                    <X className="h-3.5 w-3.5" />
                                </button>
                            </div>
                            <button
                                type="button"
                                onClick={() => investigate(insight)}
                                className="mt-2 inline-flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
                                data-testid={`cockpit-insight-investigate-${insight.ruleId}`}
                            >
                                Investigate
                            </button>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
