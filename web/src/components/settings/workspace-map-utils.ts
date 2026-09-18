import type {
    WorkspaceRelationshipSuggestion,
    WorkspaceResourceArea,
    WorkspaceResourceCandidate,
    WorkspaceTopology,
} from "@/lib/types";
import type { TopologyGraphEdge, TopologyGraphNode } from "@/components/shared/TopologyGraph";

export const AREA_LABELS: Record<WorkspaceResourceArea, string> = {
    Aks: "AKS",
    ServiceBus: "Service Bus",
    Redis: "Redis",
    Sql: "SQL",
    Storage: "Storage",
};

export const AREAS: WorkspaceResourceArea[] = [
    "Aks",
    "ServiceBus",
    "Redis",
    "Sql",
    "Storage",
];

export const EMPTY_TOPOLOGY: WorkspaceTopology = { nodes: [], relationships: [] };

export const suggestionKey = (fromNodeId: string, toNodeId: string) =>
    `${fromNodeId}|${toNodeId}`;

export const nodeLabel = (topology: WorkspaceTopology, id: string) =>
    topology.nodes.find((n) => n.id === id)?.displayLabel ?? "(unknown)";

/** Relationships touching a node, either direction — edges are undirected for
 * counting purposes (the graph treats "A consumes B" and "B is consumed by A"
 * as the same link). */
export const relationshipCountFor = (
    topology: WorkspaceTopology,
    nodeId: string,
) =>
    topology.relationships.filter(
        (r) => r.fromNodeId === nodeId || r.toNodeId === nodeId,
    ).length;

export const isOrphan = (topology: WorkspaceTopology, nodeId: string) =>
    relationshipCountFor(topology, nodeId) === 0;

/**
 * Narrows a topology to what the filter controls allow: nodes matching `search`
 * (label or resource key, case-insensitive) inside `areas`, plus the
 * relationships whose endpoints both survived. A relationship can never be
 * visible when one of its nodes is filtered out — a half-shown edge reads as a
 * data error, not a filter result.
 */
export function filterTopology(
    topology: WorkspaceTopology,
    search: string,
    areas: ReadonlySet<WorkspaceResourceArea>,
): WorkspaceTopology {
    const term = search.trim().toLowerCase();
    const nodes = topology.nodes.filter(
        (n) =>
            areas.has(n.area) &&
            (term === "" ||
                n.displayLabel.toLowerCase().includes(term) ||
                n.resourceKey.toLowerCase().includes(term)),
    );
    const ids = new Set(nodes.map((n) => n.id));
    return {
        nodes,
        relationships: topology.relationships.filter(
            (r) => ids.has(r.fromNodeId) && ids.has(r.toNodeId),
        ),
    };
}

/**
 * Confirmed relationships + visible suggestions as graph edges. Suggestions are
 * only drawn when both endpoints exist in the (possibly filtered) node set and
 * the pair isn't already declared — same suppression rule the suggestion
 * service applies server-side, re-applied here against the filtered view.
 */
export function buildGraphElements(
    topology: WorkspaceTopology,
    suggestions: WorkspaceRelationshipSuggestion[],
): { nodes: TopologyGraphNode[]; edges: TopologyGraphEdge[] } {
    const declared = new Set(
        topology.relationships.flatMap((r) => [
            suggestionKey(r.fromNodeId, r.toNodeId),
            suggestionKey(r.toNodeId, r.fromNodeId),
        ]),
    );
    const nodeIds = new Set(topology.nodes.map((n) => n.id));

    return {
        nodes: topology.nodes.map((n) => ({
            id: n.id,
            label: n.displayLabel,
            area: n.area,
        })),
        edges: [
            ...topology.relationships.map((r) => ({
                from: r.fromNodeId,
                to: r.toNodeId,
                label: r.label ?? undefined,
            })),
            ...suggestions
                .filter(
                    (s) =>
                        nodeIds.has(s.fromNodeId) &&
                        nodeIds.has(s.toNodeId) &&
                        !declared.has(suggestionKey(s.fromNodeId, s.toNodeId)),
                )
                .map((s) => ({
                    from: s.fromNodeId,
                    to: s.toNodeId,
                    dashed: true,
                })),
        ],
    };
}

/** Suggestions that touch a given node — the inspector only shows the ones
 * relevant to what the user selected. */
export const suggestionsForNode = (
    suggestions: WorkspaceRelationshipSuggestion[],
    nodeId: string,
) =>
    suggestions.filter(
        (s) => s.fromNodeId === nodeId || s.toNodeId === nodeId,
    );

/** Remaining (not-yet-added) candidates grouped by area, sorted by label. */
export function groupCandidates(
    candidates: WorkspaceResourceCandidate[],
    topology: WorkspaceTopology,
): Map<WorkspaceResourceArea, WorkspaceResourceCandidate[]> {
    const added = new Set(
        topology.nodes.map((n) => `${n.area}|${n.resourceKey}`),
    );
    const groups = new Map<WorkspaceResourceArea, WorkspaceResourceCandidate[]>();
    for (const c of candidates) {
        if (added.has(`${c.area}|${c.resourceKey}`)) continue;
        const list = groups.get(c.area) ?? [];
        list.push(c);
        groups.set(c.area, list);
    }
    for (const list of groups.values())
        list.sort((a, b) => a.displayLabel.localeCompare(b.displayLabel));
    return groups;
}
