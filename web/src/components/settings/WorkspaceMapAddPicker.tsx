import { useState } from "react";
import { Plus, Search } from "lucide-react";
import type {
    WorkspaceResourceArea,
    WorkspaceResourceCandidate,
    WorkspaceTopology,
} from "@/lib/types";
import { AREA_LABELS, AREAS, groupCandidates } from "./workspace-map-utils";

interface WorkspaceMapAddPickerProps {
    candidates: WorkspaceResourceCandidate[];
    topology: WorkspaceTopology;
    onAddNode: (node: WorkspaceResourceCandidate) => void;
}

/**
 * "Add resources" panel: every not-yet-added candidate grouped by area with a
 * search box, plus the manual custom-resource form for things the sidecar can't
 * discover (e.g. a specific queue inside a namespace). Replaces the old inline
 * per-area candidate lists, which interleaved added nodes and candidates in one
 * scroll and didn't scale.
 */
export function WorkspaceMapAddPicker({
    candidates,
    topology,
    onAddNode,
}: WorkspaceMapAddPickerProps) {
    const [search, setSearch] = useState("");
    const [manualArea, setManualArea] = useState<WorkspaceResourceArea>("Aks");
    const [manualKey, setManualKey] = useState("");
    const [manualLabel, setManualLabel] = useState("");

    const term = search.trim().toLowerCase();
    const grouped = groupCandidates(candidates, topology);
    const visibleGroups = AREAS.map((area) => {
        const items = (grouped.get(area) ?? []).filter(
            (c) =>
                term === "" ||
                c.displayLabel.toLowerCase().includes(term) ||
                c.resourceKey.toLowerCase().includes(term),
        );
        return [area, items] as const;
    }).filter(([, items]) => items.length > 0);

    const addAll = (items: readonly WorkspaceResourceCandidate[]) =>
        items.forEach((c) => onAddNode(c));

    return (
        <div
            className="space-y-3 rounded-lg border p-3"
            data-testid="workspace-map-add-picker"
        >
            <div className="flex items-center gap-2">
                <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search configured resources…"
                    className="w-full rounded-md border bg-card px-2 py-1 text-sm"
                    data-testid="workspace-map-add-search"
                />
            </div>

            {visibleGroups.length === 0 && (
                <p className="text-xs text-muted-foreground">
                    {term
                        ? `No configured resources match "${search.trim()}".`
                        : "Every configured resource is already on the map."}
                </p>
            )}

            {visibleGroups.map(([area, items]) => (
                <div key={area}>
                    <div className="flex items-center justify-between pb-1">
                        <span className="text-xs font-semibold uppercase text-muted-foreground">
                            {AREA_LABELS[area]}
                        </span>
                        {items.length > 1 && (
                            <button
                                onClick={() => addAll(items)}
                                className="text-xs text-primary hover:opacity-80"
                                data-testid={`workspace-candidate-add-all-${area}`}
                            >
                                Add all ({items.length})
                            </button>
                        )}
                    </div>
                    <ul className="space-y-0.5">
                        {items.map((candidate) => (
                            <li
                                key={`${candidate.area}-${candidate.resourceKey}`}
                                className="flex items-center justify-between gap-2 rounded-md px-2 py-1 text-sm text-muted-foreground hover:bg-accent/40"
                                data-testid={`workspace-candidate-${candidate.area}-${candidate.resourceKey}`}
                            >
                                <span className="min-w-0 truncate">
                                    {candidate.displayLabel}{" "}
                                    <span className="text-xs">
                                        ({candidate.resourceKey})
                                    </span>
                                </span>
                                <button
                                    onClick={() => onAddNode(candidate)}
                                    className="flex shrink-0 items-center gap-1 text-xs text-primary hover:opacity-80"
                                    data-testid={`workspace-candidate-add-${candidate.area}-${candidate.resourceKey}`}
                                >
                                    <Plus className="h-3 w-3" /> Add
                                </button>
                            </li>
                        ))}
                    </ul>
                </div>
            ))}

            <div className="border-t pt-3">
                <div className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                    Custom resource
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <select
                        value={manualArea}
                        onChange={(e) =>
                            setManualArea(e.target.value as WorkspaceResourceArea)
                        }
                        className="rounded-md border bg-card px-2 py-1.5 text-sm"
                        data-testid="workspace-manual-area"
                        aria-label="Resource area"
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
                            onAddNode({
                                area: manualArea,
                                resourceKey: manualKey.trim(),
                                displayLabel:
                                    manualLabel.trim() || manualKey.trim(),
                            });
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
    );
}
