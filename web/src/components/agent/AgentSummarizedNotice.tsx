/**
 * Inline notice shown under an assistant reply whose turn triggered rolling summarization
 * (workspace-intelligence Module 5/6) — placed exactly where the summarization happened
 * chronologically, per ux-plan.md, never a silent, confusing loss of information.
 */
export function AgentSummarizedNotice() {
  return (
    <div className="mt-1 text-xs italic text-muted-foreground" data-testid="agent-summarized-notice">
      Earlier parts of this conversation were summarized to stay within the model's context window.
    </div>
  );
}
