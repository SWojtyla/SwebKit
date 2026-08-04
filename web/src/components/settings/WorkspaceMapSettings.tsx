import { useState } from "react";
import {
  useProfile,
  useUpdateProfile,
  useWorkspaceTopologyCandidates,
  useWorkspaceTopologySuggestions,
} from "@/lib/hooks";
import type { WorkspaceResourceArea, WorkspaceResourceNode, WorkspaceTopology } from "@/lib/types";

const AREA_LABELS: Record<WorkspaceResourceArea, string> = {
  Aks: "AKS",
  ServiceBus: "Service Bus",
  Redis: "Redis",
  Storage: "Storage",
};

const AREAS: WorkspaceResourceArea[] = ["Aks", "ServiceBus", "Redis", "Storage"];

const EMPTY_TOPOLOGY: WorkspaceTopology = { nodes: [], relationships: [] };

export function WorkspaceMapSettings() {
  const { data: profile } = useProfile();
  const { data: candidates } = useWorkspaceTopologyCandidates();
  const { data: suggestions } = useWorkspaceTopologySuggestions();
  const updateProfile = useUpdateProfile();

  const [manualArea, setManualArea] = useState<WorkspaceResourceArea>("Aks");
  const [manualKey, setManualKey] = useState("");
  const [manualLabel, setManualLabel] = useState("");
  const [relFrom, setRelFrom] = useState("");
  const [relTo, setRelTo] = useState("");
  const [relLabel, setRelLabel] = useState("");
  // Dismissing a suggestion is session-only (per technical-plan.md Module 2 — no server-side
  // "accepted"/"dismissed" bookkeeping was scoped for this module, unlike Module 4's proactive
  // insights, which do need durable de-dup). A reload brings dismissed suggestions back.
  const [dismissedKeys, setDismissedKeys] = useState<Set<string>>(new Set());

  if (!profile) return null;

  const topology = profile.config.topology ?? EMPTY_TOPOLOGY;

  const save = (patch: Partial<WorkspaceTopology>) => {
    updateProfile.mutate({
      ...profile,
      config: {
        ...profile.config,
        topology: { ...topology, ...patch },
      },
    });
  };

  const addNode = (node: Omit<WorkspaceResourceNode, "id">) => {
    save({ nodes: [...topology.nodes, { id: crypto.randomUUID().slice(0, 8), ...node }] });
  };

  const removeNode = (id: string) => {
    save({
      nodes: topology.nodes.filter((n) => n.id !== id),
      relationships: topology.relationships.filter((r) => r.fromNodeId !== id && r.toNodeId !== id),
    });
  };

  const addRelationship = () => {
    if (!relFrom || !relTo || relFrom === relTo) return;
    save({
      relationships: [
        ...topology.relationships,
        { id: crypto.randomUUID().slice(0, 8), fromNodeId: relFrom, toNodeId: relTo, label: relLabel.trim() || null },
      ],
    });
    setRelFrom("");
    setRelTo("");
    setRelLabel("");
  };

  const removeRelationship = (id: string) => {
    save({ relationships: topology.relationships.filter((r) => r.id !== id) });
  };

  const isAdded = (area: WorkspaceResourceArea, resourceKey: string) =>
    topology.nodes.some((n) => n.area === area && n.resourceKey === resourceKey);

  const nodeLabel = (id: string) => topology.nodes.find((n) => n.id === id)?.displayLabel ?? "(unknown)";

  const suggestionKey = (fromNodeId: string, toNodeId: string) => `${fromNodeId}|${toNodeId}`;

  const confirmSuggestion = (fromNodeId: string, toNodeId: string) => {
    save({
      relationships: [
        ...topology.relationships,
        { id: crypto.randomUUID().slice(0, 8), fromNodeId, toNodeId, label: null },
      ],
    });
  };

  const dismissSuggestion = (fromNodeId: string, toNodeId: string) => {
    setDismissedKeys((prev) => new Set(prev).add(suggestionKey(fromNodeId, toNodeId)));
  };

  const visibleSuggestions = (suggestions ?? []).filter(
    (s) => !dismissedKeys.has(suggestionKey(s.fromNodeId, s.toNodeId)),
  );

  return (
    <div className="space-y-6" data-testid="workspace-map-settings">
      <div>
        <h2 className="text-lg font-semibold">Workspace Map</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Declare how your resources relate to each other (e.g. "this deployment consumes this
          queue") so the AI agent can reason across areas instead of one at a time. Nothing here is
          inferred automatically — you add and remove everything explicitly.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-6">
        <div className="space-y-3" data-testid="workspace-map-nodes">
          <h3 className="text-sm font-semibold text-muted-foreground">Known resources</h3>
          {AREAS.map((area) => {
            const areaCandidates = (candidates ?? []).filter((c) => c.area === area && !isAdded(c.area, c.resourceKey));
            const areaNodes = topology.nodes.filter((n) => n.area === area);
            if (areaCandidates.length === 0 && areaNodes.length === 0) return null;

            return (
              <div key={area} className="rounded-lg border p-3">
                <div className="mb-2 text-xs font-semibold uppercase text-muted-foreground">{AREA_LABELS[area]}</div>
                <ul className="space-y-1">
                  {areaNodes.map((node) => (
                    <li key={node.id} className="flex items-center justify-between text-sm" data-testid={`workspace-node-${node.id}`}>
                      <span>
                        {node.displayLabel} <span className="text-muted-foreground">({node.resourceKey})</span>
                      </span>
                      <button onClick={() => removeNode(node.id)} className="text-xs text-destructive hover:opacity-80">
                        Remove
                      </button>
                    </li>
                  ))}
                  {areaCandidates.map((candidate) => (
                    <li
                      key={`${candidate.area}-${candidate.resourceKey}`}
                      className="flex items-center justify-between text-sm text-muted-foreground"
                      data-testid={`workspace-candidate-${candidate.area}-${candidate.resourceKey}`}
                    >
                      <span>
                        {candidate.displayLabel} <span>({candidate.resourceKey})</span>
                      </span>
                      <button
                        onClick={() =>
                          addNode({ area: candidate.area, resourceKey: candidate.resourceKey, displayLabel: candidate.displayLabel })
                        }
                        className="text-xs text-primary hover:opacity-80"
                      >
                        Add
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            );
          })}

          <div className="rounded-lg border p-3">
            <div className="mb-2 text-xs font-semibold uppercase text-muted-foreground">Add a custom resource</div>
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={manualArea}
                onChange={(e) => setManualArea(e.target.value as WorkspaceResourceArea)}
                className="rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-manual-area"
              >
                {AREAS.map((area) => (
                  <option key={area} value={area}>
                    {AREA_LABELS[area]}
                  </option>
                ))}
              </select>
              <input
                value={manualKey}
                onChange={(e) => setManualKey(e.target.value)}
                placeholder="Resource key, e.g. prod-ns/orders-queue"
                className="min-w-[220px] flex-1 rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-manual-key"
              />
              <input
                value={manualLabel}
                onChange={(e) => setManualLabel(e.target.value)}
                placeholder="Display label"
                className="min-w-[140px] flex-1 rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-manual-label"
              />
              <button
                onClick={() => {
                  if (!manualKey.trim()) return;
                  addNode({ area: manualArea, resourceKey: manualKey.trim(), displayLabel: manualLabel.trim() || manualKey.trim() });
                  setManualKey("");
                  setManualLabel("");
                }}
                className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                data-testid="workspace-manual-add"
              >
                Add
              </button>
            </div>
          </div>
        </div>

        <div className="space-y-3" data-testid="workspace-map-relationships">
          <h3 className="text-sm font-semibold text-muted-foreground">Relationships</h3>

          {visibleSuggestions.length > 0 && (
            <div className="space-y-2 rounded-lg border border-dashed p-3" data-testid="workspace-suggestions">
              <div className="text-xs font-semibold uppercase text-muted-foreground">
                Suggested — confirm?
              </div>
              <ul className="space-y-2">
                {visibleSuggestions.map((s) => (
                  <li
                    key={suggestionKey(s.fromNodeId, s.toNodeId)}
                    className="rounded-md bg-accent/30 p-2 text-sm"
                    data-testid={`workspace-suggestion-${s.fromNodeId}-${s.toNodeId}`}
                  >
                    <div>
                      {nodeLabel(s.fromNodeId)} → {nodeLabel(s.toNodeId)}
                    </div>
                    <div className="mt-0.5 text-xs text-muted-foreground">{s.reason}</div>
                    <div className="mt-1.5 flex gap-2">
                      <button
                        onClick={() => confirmSuggestion(s.fromNodeId, s.toNodeId)}
                        className="rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
                        data-testid={`workspace-suggestion-confirm-${s.fromNodeId}-${s.toNodeId}`}
                      >
                        Confirm
                      </button>
                      <button
                        onClick={() => dismissSuggestion(s.fromNodeId, s.toNodeId)}
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
          )}

          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground">
                <th className="pb-1">From</th>
                <th className="pb-1">Label</th>
                <th className="pb-1">To</th>
                <th className="pb-1" />
              </tr>
            </thead>
            <tbody>
              {topology.relationships.map((rel) => (
                <tr key={rel.id} data-testid={`workspace-relationship-${rel.id}`}>
                  <td className="py-1">{nodeLabel(rel.fromNodeId)}</td>
                  <td className="py-1 text-muted-foreground">{rel.label ?? "—"}</td>
                  <td className="py-1">{nodeLabel(rel.toNodeId)}</td>
                  <td className="py-1 text-right">
                    <button onClick={() => removeRelationship(rel.id)} className="text-xs text-destructive hover:opacity-80">
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {topology.nodes.length < 2 ? (
            <p className="text-sm text-muted-foreground">Add at least two resources on the left before declaring a relationship.</p>
          ) : (
            <div className="flex flex-wrap items-center gap-2 rounded-lg border p-3">
              <select
                value={relFrom}
                onChange={(e) => setRelFrom(e.target.value)}
                className="rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-relationship-from"
              >
                <option value="">From…</option>
                {topology.nodes.map((n) => (
                  <option key={n.id} value={n.id}>
                    {n.displayLabel}
                  </option>
                ))}
              </select>
              <input
                value={relLabel}
                onChange={(e) => setRelLabel(e.target.value)}
                placeholder="e.g. consumes"
                className="min-w-[120px] flex-1 rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-relationship-label"
              />
              <select
                value={relTo}
                onChange={(e) => setRelTo(e.target.value)}
                className="rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid="workspace-relationship-to"
              >
                <option value="">To…</option>
                {topology.nodes.map((n) => (
                  <option key={n.id} value={n.id}>
                    {n.displayLabel}
                  </option>
                ))}
              </select>
              <button
                onClick={addRelationship}
                disabled={!relFrom || !relTo || relFrom === relTo}
                className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
                data-testid="workspace-relationship-add"
              >
                Add
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
