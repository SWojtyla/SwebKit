import { useState } from "react";
import { List, Network, Plus, Search, Trash2 } from "lucide-react";
import {
    useProfile,
    useUpdateProfile,
    useWorkspaceTopologyCandidates,
    useWorkspaceTopologySuggestions,
} from "@/lib/hooks";
import { ProfileListLayout } from "./ProfileListLayout";
import { TopologyGraph } from "@/components/shared/TopologyGraph";
import { WorkspaceMapAddPicker } from "./WorkspaceMapAddPicker";
import { WorkspaceMapInspector } from "./WorkspaceMapInspector";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { DraftInput } from "./DraftInput";
import { themeColor } from "@/lib/theme-colors";
import type {
    WorkspaceMap,
    WorkspaceResourceArea,
    WorkspaceResourceCandidate,
    WorkspaceResourceNode,
    WorkspaceTopology,
} from "@/lib/types";
import {
    AREA_LABELS,
    AREAS,
    buildGraphElements,
    EMPTY_TOPOLOGY,
    filterTopology,
    suggestionKey,
} from "./workspace-map-utils";

/** Per-area node colors — theme variables, resolved to literal colors at render
 * (cytoscape can't parse var()/oklch itself — see theme-colors.ts). */
const AREA_COLOR_VARS: Record<WorkspaceResourceArea, string> = {
    Aks: "--primary",
    ServiceBus: "--info",
    Redis: "--destructive",
    Sql: "--warning",
    Storage: "--success",
};

const AREA_COLOR_FALLBACKS: Record<WorkspaceResourceArea, string> = {
    Aks: "#5b8dd9",
    ServiceBus: "#3fa7d6",
    Redis: "#d9534f",
    Sql: "#d9a05b",
    Storage: "#59a869",
};

/**
 * Settings → Map tab. Each workspace map is a user-curated graph the agent
 * gets as context (see AgentSystemPromptBuilder's workspace-map section) — a
 * profile carries several, one per project/environment, so the view here is a
 * map selector on top of a graph-first editor: a canvas showing nodes colored
 * by area and relationships as labeled edges, an inspector that owns all
 * per-node editing, and a list fallback (same inspector) for
 * keyboard/screen-reader and e2e access. Everything still persists through the
 * whole-profile PUT — the redesign is presentation only, the model is untouched.
 */
export function WorkspaceMapSettings() {
    const { data: profile } = useProfile();
    const { data: candidates } = useWorkspaceTopologyCandidates();
    const { data: suggestions } = useWorkspaceTopologySuggestions();
    const updateProfile = useUpdateProfile();

    const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
    const [newMapName, setNewMapName] = useState("");
    const [confirmDeleteMap, setConfirmDeleteMap] = useState(false);
    const [view, setView] = useState<"graph" | "list">("graph");
    const [search, setSearch] = useState("");
    const [hiddenAreas, setHiddenAreas] = useState<Set<WorkspaceResourceArea>>(
        new Set(),
    );
    const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
    const [pickerOpen, setPickerOpen] = useState(false);
    // Dismissing a suggestion is session-only (per technical-plan.md Module 2 — no server-side
    // "accepted"/"dismissed" bookkeeping was scoped for this module, unlike Module 4's proactive
    // insights, which do need durable de-dup). A reload brings dismissed suggestions back.
    const [dismissedKeys, setDismissedKeys] = useState<Set<string>>(new Set());

    if (!profile) return null;

    const maps = profile.config.maps ?? [];
    const map = maps.find((m) => m.id === selectedMapId) ?? maps[0] ?? null;
    const topology: WorkspaceTopology = map ?? EMPTY_TOPOLOGY;
    const visibleAreas = new Set(AREAS.filter((a) => !hiddenAreas.has(a)));
    const filtered = filterTopology(topology, search, visibleAreas);
    // Suggestions arrive for every map; the server only ever pairs nodes that
    // share a map, so membership in the selected map's node set scopes them.
    const mapNodeIds = new Set(topology.nodes.map((n) => n.id));
    const visibleSuggestions = (suggestions ?? []).filter(
        (s) =>
            mapNodeIds.has(s.fromNodeId) &&
            mapNodeIds.has(s.toNodeId) &&
            !dismissedKeys.has(suggestionKey(s.fromNodeId, s.toNodeId)),
    );
    const graphElements = buildGraphElements(filtered, visibleSuggestions);
    const selectedNode =
        filtered.nodes.find((n) => n.id === selectedNodeId) ?? null;
    const areaColors = Object.fromEntries(
        AREAS.map((area) => [
            area,
            themeColor(AREA_COLOR_VARS[area], AREA_COLOR_FALLBACKS[area]),
        ]),
    );

    // Updater form so concurrent edits queue against current state instead of each
    // PUTting a profile snapshot taken before the other landed.
    const saveMaps = (mutate: (maps: WorkspaceMap[]) => WorkspaceMap[]) => {
        updateProfile.mutate((prev) => ({
            ...prev,
            config: { ...prev.config, maps: mutate(prev.config.maps ?? []) },
        }));
    };

    // Patches the selected map in place — every node/relationship edit goes through this.
    const save = (patch: Partial<WorkspaceTopology>) => {
        if (!map) return;
        saveMaps((list) =>
            list.map((m) => (m.id === map.id ? { ...m, ...patch } : m)),
        );
    };

    const createMap = (name: string) => {
        const trimmed = name.trim();
        if (!trimmed) return;
        const id = crypto.randomUUID().slice(0, 8);
        saveMaps((list) => [
            ...list,
            { id, name: trimmed, nodes: [], relationships: [] },
        ]);
        setSelectedMapId(id);
        setNewMapName("");
        setPickerOpen(true);
    };

    const renameMap = (name: string) => {
        if (!map || !name.trim()) return;
        saveMaps((list) =>
            list.map((m) =>
                m.id === map.id ? { ...m, name: name.trim() } : m,
            ),
        );
    };

    const deleteMap = () => {
        if (!map) return;
        saveMaps((list) => list.filter((m) => m.id !== map.id));
        setSelectedMapId(null);
        setSelectedNodeId(null);
        setConfirmDeleteMap(false);
    };

    const addNode = (node: WorkspaceResourceCandidate) => {
        save({
            nodes: [
                ...topology.nodes,
                { id: crypto.randomUUID().slice(0, 8), ...node },
            ],
        });
    };

    const renameNode = (id: string, displayLabel: string) => {
        save({
            nodes: topology.nodes.map((n) =>
                n.id === id ? { ...n, displayLabel } : n,
            ),
        });
    };

    const setNodeContext = (id: string, kubeconfigContext: string | null) => {
        save({
            nodes: topology.nodes.map((n) =>
                n.id === id ? { ...n, kubeconfigContext } : n,
            ),
        });
    };

    const removeNode = (id: string) => {
        save({
            nodes: topology.nodes.filter((n) => n.id !== id),
            relationships: topology.relationships.filter(
                (r) => r.fromNodeId !== id && r.toNodeId !== id,
            ),
        });
        if (selectedNodeId === id) setSelectedNodeId(null);
    };

    const addRelationship = (fromId: string, toId: string, label: string) => {
        if (!fromId || !toId || fromId === toId) return;
        save({
            relationships: [
                ...topology.relationships,
                {
                    id: crypto.randomUUID().slice(0, 8),
                    fromNodeId: fromId,
                    toNodeId: toId,
                    label: label || null,
                },
            ],
        });
    };

    const removeRelationship = (id: string) => {
        save({
            relationships: topology.relationships.filter((r) => r.id !== id),
        });
    };

    const confirmSuggestion = (fromNodeId: string, toNodeId: string) => {
        addRelationship(fromNodeId, toNodeId, "");
    };

    const dismissSuggestion = (fromNodeId: string, toNodeId: string) => {
        setDismissedKeys((prev) =>
            new Set(prev).add(suggestionKey(fromNodeId, toNodeId)),
        );
    };

    const toggleArea = (area: WorkspaceResourceArea) => {
        setHiddenAreas((prev) => {
            const next = new Set(prev);
            if (next.has(area)) next.delete(area);
            else next.add(area);
            return next;
        });
    };

    const inspector = (
        <WorkspaceMapInspector
            key={selectedNode?.id ?? "summary"}
            topology={filtered}
            node={selectedNode}
            suggestions={visibleSuggestions}
            onRenameNode={renameNode}
            onSetNodeContext={setNodeContext}
            onRemoveNode={removeNode}
            onAddRelationship={addRelationship}
            onRemoveRelationship={removeRelationship}
            onConfirmSuggestion={confirmSuggestion}
            onDismissSuggestion={dismissSuggestion}
            onSelectNode={setSelectedNodeId}
        />
    );

    return (
        <div className="space-y-4" data-testid="workspace-map-settings">
            <div>
                <h2 className="text-lg font-semibold">Workspace Maps</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                    Declare how your resources relate to each other (e.g. "this
                    deployment consumes this queue") — one map per project or
                    environment. The AI gets the relevant map as context, so
                    declared relationships shape how it investigates. Dashed
                    edges are heuristic suggestions you haven't confirmed yet.
                </p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
                <select
                    value={map?.id ?? ""}
                    onChange={(e) => {
                        setSelectedMapId(e.target.value || null);
                        setSelectedNodeId(null);
                        setConfirmDeleteMap(false);
                    }}
                    className="rounded-md border bg-card px-2 py-1.5 text-sm"
                    data-testid="workspace-map-selector"
                    aria-label="Workspace map"
                >
                    {maps.length === 0 && <option value="">No maps yet</option>}
                    {maps.map((m) => (
                        <option key={m.id} value={m.id}>
                            {m.name}
                        </option>
                    ))}
                </select>
                {map && (
                    <>
                        <DraftInput
                            key={map.id}
                            value={map.name}
                            onCommit={renameMap}
                            className="w-40 rounded-md border bg-card px-2 py-1.5 text-sm"
                            data-testid="workspace-map-name"
                            aria-label="Map name"
                        />
                        <button
                            onClick={() => setConfirmDeleteMap(true)}
                            className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-destructive hover:bg-accent"
                            data-testid="workspace-map-delete"
                        >
                            <Trash2 className="h-3 w-3" /> Delete map
                        </button>
                    </>
                )}
                <input
                    value={newMapName}
                    onChange={(e) => setNewMapName(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") createMap(newMapName);
                    }}
                    placeholder="New map name…"
                    className="w-40 rounded-md border bg-card px-2 py-1.5 text-sm"
                    data-testid="workspace-map-new-name"
                    aria-label="New map name"
                />
                <button
                    onClick={() => createMap(newMapName)}
                    disabled={!newMapName.trim()}
                    className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid="workspace-map-create"
                >
                    <Plus className="h-3 w-3" /> New map
                </button>
            </div>

            {confirmDeleteMap && map && (
                <ConfirmBar
                    message={`Delete map "${map.name}"? Its ${topology.nodes.length} resource(s) and ${topology.relationships.length} relationship(s) go with it.`}
                    confirmLabel="Delete map"
                    onConfirm={deleteMap}
                    onCancel={() => setConfirmDeleteMap(false)}
                    testId="workspace-map-delete-confirm"
                />
            )}

            {!map && (
                <p
                    className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
                    data-testid="workspace-map-no-maps"
                >
                    No workspace maps yet — create one above (one per project or
                    environment), then add the resources whose relationships the
                    AI should know about.
                </p>
            )}

            {map && (
                <>
                    <div className="flex flex-wrap items-center gap-2">
                        <div className="relative min-w-[200px] flex-1">
                            <Search className="pointer-events-none absolute left-2 top-2 h-3.5 w-3.5 text-muted-foreground" />
                            <input
                                value={search}
                                onChange={(e) => setSearch(e.target.value)}
                                placeholder="Filter by name or resource key…"
                                className="w-full rounded-md border bg-card py-1.5 pl-7 pr-2 text-sm"
                                data-testid="workspace-map-search"
                                aria-label="Filter map resources"
                            />
                        </div>
                        {AREAS.map((area) => {
                            const visible = visibleAreas.has(area);
                            return (
                                <button
                                    key={area}
                                    onClick={() => toggleArea(area)}
                                    aria-pressed={visible}
                                    className={`flex items-center gap-1.5 rounded-md border px-2 py-1.5 text-xs ${
                                        visible
                                            ? "bg-accent/60 text-foreground"
                                            : "text-muted-foreground opacity-50"
                                    }`}
                                    data-testid={`workspace-map-area-${area}`}
                                >
                                    <span
                                        className="h-2 w-2 rounded-full"
                                        style={{
                                            backgroundColor: areaColors[area],
                                        }}
                                    />
                                    {AREA_LABELS[area]}
                                </button>
                            );
                        })}
                        <div
                            className="flex rounded-md border"
                            role="group"
                            aria-label="Map view"
                        >
                            <button
                                onClick={() => setView("graph")}
                                aria-pressed={view === "graph"}
                                className={`flex items-center gap-1 rounded-l-md px-2 py-1.5 text-xs ${
                                    view === "graph"
                                        ? "bg-primary/15 text-foreground"
                                        : "text-muted-foreground hover:bg-accent"
                                }`}
                                data-testid="workspace-map-view-graph"
                            >
                                <Network className="h-3 w-3" /> Graph
                            </button>
                            <button
                                onClick={() => setView("list")}
                                aria-pressed={view === "list"}
                                className={`flex items-center gap-1 rounded-r-md px-2 py-1.5 text-xs ${
                                    view === "list"
                                        ? "bg-primary/15 text-foreground"
                                        : "text-muted-foreground hover:bg-accent"
                                }`}
                                data-testid="workspace-map-view-list"
                            >
                                <List className="h-3 w-3" /> List
                            </button>
                        </div>
                        <button
                            onClick={() => setPickerOpen((open) => !open)}
                            aria-expanded={pickerOpen}
                            className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                            data-testid="workspace-map-add-toggle"
                        >
                            <Plus className="h-3.5 w-3.5" /> Add resources
                        </button>
                    </div>

                    {(pickerOpen || topology.nodes.length === 0) && (
                        <WorkspaceMapAddPicker
                            candidates={candidates ?? []}
                            topology={topology}
                            onAddNode={(node) => {
                                addNode(node);
                                // The picker auto-opens on an empty map; pinning it open on
                                // the first add keeps multi-add sessions from having the
                                // panel yanked away mid-flow.
                                setPickerOpen(true);
                            }}
                        />
                    )}

                    {topology.nodes.length === 0 ? (
                        <p
                            className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
                            data-testid="workspace-map-empty"
                        >
                            The map is empty — add resources above, then connect
                            them so the AI can reason across your workspace
                            instead of one area at a time.
                        </p>
                    ) : view === "graph" ? (
                        <div className="flex gap-4">
                            <div className="flex min-h-[420px] min-w-0 flex-1 flex-col">
                                <TopologyGraph
                                    nodes={graphElements.nodes}
                                    edges={graphElements.edges}
                                    areaColors={areaColors}
                                    selectedNodeId={selectedNodeId}
                                    onNodeClick={(id) =>
                                        setSelectedNodeId(id === "" ? null : id)
                                    }
                                    testId="workspace-map-graph"
                                />
                                <p className="mt-1.5 text-xs text-muted-foreground">
                                    Click a node to inspect it · dashed edges
                                    are unconfirmed suggestions
                                </p>
                            </div>
                            <div className="w-80 shrink-0">{inspector}</div>
                        </div>
                    ) : (
                        <div data-testid="workspace-map-nodes">
                            <ProfileListLayout
                                items={filtered.nodes}
                                getKey={(n: WorkspaceResourceNode) => n.id}
                                getTitle={(n: WorkspaceResourceNode) =>
                                    n.displayLabel
                                }
                                getSubtitle={(n: WorkspaceResourceNode) =>
                                    n.resourceKey
                                }
                                getGroup={(n: WorkspaceResourceNode) =>
                                    AREA_LABELS[n.area]
                                }
                                getFilterText={(n: WorkspaceResourceNode) =>
                                    `${n.displayLabel} ${n.resourceKey}`
                                }
                                renderEditor={(n: WorkspaceResourceNode) => (
                                    <WorkspaceMapInspector
                                        key={n.id}
                                        topology={filtered}
                                        node={n}
                                        suggestions={visibleSuggestions}
                                        onRenameNode={renameNode}
                                        onSetNodeContext={setNodeContext}
                                        onRemoveNode={removeNode}
                                        onAddRelationship={addRelationship}
                                        onRemoveRelationship={
                                            removeRelationship
                                        }
                                        onConfirmSuggestion={confirmSuggestion}
                                        onDismissSuggestion={dismissSuggestion}
                                    />
                                )}
                                emptyMessage="No resources match the current filter."
                                testIdPrefix="workspace-map"
                            />
                        </div>
                    )}
                </>
            )}
        </div>
    );
}
