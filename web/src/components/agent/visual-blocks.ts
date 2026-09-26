export interface TopologyNode {
  id: string;
  label: string;
  area?: string;
}

export interface TopologyEdge {
  from: string;
  to: string;
  label?: string;
}

export interface TopologyPayload {
  type: "topology";
  nodes: TopologyNode[];
  edges: TopologyEdge[];
}

export interface TimelineEvent {
  time: string;
  title: string;
  description?: string;
}

export interface TimelinePayload {
  type: "timeline";
  events: TimelineEvent[];
}

export type VisualPayload = TopologyPayload | TimelinePayload | Record<string, unknown>;

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

    let kind: VisualBlock["kind"];
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
