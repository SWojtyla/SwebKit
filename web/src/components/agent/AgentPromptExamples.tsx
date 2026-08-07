import { useRef } from "react";
import { Lightbulb } from "lucide-react";

const EXAMPLES = [
  {
    label: "Investigate order-api incident with diagram, topology, and timeline",
    prompt:
      "My order-api is failing in AKS. Investigate across AKS logs, App Insights dependencies, SQL DB, and Service Bus. Show a Mermaid sequence diagram, a JSON topology of the impacted services, and an incident timeline.",
  },
  {
    label: "Compare AKS deployments and Service Bus queue depth",
    prompt:
      "Compare the health of AKS deployments with Service Bus queue depths. Highlight any correlations and include a Mermaid flowchart.",
  },
  {
    label: "Show a Redis latency timeline",
    prompt:
      "Show me a timeline of Redis cache hit-rate and latency spikes over the last hour.",
  },
  {
    label: "Topology of payment service dependencies",
    prompt:
      "Draw a topology map of the payment service dependencies across AKS, Service Bus, and Storage.",
  },
];

interface AgentPromptExamplesProps {
  onSelect: (prompt: string) => void;
}

export function AgentPromptExamples({ onSelect }: AgentPromptExamplesProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  return (
    <div ref={containerRef} className="flex flex-col gap-1.5 px-6 pb-2 pt-1" data-testid="agent-prompt-examples">
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Lightbulb className="h-3 w-3" /> Try asking for visuals:
      </div>
      <div className="flex flex-wrap gap-2">
        {EXAMPLES.map((example) => (
          <button
            key={example.label}
            onClick={() => onSelect(example.prompt)}
            className="max-w-full truncate rounded-full border border-primary/30 bg-primary/5 px-2.5 py-1 text-xs text-primary hover:bg-primary/10"
            title={example.prompt}
          >
            {example.label}
          </button>
        ))}
      </div>
    </div>
  );
}
