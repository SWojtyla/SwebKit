import { useEffect, useRef } from "react";
import { themeColor } from "@/lib/theme-colors";

export interface TopologyGraphNode {
    id: string;
    label: string;
    /** Grouping key — looked up in `areaColors` for the node's fill. */
    area?: string;
}

export interface TopologyGraphEdge {
    from: string;
    to: string;
    label?: string;
    /** Unconfirmed/suggested edge — rendered dashed. */
    dashed?: boolean;
}

interface TopologyGraphProps {
    nodes: TopologyGraphNode[];
    edges: TopologyGraphEdge[];
    /** Fill color per `TopologyGraphNode.area` — literal colors (see
     * `themeColor`: cytoscape can't parse `var()` or oklch). Nodes whose area
     * isn't listed fall back to the theme's primary color. */
    areaColors?: Record<string, string>;
    onNodeClick?: (id: string) => void;
    selectedNodeId?: string | null;
    testId?: string;
}

// fcose registers itself as a cytoscape extension once per module — repeated
// `cytoscape.use()` calls would warn. Both stay behind dynamic imports so the
// ~300KB graph stack only loads when a graph is actually on screen.
let fcoseRegistered = false;

function buildStyles(foreground: string, muted: string, edgeLabelBg: string) {
    return [
        {
            selector: "node",
            style: {
                label: "data(label)",
                "background-color": "data(color)",
                color: foreground,
                "text-valign": "bottom",
                "text-halign": "center",
                "text-margin-y": 4,
                "font-size": "10px",
                "text-wrap": "wrap",
                "text-max-width": "110px",
                "text-background-color": edgeLabelBg,
                "text-background-opacity": 0.75,
                "text-background-padding": "2px",
                width: "34px",
                height: "34px",
            },
        },
        {
            selector: "node:selected",
            style: {
                "border-width": 3,
                "border-color": foreground,
            },
        },
        {
            selector: "edge",
            style: {
                width: 2,
                "line-color": muted,
                "target-arrow-color": muted,
                "target-arrow-shape": "triangle",
                "curve-style": "bezier",
                label: "data(label)",
                "font-size": "9px",
                color: foreground,
                "text-rotation": "autorotate",
                "text-background-color": edgeLabelBg,
                "text-background-opacity": 0.75,
                "text-background-padding": "1px",
            },
        },
        {
            selector: "edge[dashed]",
            style: {
                "line-style": "dashed",
                "target-arrow-shape": "triangle",
                opacity: 0.7,
            },
        },
    ] as cytoscape.StylesheetJson;
}

/**
 * Shared cytoscape wrapper — renders a labeled node/edge graph. Used by the
 * workspace Map (Settings) and the agent visualization panel's ```topology
 * blocks. Elements are replaced wholesale on prop change and the layout re-runs;
 * node clicks and selection flow through the callbacks/props.
 */
export function TopologyGraph({
    nodes,
    edges,
    areaColors,
    onNodeClick,
    selectedNodeId,
    testId,
}: TopologyGraphProps) {
    const containerRef = useRef<HTMLDivElement>(null);
    const cyRef = useRef<cytoscape.Core | null>(null);
    const onNodeClickRef = useRef(onNodeClick);
    // Latest props for the init effect, which runs once — the elements effect
    // below handles all later updates without rebuilding the instance.
    const elementsRef = useRef({ nodes, edges, areaColors });
    useEffect(() => {
        onNodeClickRef.current = onNodeClick;
        elementsRef.current = { nodes, edges, areaColors };
    });

    // Init once the dynamic imports land.
    useEffect(() => {
        if (!containerRef.current) return;
        let cancelled = false;

        Promise.all([import("cytoscape"), import("cytoscape-fcose")]).then(
            ([cyMod, fcoseMod]) => {
                if (cancelled || !containerRef.current) return;
                const cytoscape = cyMod.default;
                if (!fcoseRegistered) {
                    cytoscape.use(fcoseMod.default);
                    fcoseRegistered = true;
                }

                const foreground = themeColor("--foreground", "#cccccc");
                const muted = themeColor("--muted-foreground", "#888888");
                const bg = themeColor("--card", "#222222");
                const primary = themeColor("--primary", "#5b8dd9");

                const cy = cytoscape({
                    container: containerRef.current,
                    style: buildStyles(foreground, muted, bg),
                    userZoomingEnabled: true,
                    userPanningEnabled: true,
                    boxSelectionEnabled: false,
                });
                cyRef.current = cy;

                cy.on("tap", "node", (evt) => {
                    const id = (evt.target as cytoscape.NodeSingular).id();
                    onNodeClickRef.current?.(id);
                });
                // Tap on empty canvas clears the selection — clicking a node is
                // how you "open" it, so the inverse gesture should close it.
                cy.on("tap", (evt) => {
                    if (evt.target === cy) onNodeClickRef.current?.("");
                });

                syncElements(cy, elementsRef.current, primary);
            },
        );

        return () => {
            cancelled = true;
            cyRef.current?.destroy();
            cyRef.current = null;
        };
    }, []);

    // Re-sync elements + layout when the graph data changes.
    useEffect(() => {
        const cy = cyRef.current;
        if (!cy) return;
        const primary = themeColor("--primary", "#5b8dd9");
        syncElements(cy, { nodes, edges, areaColors }, primary);
    }, [nodes, edges, areaColors]);

    // Selection is a cy-side concern — re-select without re-laying out.
    useEffect(() => {
        const cy = cyRef.current;
        if (!cy) return;
        cy.nodes().unselect();
        if (selectedNodeId) cy.getElementById(selectedNodeId).select();
    }, [selectedNodeId]);

    return (
        <div
            ref={containerRef}
            className="min-h-[320px] flex-1 rounded-md bg-muted/30"
            data-testid={testId}
        />
    );
}

function syncElements(
    cy: cytoscape.Core,
    data: {
        nodes: TopologyGraphNode[];
        edges: TopologyGraphEdge[];
        areaColors?: Record<string, string>;
    },
    fallbackColor: string,
) {
    const nodeIds = new Set(data.nodes.map((n) => n.id));
    cy.elements().remove();
    cy.add([
        ...data.nodes.map((n) => ({
            data: {
                id: n.id,
                label: n.label,
                color: (n.area && data.areaColors?.[n.area]) || fallbackColor,
            },
        })),
        ...data.edges
            .filter((e) => nodeIds.has(e.from) && nodeIds.has(e.to))
            .map((e) => ({
                data: {
                    source: e.from,
                    target: e.to,
                    label: e.label ?? "",
                    dashed: e.dashed ? true : undefined,
                },
            })),
    ]);
    cy.layout({
        name: "fcose",
        padding: 32,
        animate: false,
        nodeSeparation: 90,
        idealEdgeLength: 110,
    } as cytoscape.LayoutOptions).run();
}
