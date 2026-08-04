interface ContextUsageIndicatorProps {
  percent: number;
  /** The percentage at which the indicator turns to a warning color — defaults to 75 for callers
   * that don't yet have the backend's scaled threshold. */
  warningAt?: number;
}

/**
 * Small, unobtrusive context-window usage indicator (workspace-intelligence Module 6/7) — an
 * always-visible percentage that only calls attention to itself once a conversation is actually
 * getting full (matching ux-plan.md: summarization handling it gracefully isn't a failure state).
 * Renders nothing at 0% (no turn sent yet) to avoid a meaningless "0%" on a fresh conversation.
 * Warning threshold is scaled to the active profile's context window in Module 7, so a tiny local
 * model turns yellow sooner (in absolute tokens) than a big cloud one.
 */
export function ContextUsageIndicator({ percent, warningAt = 75 }: ContextUsageIndicatorProps) {
  if (percent <= 0) return null;

  const isGettingFull = percent >= warningAt;

  return (
    <span
      className={isGettingFull ? "text-warning" : "text-muted-foreground"}
      data-testid="agent-context-usage"
      title="Percentage of the model's context window used by the current conversation"
    >
      · {Math.round(percent)}% of context window
    </span>
  );
}
