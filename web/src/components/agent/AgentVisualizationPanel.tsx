import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { MermaidBlock } from "./AgentMarkdown";
import { AlertCircle, BarChart3, Calendar, Code2, GitBranch, Network, X } from "lucide-react";

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

export interface VisualBlock {
  id: string;
  kind: "mermaid" | "topology" | "timeline" | "json";
  title: string;
  code: string;
  payload?: VisualPayload;
}

const BLOCK_RE = /```(?:mermaid|json|topology|cytoscape|timeline)\r?\n([\s\S]*?)```/g;

function fallbackTitle(kind: VisualBlock["kind"], ordinal: number): string {
  const label = {
    mermaid: "Diagram",
    topology: "Topology",
    timeline: "Timeline",
    json: "Structured data",
  }[kind];
  return `${label} ${ordinal}`;
}

function precedingHeading(content: string, blockIndex: number): string | undefined {
  const headings = content.slice(0, blockIndex).matchAll(/^#{1,6}\s+(.+?)\s*$/gm);
  let title: string | undefined;
  for (const heading of headings) title = heading[1];
  return title;
}

export function parseVisualBlocks(content: string): VisualBlock[] {
  const blocks: VisualBlock[] = [];
  const seen = new Set<string>();
  for (const match of content.matchAll(BLOCK_RE)) {
    const raw = match[1].trim();
    const newlineIndex = match[0].indexOf("\n");
    if (newlineIndex < 0) continue;
    const lang = match[0].slice(3, newlineIndex).trim();
    const id = `${lang}-${raw.slice(0, 80)}`;
    if (seen.has(id)) continue;
    seen.add(id);

    let kind: VisualBlock["kind"] = "json";
    let payload: VisualPayload | undefined;
    if (lang === "mermaid") {
      kind = "mermaid";
    } else {
      try {
        payload = JSON.parse(raw) as VisualPayload;
        kind =
          (payload as { type?: string }).type === "topology"
            ? "topology"
            : (payload as { type?: string }).type === "timeline"
              ? "timeline"
              : "json";
      } catch {
        kind = "json";
      }
    }

    blocks.push({
      id,
      kind,
      title: precedingHeading(content, match.index ?? 0) ?? fallbackTitle(kind, blocks.length + 1),
      code: raw,
      payload,
    });
  }
  return blocks;
}

function TopologyGraph({ payload }: { payload: TopologyPayload }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const cyRef = useRef<cytoscape.Core | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    const nodeIds = new Set(payload.nodes.map((node) => node.id));
    const edges = payload.edges
      .filter((edge) => nodeIds.has(edge.from) && nodeIds.has(edge.to))
      .map((edge) => ({
        data: { source: edge.from, target: edge.to, label: edge.label ?? "" },
      }));

    import("cytoscape").then((mod) => {
      if (cancelled || !containerRef.current) return;
      cyRef.current = mod.default({
        container: containerRef.current,
        elements: [
          ...payload.nodes.map((node) => ({
            data: { id: node.id, label: node.label, area: node.area ?? "" },
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
        layout: { name: "cose", padding: 24, animate: false } as cytoscape.LayoutOptions,
      });
    });

    return () => {
      cancelled = true;
      cyRef.current?.destroy();
      cyRef.current = null;
    };
  }, [payload]);

  return (
    <div className="flex h-full min-h-[320px] flex-col rounded-lg border bg-card p-3" data-testid="topology-graph">
      <div className="mb-2 flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <Network className="h-3.5 w-3.5" /> Interactive topology
      </div>
      <div ref={containerRef} className="min-h-[280px] flex-1 rounded-md bg-muted/30" />
    </div>
  );
}

function TimelineView({ payload }: { payload: TimelinePayload }) {
  return (
    <div className="rounded-lg border bg-card p-4" data-testid="timeline-view">
      <div className="mb-4 flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <Calendar className="h-3.5 w-3.5" /> Incident progression
      </div>
      <div className="relative ml-2 border-l-2 border-muted pl-5">
        {payload.events.map((event, index) => (
          <div key={`${event.time}-${event.title}-${index}`} className="relative pb-5 last:pb-0">
            <span className="absolute -left-[calc(1.25rem+5px)] top-1.5 h-2.5 w-2.5 rounded-full bg-primary ring-2 ring-card" />
            <div className="text-sm font-medium">{event.title}</div>
            <div className="mt-0.5 text-xs font-medium text-primary">{event.time}</div>
            {event.description && <div className="mt-1 text-xs leading-relaxed text-muted-foreground">{event.description}</div>}
          </div>
        ))}
      </div>
    </div>
  );
}

function JsonFallback({ code }: { code: string }) {
  return (
    <div className="rounded-lg border bg-card p-3">
      <div className="mb-2 flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <Code2 className="h-3.5 w-3.5" /> Structured data
      </div>
      <pre className="overflow-auto rounded-md bg-muted p-3 text-xs">
        <code>{code}</code>
      </pre>
    </div>
  );
}

function VisualContent({ block }: { block: VisualBlock }) {
  if (block.kind === "mermaid") return <MermaidBlock code={block.code} />;
  if (block.kind === "topology" && block.payload?.type === "topology") {
    return <TopologyGraph payload={block.payload as TopologyPayload} />;
  }
  if (block.kind === "timeline" && block.payload?.type === "timeline") {
    return <TimelineView payload={block.payload as TimelinePayload} />;
  }
  return <JsonFallback code={block.code} />;
}

const kindIcons = {
  mermaid: GitBranch,
  topology: Network,
  timeline: Calendar,
  json: Code2,
};

export function AgentVisualizationPanel({
  content,
  onClose,
}: {
  content: string;
  onClose?: () => void;
}) {
  const blocks = parseVisualBlocks(content);
  const [activeId, setActiveId] = useState(blocks[0]?.id ?? "");
  const panelId = useId().replace(/:/g, "");
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const activeIndex = Math.max(0, blocks.findIndex((block) => block.id === activeId));
  const activeBlock = blocks[activeIndex];

  useEffect(() => {
    setActiveId(blocks[0]?.id ?? "");
  }, [content]);

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    let nextIndex = index;
    if (event.key === "ArrowRight") nextIndex = (index + 1) % blocks.length;
    else if (event.key === "ArrowLeft") nextIndex = (index - 1 + blocks.length) % blocks.length;
    else if (event.key === "Home") nextIndex = 0;
    else if (event.key === "End") nextIndex = blocks.length - 1;
    else return;
    event.preventDefault();
    setActiveId(blocks[nextIndex].id);
    tabRefs.current[nextIndex]?.focus();
  };

  return (
    <section className="flex h-full min-h-0 flex-col bg-background" data-testid="visualization-panel" aria-label="Visualization workspace">
      <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 text-sm font-semibold">
            <BarChart3 className="h-4 w-4 text-primary" /> Visual workspace
          </div>
          <p className="mt-0.5 truncate text-xs text-muted-foreground">
            {blocks.length > 0 ? `${blocks.length} views from the latest response` : "Latest response"}
          </p>
        </div>
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="rounded-md p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground focus:outline-none focus:ring-2 focus:ring-primary"
            data-testid="agent-visualization-close"
            aria-label="Close visualization workspace"
            title="Close visuals (Esc)"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      {blocks.length === 0 ? (
        <div className="m-4 rounded-lg border border-dashed bg-card p-5 text-sm text-muted-foreground" data-testid="agent-visualization-empty">
          <div className="flex items-start gap-2">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>No diagrams, maps, or timelines were found in the latest response.</span>
          </div>
        </div>
      ) : (
        <>
          <div className="border-b px-3 pt-2">
            <div className="flex gap-1 overflow-x-auto" role="tablist" aria-label="Available visualizations">
              {blocks.map((block, index) => {
                const Icon = kindIcons[block.kind];
                const selected = index === activeIndex;
                return (
                  <button
                    key={block.id}
                    ref={(element) => { tabRefs.current[index] = element; }}
                    type="button"
                    role="tab"
                    id={`${panelId}-tab-${index}`}
                    aria-selected={selected}
                    aria-controls={`${panelId}-panel-${index}`}
                    tabIndex={selected ? 0 : -1}
                    onClick={() => setActiveId(block.id)}
                    onKeyDown={(event) => handleTabKeyDown(event, index)}
                    className={`flex shrink-0 items-center gap-1.5 rounded-t-md border-b-2 px-3 py-2 text-xs font-medium transition-colors ${
                      selected
                        ? "border-primary text-foreground"
                        : "border-transparent text-muted-foreground hover:bg-accent/50 hover:text-foreground"
                    }`}
                    data-testid={`visualization-tab-${block.kind}-${index}`}
                  >
                    <Icon className="h-3.5 w-3.5" />
                    {block.title}
                  </button>
                );
              })}
            </div>
          </div>
          <div
            className="min-h-0 flex-1 overflow-auto bg-muted/10 p-4"
            role="tabpanel"
            id={`${panelId}-panel-${activeIndex}`}
            aria-labelledby={`${panelId}-tab-${activeIndex}`}
            data-testid="visualization-canvas"
          >
            <VisualContent block={activeBlock} />
          </div>
          <div className="border-t px-4 py-2 text-xs text-muted-foreground">
            View {activeIndex + 1} of {blocks.length}
          </div>
        </>
      )}
    </section>
  );
}
