import { useState } from "react";
import type { AgentChatStep } from "@/lib/types";

interface AgentReasoningTraceProps {
  steps: AgentChatStep[];
}

/** Counts steps that represent a failed tool call, for the collapsed toggle's own label — a failed
 * data source shouldn't be buried behind a disclosure most users never open (unit 7.4). Exported and
 * kept pure so it's unit-testable without rendering the component. */
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

/**
 * Collapsed-by-default "Show reasoning" disclosure under an assistant reply — workspace-intelligence
 * Module 6. A debugging/trust aid, not the primary reading experience, so it never auto-expands.
 * Renders nothing when there are no steps (a turn that used no tools).
 */
export function AgentReasoningTrace({ steps }: AgentReasoningTraceProps) {
  const [expanded, setExpanded] = useState(false);

  if (steps.length === 0) return null;

  return (
    <div className="mt-1.5 text-xs" data-testid="agent-reasoning-trace">
      <button
        onClick={() => setExpanded((v) => !v)}
        className="text-muted-foreground underline decoration-dotted hover:text-foreground"
        data-testid="agent-reasoning-trace-toggle"
      >
        {expanded ? "Hide reasoning" : describeReasoningToggleLabel(steps)}
      </button>
      {expanded && (
        <ul className="mt-1 space-y-0.5 border-l pl-2 text-muted-foreground" data-testid="agent-reasoning-trace-steps">
          {steps.map((step, i) => {
            const isFailedResult = step.type === "tool_result" && step.isFailure;
            return (
              <li
                key={i}
                className={isFailedResult ? "text-warning" : undefined}
                data-testid={isFailedResult ? "agent-reasoning-trace-step-failed" : undefined}
              >
                {step.type === "tool_call" ? "→ " : "← "}
                {step.summary}
                {step.elapsed && step.type === "tool_result" && (
                  <span className="opacity-70"> ({step.elapsed})</span>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
