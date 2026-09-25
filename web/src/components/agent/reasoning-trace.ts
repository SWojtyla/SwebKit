import type { AgentChatStep } from "@/lib/types";

/** Counts steps that represent a failed tool call, for the collapsed toggle's own label — a failed
 * data source shouldn't be buried behind a disclosure most users never open (unit 7.4). Kept pure
 * so it's unit-testable without rendering the component. */
export function countFailedSteps(steps: AgentChatStep[]): number {
  return steps.filter((step) => step.type === "tool_result" && step.isFailure).length;
}

/** Builds the "Show reasoning (…)" toggle label — plain step count when nothing failed, with an
 * explicit ", N failed" suffix appended when at least one tool call did (unit 7.4). */
export function describeReasoningToggleLabel(steps: AgentChatStep[]): string {
  const stepWord = `${steps.length} step${steps.length === 1 ? "" : "s"}`;
  const failedCount = countFailedSteps(steps);
  return failedCount > 0 ? `Show reasoning (${stepWord}, ${failedCount} failed)` : `Show reasoning (${stepWord})`;
}
