import { useState } from "react";
import { GitBranch, Trash2 } from "lucide-react";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { DraftInput } from "./DraftInput";
import type {
    WorkspaceRelationshipSuggestion,
    WorkspaceResourceNode,
    WorkspaceTopology,
} from "@/lib/types";
import {
    AREA_LABELS,
    isOrphan,
    nodeLabel,
    relationshipCountFor,
    suggestionKey,
    suggestionsForNode,
} from "./workspace-map-utils";

interface WorkspaceMapInspectorProps {
    topology: WorkspaceTopology;
    /** Selected node — `null` renders the map summary instead of node details. */
    node: WorkspaceResourceNode | null;
    /** Session-visible suggestions (dismissed ones already filtered out). */
    suggestions: WorkspaceRelationshipSuggestion[];
    onRenameNode: (id: string, label: string) => void;
    onRemoveNode: (id: string) => void;
    onAddRelationship: (fromId: string, toId: string, label: string) => void;
    onRemoveRelationship: (id: string) => void;
    onConfirmSuggestion: (fromId: string, toId: string) => void;
    onDismissSuggestion: (fromId: string, toId: string) => void;
    /** Optional — without it, related node names render as plain text rather
     * than a button that does nothing (list view owns its own selection). */
    onSelectNode?: (id: string) => void;
}

/**
 * Right-hand detail pane for the workspace Map: everything that acts on ONE
 * node lives here (rename, its relationships, add-relationship, remove) so the
 * graph/list views stay uncluttered. With nothing selected it shows the map
 * summary + all pending suggestions instead.
 */
export function WorkspaceMapInspector({
    topology,
    node,
    suggestions,
    onRenameNode,
    onRemoveNode,
    onAddRelationship,
    onRemoveRelationship,
    onConfirmSuggestion,
    onDismissSuggestion,
    onSelectNode,
}: WorkspaceMapInspectorProps) {
    const [relTo, setRelTo] = useState("");
    const [relLabel, setRelLabel] = useState("");
    const [confirmRemoveNode, setConfirmRemoveNode] = useState(false);
    const [pendingRemoveRelId, setPendingRemoveRelId] = useState<string | null>(
        null,
    );

    if (!node) {
        return (
            <div
                className="space-y-3 rounded-lg border p-3"
                data-testid="workspace-map-inspector"
            >
                <div className="text-xs font-semibold uppercase text-muted-foreground">
                    Map summary
                </div>
                <p className="text-sm text-muted-foreground">
                    {topology.nodes.length} resource(s),{" "}
                    {topology.relationships.length} relationship(s). Select a
                    node to inspect or edit it.
                </p>
                {suggestions.length > 0 && (
                    <SuggestionList
                        topology={topology}
                        suggestions={suggestions}
                        onConfirm={onConfirmSuggestion}
                        onDismiss={onDismissSuggestion}
                    />
                )}
            </div>
        );
    }

    const nodeRelationships = topology.relationships.filter(
        (r) => r.fromNodeId === node.id || r.toNodeId === node.id,
    );
    const nodeSuggestions = suggestionsForNode(suggestions, node.id);
    const otherNodes = topology.nodes.filter((n) => n.id !== node.id);
    const orphan = isOrphan(topology, node.id);

    return (
        <div
            className="space-y-4 rounded-lg border p-3"
            data-testid="workspace-map-inspector"
        >
            <div className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                    <span className="rounded bg-primary/15 px-1.5 py-0.5 text-[10px] font-medium text-primary">
                        {AREA_LABELS[node.area]}
                    </span>
                    <button
                        onClick={() => setConfirmRemoveNode(true)}
                        className="flex items-center gap-1 text-xs text-destructive hover:opacity-80"
                        data-testid={`workspace-node-remove-${node.id}`}
                    >
                        <Trash2 className="h-3 w-3" /> Remove node
                    </button>
                </div>
                <DraftInput
                    value={node.displayLabel}
                    onCommit={(label) => {
                        const trimmed = label.trim();
                        if (trimmed) onRenameNode(node.id, trimmed);
                    }}
                    className="w-full rounded-md border bg-card px-2 py-1.5 text-sm font-medium"
                    data-testid="workspace-inspector-label"
                    aria-label="Display label"
                />
                <div
                    className="truncate text-xs text-muted-foreground"
                    title={node.resourceKey}
                    data-testid="workspace-inspector-key"
                >
                    {node.resourceKey}
                </div>
                {orphan && (
                    <p
                        className="rounded-md bg-warning/10 px-2 py-1 text-xs text-warning"
                        data-testid="workspace-inspector-orphan"
                    >
                        Not connected to anything yet — relationships are what
                        let the AI reason across this resource.
                    </p>
                )}
            </div>

            {confirmRemoveNode && (
                <ConfirmBar
                    message={
                        relationshipCountFor(topology, node.id) > 0
                            ? `Remove "${node.displayLabel}"? This also removes ${relationshipCountFor(topology, node.id)} relationship(s) that reference it.`
                            : `Remove "${node.displayLabel}" from the workspace map?`
                    }
                    confirmLabel="Remove"
                    onConfirm={() => {
                        onRemoveNode(node.id);
                        setConfirmRemoveNode(false);
                    }}
                    onCancel={() => setConfirmRemoveNode(false)}
                    testId={`workspace-node-remove-confirm-${node.id}`}
                />
            )}

            <div className="space-y-2">
                <div className="flex items-center gap-1.5 text-xs font-semibold uppercase text-muted-foreground">
                    <GitBranch className="h-3 w-3" /> Relationships
                </div>
                <table
                    className="w-full text-sm"
                    data-testid="workspace-map-relationships"
                >
                    <tbody>
                        {nodeRelationships.map((rel) => {
                            const otherId =
                                rel.fromNodeId === node.id
                                    ? rel.toNodeId
                                    : rel.fromNodeId;
                            const direction =
                                rel.fromNodeId === node.id ? "→" : "←";
                            return (
                                <tr
                                    key={rel.id}
                                    data-testid={`workspace-relationship-${rel.id}`}
                                >
                                    <td className="py-1 text-muted-foreground">
                                        {direction}
                                    </td>
                                    <td className="py-1">
                                        {onSelectNode ? (
                                            <button
                                                onClick={() =>
                                                    onSelectNode(otherId)
                                                }
                                                className="text-left hover:underline"
                                            >
                                                {nodeLabel(topology, otherId)}
                                            </button>
                                        ) : (
                                            nodeLabel(topology, otherId)
                                        )}
                                    </td>
                                    <td className="py-1 text-muted-foreground">
                                        {rel.label ?? "—"}
                                    </td>
                                    <td className="py-1 text-right">
                                        <button
                                            onClick={() =>
                                                setPendingRemoveRelId(rel.id)
                                            }
                                            className="text-xs text-destructive hover:opacity-80"
                                        >
                                            Remove
                                        </button>
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
                {pendingRemoveRelId && (
                    <ConfirmBar
                        message="Remove this relationship?"
                        confirmLabel="Remove"
                        onConfirm={() => {
                            onRemoveRelationship(pendingRemoveRelId);
                            setPendingRemoveRelId(null);
                        }}
                        onCancel={() => setPendingRemoveRelId(null)}
                        testId={`workspace-relationship-remove-confirm-${pendingRemoveRelId}`}
                    />
                )}
                {nodeRelationships.length === 0 && (
                    <p className="text-xs text-muted-foreground">
                        No relationships declared for this resource.
                    </p>
                )}

                {otherNodes.length > 0 && (
                    <div className="flex flex-wrap items-center gap-2">
                        <select
                            value={relTo}
                            onChange={(e) => setRelTo(e.target.value)}
                            className="min-w-[140px] flex-1 rounded-md border bg-card px-2 py-1.5 text-sm"
                            data-testid="workspace-relationship-to"
                            aria-label="Related resource"
                        >
                            <option value="">Relates to…</option>
                            {otherNodes.map((n) => (
                                <option key={n.id} value={n.id}>
                                    {n.displayLabel} ({AREA_LABELS[n.area]})
                                </option>
                            ))}
                        </select>
                        <input
                            value={relLabel}
                            onChange={(e) => setRelLabel(e.target.value)}
                            placeholder="e.g. consumes"
                            className="w-28 rounded-md border bg-card px-2 py-1.5 text-sm"
                            data-testid="workspace-relationship-label"
                            aria-label="Relationship label"
                        />
                        <button
                            onClick={() => {
                                if (!relTo) return;
                                onAddRelationship(
                                    node.id,
                                    relTo,
                                    relLabel.trim(),
                                );
                                setRelTo("");
                                setRelLabel("");
                            }}
                            disabled={!relTo}
                            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
                            data-testid="workspace-relationship-add"
                        >
                            Add
                        </button>
                    </div>
                )}
            </div>

            {nodeSuggestions.length > 0 && (
                <SuggestionList
                    topology={topology}
                    suggestions={nodeSuggestions}
                    onConfirm={onConfirmSuggestion}
                    onDismiss={onDismissSuggestion}
                />
            )}
        </div>
    );
}

function SuggestionList({
    topology,
    suggestions,
    onConfirm,
    onDismiss,
}: {
    topology: WorkspaceTopology;
    suggestions: WorkspaceRelationshipSuggestion[];
    onConfirm: (fromId: string, toId: string) => void;
    onDismiss: (fromId: string, toId: string) => void;
}) {
    return (
        <div
            className="space-y-2 rounded-lg border border-dashed p-3"
            data-testid="workspace-suggestions"
        >
            <div className="text-xs font-semibold uppercase text-muted-foreground">
                Suggested — confirm?
            </div>
            <ul className="space-y-2">
                {suggestions.map((s) => (
                    <li
                        key={suggestionKey(s.fromNodeId, s.toNodeId)}
                        className="rounded-md bg-accent/30 p-2 text-sm"
                        data-testid={`workspace-suggestion-${s.fromNodeId}-${s.toNodeId}`}
                    >
                        <div>
                            {nodeLabel(topology, s.fromNodeId)} →{" "}
                            {nodeLabel(topology, s.toNodeId)}
                        </div>
                        <div className="mt-0.5 text-xs text-muted-foreground">
                            {s.reason}
                        </div>
                        <div className="mt-1.5 flex gap-2">
                            <button
                                onClick={() =>
                                    onConfirm(s.fromNodeId, s.toNodeId)
                                }
                                className="rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
                                data-testid={`workspace-suggestion-confirm-${s.fromNodeId}-${s.toNodeId}`}
                            >
                                Confirm
                            </button>
                            <button
                                onClick={() =>
                                    onDismiss(s.fromNodeId, s.toNodeId)
                                }
                                className="rounded-md border px-2 py-1 text-xs hover:bg-accent"
                                data-testid={`workspace-suggestion-dismiss-${s.fromNodeId}-${s.toNodeId}`}
                            >
                                Dismiss
                            </button>
                        </div>
                    </li>
                ))}
            </ul>
        </div>
    );
}
