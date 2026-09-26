import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { ArrowRight, Network } from "lucide-react";
import { TopologyGraph } from "@/components/shared/TopologyGraph";
import { themeColor } from "@/lib/theme-colors";
import {
    AREA_LABELS,
    buildGraphElements,
} from "@/components/settings/workspace-map-utils";
import type { WorkspaceMap, WorkspaceResourceArea } from "@/lib/types";

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

const AREA_ROUTES: Record<WorkspaceResourceArea, string> = {
    Aks: "/aks",
    ServiceBus: "/service-bus",
    Redis: "/redis",
    Sql: "/sql",
    Storage: "/storage",
};

/**
 * The workspace map rendered as an actual graph — same TopologyGraph (cytoscape)
 * the map editor and agent visualizations use, not a static pill list. Clicking
 * a node jumps to that resource's feature page. With several maps defined, a
 * compact selector picks which one to render.
 */
export function CockpitTopology({ maps }: { maps: WorkspaceMap[] }) {
    const navigate = useNavigate();
    const nonEmpty = maps.filter((m) => m.nodes.length > 0);
    const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
    const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

    const map =
        nonEmpty.find((m) => m.id === selectedMapId) ?? nonEmpty[0] ?? null;

    const areaColors = useMemo(
        () =>
            Object.fromEntries(
                Object.entries(AREA_COLOR_VARS).map(([area, varName]) => [
                    area,
                    themeColor(
                        varName,
                        AREA_COLOR_FALLBACKS[
                            area as WorkspaceResourceArea
                        ],
                    ),
                ]),
            ),
        [],
    );

    const { nodes, edges } = map
        ? buildGraphElements(map, [])
        : { nodes: [], edges: [] };

    const handleNodeClick = (id: string) => {
        setSelectedNodeId(id);
        const node = map?.nodes.find((n) => n.id === id);
        if (node) navigate(AREA_ROUTES[node.area]);
    };

    return (
        <div className="glass-card rounded-xl p-4" data-testid="cockpit-topology">
            <div className="mb-2 flex items-center justify-between gap-2">
                <h2 className="flex items-center gap-2 text-sm font-semibold">
                    <Network className="h-4 w-4 text-primary" /> Workspace
                    Topology
                </h2>
                {nonEmpty.length > 1 && (
                    <select
                        value={map?.id ?? ""}
                        onChange={(e) => setSelectedMapId(e.target.value)}
                        className="rounded border bg-background px-1.5 py-0.5 text-xs"
                        aria-label="Workspace map"
                        data-testid="cockpit-topology-map-picker"
                    >
                        {nonEmpty.map((m) => (
                            <option key={m.id} value={m.id}>
                                {m.name}
                            </option>
                        ))}
                    </select>
                )}
            </div>

            {map ? (
                <>
                    <TopologyGraph
                        nodes={nodes}
                        edges={edges}
                        areaColors={areaColors}
                        onNodeClick={handleNodeClick}
                        selectedNodeId={selectedNodeId}
                        testId="cockpit-topology-graph"
                    />
                    <div className="mt-2 flex items-center justify-between">
                        <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
                            {Object.entries(AREA_LABELS)
                                .filter(([area]) =>
                                    map.nodes.some((n) => n.area === area),
                                )
                                .map(([area, label]) => (
                                    <span
                                        key={area}
                                        className="flex items-center gap-1"
                                    >
                                        <span
                                            className="h-2 w-2 rounded-full"
                                            style={{
                                                backgroundColor:
                                                    areaColors[area],
                                            }}
                                        />
                                        {label}
                                    </span>
                                ))}
                        </div>
                        <Link
                            to="/settings/map"
                            className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
                            data-testid="cockpit-topology-edit"
                        >
                            Edit maps <ArrowRight className="h-3 w-3" />
                        </Link>
                    </div>
                </>
            ) : (
                <p className="text-sm text-muted-foreground">
                    No workspace maps yet. Configure resources and relationships
                    in{" "}
                    <Link
                        to="/settings/map"
                        className="text-primary hover:underline"
                    >
                        Settings → Map
                    </Link>
                    .
                </p>
            )}
        </div>
    );
}
