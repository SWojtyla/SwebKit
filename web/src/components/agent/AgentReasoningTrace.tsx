import { useState } from "react";
import type { AgentChatStep } from "@/lib/types";
import { describeReasoningToggleLabel } from "./reasoning-trace";

interface AgentReasoningTraceProps {
  steps: AgentChatStep[];
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
