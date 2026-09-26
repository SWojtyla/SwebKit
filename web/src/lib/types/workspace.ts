export type WorkspaceResourceArea =
    | "Aks"
    | "ServiceBus"
    | "Redis"
    | "Storage"
    | "Sql";

export interface WorkspaceResourceNode {
    id: string;
    area: WorkspaceResourceArea;
    resourceKey: string;
    displayLabel: string;
    /** Optional kubeconfig context for AKS nodes — pins the node to one cluster;
     * null/absent means "whatever context is globally configured". */
    kubeconfigContext?: string | null;
}

export interface WorkspaceResourceRelationship {
    id: string;
    fromNodeId: string;
    toNodeId: string;
    label: string | null;
}

export interface WorkspaceTopology {
    nodes: WorkspaceResourceNode[];
    relationships: WorkspaceResourceRelationship[];
}

/** One named workspace map — a project/environment's own component graph. */
export interface WorkspaceMap extends WorkspaceTopology {
    id: string;
    name: string;
}

/** Not-yet-added node the user can pick from — computed by the sidecar from existing config, never
 * persisted itself. See `GET /api/workspace/topology/candidates`. */
export interface WorkspaceResourceCandidate {
    area: WorkspaceResourceArea;
    resourceKey: string;
    displayLabel: string;
    /** Same semantics as `WorkspaceResourceNode.kubeconfigContext` — carried
     * through so adding an AKS candidate preserves its cluster. */
    kubeconfigContext?: string | null;
}

/** A candidate relationship the heuristic scan found but nobody has confirmed yet
 * (workspace-intelligence Module 2) — never persisted; recomputed each time the Map view asks for
 * it. See `GET /api/workspace/topology/suggestions`. */
export interface WorkspaceRelationshipSuggestion {
    fromNodeId: string;
    toNodeId: string;
    reason: string;
}

export interface WorkspaceSnapshot {
    resource: OperatorResourceReference;
    restoreState: Record<string, string>;
    capturedAt: string;
}

export interface OperatorResourceReference {
    key: string;
    area: string;
    kind: string;
    displayName: string;
    displayPath?: string | null;
    summary?: string | null;
    icon?: string | null;
    metadata: Record<string, string>;
}
