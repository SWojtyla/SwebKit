import { describe, expect, it } from "vitest";
import { parseVisualBlocks } from "./AgentVisualizationPanel";

const topology = JSON.stringify({
  type: "topology",
  nodes: [{ id: "api", label: "API" }],
  edges: [],
});

describe("parseVisualBlocks", () => {
  it("uses preceding headings as labels and recognizes each visual kind", () => {
    const content = `### Request flow
\`\`\`mermaid
sequenceDiagram
A->>B: request
\`\`\`

### Impact map
\`\`\`json
${topology}
\`\`\`

### Incident timeline
\`\`\`timeline
{"type":"timeline","events":[]}
\`\`\``;

    expect(parseVisualBlocks(content).map(({ kind, title }) => ({ kind, title }))).toEqual([
      { kind: "mermaid", title: "Request flow" },
      { kind: "topology", title: "Impact map" },
      { kind: "timeline", title: "Incident timeline" },
    ]);
  });

  it("supports CRLF fences and removes exact duplicate blocks", () => {
    const block = "```mermaid\r\ngraph LR\r\nA-->B\r\n```";

    const blocks = parseVisualBlocks(`${block}\r\n${block}`);

    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toMatchObject({ kind: "mermaid", code: "graph LR\r\nA-->B" });
  });

  it("falls back to structured data for invalid JSON", () => {
    const [block] = parseVisualBlocks("```json\n{not-json}\n```");

    expect(block).toMatchObject({ kind: "json", title: "Structured data 1" });
  });

  it("ignores malformed fences without a language newline", () => {
    expect(parseVisualBlocks("```mermaid graph LR A-->B```")).toEqual([]);
  });
});
