import type { ProactiveInsightReadyEvent } from "../../lib/api";

interface ProactiveInsightCardProps {
  insight: ProactiveInsightReadyEvent;
  onInvestigate: (insight: ProactiveInsightReadyEvent) => void;
  onDismiss: (insight: ProactiveInsightReadyEvent) => void;
}

/**
 * Dismissible card for a background-completed proactive investigation (workspace-intelligence
 * Module 4) — visually distinct from a regular alert-history row (this is a generated hypothesis,
 * not a raw signal) and from a chat message. Short and scannable per ux-plan.md: what fired, a
 * one-line generated hypothesis, an "Investigate" button — never a full unprompted essay.
 */
export function ProactiveInsightCard({ insight, onInvestigate, onDismiss }: ProactiveInsightCardProps) {
  return (
    <div
      className="flex items-start justify-between gap-3 rounded-lg border border-primary/30 bg-primary/5 px-4 py-3"
      data-testid={`proactive-insight-${insight.ruleId}-${insight.firedAt}`}
    >
      <div className="min-w-0">
        <div className="text-sm font-semibold">{insight.ruleName} — possibly related</div>
        <p className="mt-0.5 text-sm text-muted-foreground">{insight.summary}</p>
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <button
          onClick={() => onInvestigate(insight)}
          className="rounded-md bg-primary px-3 py-1.5 text-xs font-medium text-primary-foreground hover:opacity-90"
          data-testid={`proactive-insight-investigate-${insight.ruleId}-${insight.firedAt}`}
        >
          Investigate
        </button>
        <button
          onClick={() => onDismiss(insight)}
          className="rounded-md px-2 py-1.5 text-xs hover:bg-accent"
          data-testid={`proactive-insight-dismiss-${insight.ruleId}-${insight.firedAt}`}
        >
          ✕
        </button>
      </div>
    </div>
  );
}
