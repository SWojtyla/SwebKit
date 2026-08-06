import { useEffect, useRef } from "react";
import { MermaidBlock } from "./AgentMarkdown";
import { Network, Calendar, AlertCircle } from "lucide-react";

interface TopologyNode {
  id: string;
  label: string;
  area?: string;
}

interface TopologyEdge {
  from: string;
  to: string;
  label?: string;
}

interface TopologyPayload {
  type: "topology";
  nodes: TopologyNode[];
  edges: TopologyEdge[];
}

interface TimelineEvent {
  time: string;
  title: string;
  description?: string;
}

interface TimelinePayload {
  type: "timeline";
  events: TimelineEvent[];
}

type VisualPayload = TopologyPayload | TimelinePayload | Record<string, unknown>;

interface VisualBlock {
  id: string;
  kind: "mermaid" | "topology" | "timeline" | "json";
  code: string;
  payload?: VisualPayload;
}

const BLOCK_RE = /```(?:mermaid|json|topology|cytoscape|timeline)\n([\s\S]*?)```/g;

function parseVisualBlocks(content: string): VisualBlock[] {
  const blocks: VisualBlock[] = [];
  const seen = new Set<string>();
  for (const match of content.matchAll(BLOCK_RE)) {
    const raw = match[1].trim();
    const lang = match[0].slice(3, match[0].indexOf("\n")).trim();
    const id = `${lang}-${raw.slice(0, 80)}`;
    if (seen.has(id)) continue;
    seen.add(id);

    if (lang === "mermaid") {
      blocks.push({ id, kind: "mermaid", code: raw });
      continue;
    }

    try {
      const payload = JSON.parse(raw) as VisualPayload;
      const kind = (payload as { type?: string }).type === "topology" ? "topology" : (payload as { type?: string }).type === "timeline" ? "timeline" : "json";
      blocks.push({ id, kind, code: raw, payload });
    } catch {
      blocks.push({ id, kind: "json", code: raw });
    }
  }
  return blocks;
}

function TopologyGraph({ payload }: { payload: TopologyPayload }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const cyRef = useRef<cytoscape.Core | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    const nodeIds = new Set(payload.nodes.map((n) => n.id));
    const edges = payload.edges
      .filter((e) => nodeIds.has(e.from) && nodeIds.has(e.to))
      .map((e) => ({
        data: { source: e.from, target: e.to, label: e.label ?? "" },
      }));

    import("cytoscape").then((mod) => {
      const cytoscape = mod.default;
      cyRef.current = cytoscape({
        container: containerRef.current,
        elements: [
          ...payload.nodes.map((n) => ({
            data: { id: n.id, label: n.label, area: n.area ?? "" },
          })),
          ...edges,
        ],
        style: [
          {
            selector: "node",
            style: {
              label: "data(label)",
              "background-color": "hsl(var(--primary))",
              color: "hsl(var(--foreground))",
              "text-valign": "center",
              "text-halign": "center",
              "font-size": "10px",
              width: "40px",
              height: "40px",
            },
          },
          {
            selector: "edge",
            style: {
              width: 2,
              "line-color": "hsl(var(--muted-foreground))",
              "target-arrow-color": "hsl(var(--muted-foreground))",
              "target-arrow-shape": "triangle",
              "curve-style": "bezier",
              label: "data(label)",
              "font-size": "9px",
              color: "hsl(var(--foreground))",
            },
          },
        ],
        layout: { name: "cose", padding: 10, animate: false } as cytoscape.LayoutOptions,
      });
    });

    return () => {
      cyRef.current?.destroy();
      cyRef.current = null;
    };
  }, [payload]);

  return (
    <div className="rounded border bg-card p-2">
      <div className="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
        <Network className="h-3.5 w-3.5" /> Topology map
      </div>
      <div ref={containerRef} className="h-[300px] w-full rounded bg-muted/30" />
    </div>
  );
}

function TimelineView({ payload }: { payload: TimelinePayload }) {
  return (
    <div className="rounded border bg-card p-2">
      <div className="mb-2 flex items-center gap-1 text-xs text-muted-foreground">
        <Calendar className="h-3.5 w-3.5" /> Timeline
      </div>
      <div className="relative ml-2 border-l-2 border-muted pl-4">
        {payload.events.map((evt, i) => (
          <div key={i} className="relative mb-3">
            <span className="absolute -left-[calc(1rem+5px)] top-1.5 h-2.5 w-2.5 rounded-full bg-primary ring-2 ring-card" />
            <div className="text-xs font-medium">{evt.title}</div>
            <div className="text-xs text-muted-foreground">{evt.time}</div>
            {evt.description && <div className="mt-0.5 text-xs text-foreground">{evt.description}</div>}
          </div>
        ))}
      </div>
    </div>
  );
}

function JsonFallback({ code }: { code: string }) {
  return (
    <details className="rounded border bg-card p-2">
      <summary className="cursor-pointer text-xs text-muted-foreground">Structured data</summary>
      <pre className="mt-1 overflow-x-auto text-xs">
        <code>{code}</code>
      </pre>
    </details>
  );
}

export function AgentVisualizationPanel({ content }: { content: string }) {
  const blocks = parseVisualBlocks(content);

  if (blocks.length === 0) {
    return (
      <div className="rounded border bg-card p-4 text-sm text-muted-foreground" data-testid="agent-visualization-empty">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-4 w-4" />
          No diagrams, maps, or timelines found in the last response. Ask the agent to produce a Mermaid diagram or a JSON topology/timeline block.
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-3" data-testid="agent-visualization-panel">
      {blocks.map((block) => (
        <div key={block.id}>
          {block.kind === "mermaid" && <MermaidBlock code={block.code} />}
          {block.kind === "topology" && block.payload?.type === "topology" && <TopologyGraph payload={block.payload as TopologyPayload} />}
          {block.kind === "timeline" && block.payload?.type === "timeline" && <TimelineView payload={block.payload as TimelinePayload} />}
          {block.kind === "json" && <JsonFallback code={block.code} />}
        </div>
      ))}
    </div>
  );
}
