import { X } from "lucide-react";
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
        {insight.evidence && insight.evidence.length > 0 && (
          <ul className="mt-1 list-disc space-y-0.5 pl-4 text-xs text-muted-foreground/80">
            {insight.evidence.slice(0, 4).map((item, i) => (
              <li key={i}>{item}</li>
            ))}
          </ul>
        )}
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
          className="rounded-md p-1.5 hover:bg-accent"
          title="Dismiss"
          data-testid={`proactive-insight-dismiss-${insight.ruleId}-${insight.firedAt}`}
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
}
