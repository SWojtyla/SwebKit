import { useState } from "react";

interface AgentThoughtBlockProps {
  thoughts: string;
}

/**
 * Collapsed-by-default "Reasoning" block under an assistant reply, rendering the raw
 * `agent_thought_chunk` stream an ACP agent emits while it works (agent-correlation Module 4).
 * Always muted and closed initially — it's the model's scratchpad, not authoritative output, and
 * it must never read as part of the answer. Renders nothing when no thought chunks arrived
 * (non-ACP providers never emit them).
 */
export function AgentThoughtBlock({ thoughts }: AgentThoughtBlockProps) {
  const [expanded, setExpanded] = useState(false);

  if (!thoughts.trim()) return null;

  return (
    <div className="mt-1.5 text-xs" data-testid="agent-thought-block">
      <button
        onClick={() => setExpanded((v) => !v)}
        className="text-muted-foreground underline decoration-dotted hover:text-foreground"
        data-testid="agent-thought-toggle"
      >
        {expanded ? "Hide thoughts" : "Show thoughts"}
      </button>
      {expanded && (
        <div
          className="mt-1 whitespace-pre-wrap border-l pl-2 italic text-muted-foreground"
          data-testid="agent-thought-text"
        >
          {thoughts}
        </div>
      )}
    </div>
  );
}
