import { useCallback, useMemo, useState, type MouseEvent } from "react";
import {
    useAksHpas,
    useAksScaleHpa,
    useAksDeleteHpa,
    useAksSetHpaScalingEnabled,
} from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import { Dialog } from "@/components/shared/Dialog";
import { MoreHorizontal, X } from "lucide-react";
import type { ContextMenuItem } from "./ContextMenu";
import type { HpaInfo } from "@/lib/types";

export function HpaTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
    const ws = useAksWorkspace();
    const { data: hpas, isLoading, error } = useAksHpas(ns);
    const scaleMutation = useAksScaleHpa();
    const deleteMutation = useAksDeleteHpa();
    const toggleMutation = useAksSetHpaScalingEnabled();

    const [scaleTarget, setScaleTarget] = useState<HpaInfo | null>(null);

    const handleScale = useCallback(
        (min: number, max: number) => {
            if (!scaleTarget) return;
            const hpa = scaleTarget;
            setScaleTarget(null);
            // Route through the same confirm step Delete/Toggle-scaling already use on this same tab —
            // scaling min/max replicas is at least as consequential as either, and Deployments/
            // StatefulSets' own Scale flows already confirm this way.
            ws.requestConfirm({
                message: `Scale HPA "${hpa.name}" to min ${min} / max ${max} replicas?`,
                resourceName: hpa.name,
                onConfirm: () =>
                    scaleMutation.mutate({
                        ns: hpa.namespace,
                        name: hpa.name,
                        minReplicas: min,
                        maxReplicas: max,
                    }),
            });
        },
        [ws, scaleTarget, scaleMutation],
    );

    const handleDelete = useCallback(
        (hpa: HpaInfo) => {
            // A KEDA-managed HPA cannot outlive its ScaledObject — the backend deletes the ScaledObject
            // itself for these, and the confirm has to say so because the ScaledObject, not the generated
            // HPA, is the resource the user actually owns.
            ws.requestConfirm({
                message: hpa.isKedaManaged
                    ? `"${hpa.name}" is managed by KEDA ScaledObject "${hpa.scaledObjectName ?? "?"}". Delete the ScaledObject to remove autoscaling?`
                    : `Delete HPA ${hpa.name} in ${hpa.namespace}?`,
                resourceName: hpa.name,
                onConfirm: () =>
                    deleteMutation.mutate({
                        ns: hpa.namespace,
                        name: hpa.name,
                    }),
            });
        },
        [ws, deleteMutation],
    );

    const handleToggleScaling = useCallback(
        (hpa: HpaInfo) => {
            const next = !hpa.isScalingDisabled;
            const action = next ? "disable" : "enable";
            ws.requestConfirm({
                message: `${action === "disable" ? "Disable" : "Enable"} scaling for ${hpa.name}?`,
                resourceName: hpa.name,
                onConfirm: () =>
                    toggleMutation.mutate({
                        ns: hpa.namespace,
                        name: hpa.name,
                        enabled: !next,
                    }),
            });
        },
        [ws, toggleMutation],
    );

    const buildMenu = useCallback(
        (hpa: HpaInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(hpa.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml(
                        "horizontalpodautoscaler",
                        hpa.name,
                        hpa.namespace,
                    ),
            },
            { label: "", separator: true, onClick: () => {} },
            { label: "Scale…", icon: "⇳", onClick: () => setScaleTarget(hpa) },
            {
                label: hpa.isScalingDisabled
                    ? "Enable autoscaling"
                    : "Disable autoscaling",
                icon: "⏸",
                onClick: () => handleToggleScaling(hpa),
            },
            { label: "", separator: true, onClick: () => {} },
            {
                label: "Delete",
                icon: "✕",
                onClick: () => handleDelete(hpa),
                destructive: true,
            },
        ],
        [ws, handleToggleScaling, handleDelete],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, hpa: HpaInfo) =>
            ws.showContextMenu(e, buildMenu(hpa)),
        [ws, buildMenu],
    );

    const columns: Column<HpaInfo>[] = useMemo(
        () => [
            {
                // The only flexible column. The table is w-full and every other column is
                // content-sized, so without one slack-absorbing column the extra width is
                // smeared across all of them and the row reads as disconnected fragments.
                header: "Target",
                className: "w-full py-2 pr-4",
                cell: (hpa) => (
                    <span className="text-xs text-muted-foreground">
                        {hpa.targetKind}/{hpa.targetName}
                    </span>
                ),
            },
            {
                header: "Bounds",
                cell: (hpa) => (
                    <span title="min – max replicas">
                        {hpa.minReplicas}–{hpa.maxReplicas}
                    </span>
                ),
                sortValue: (hpa) => hpa.minReplicas,
            },
            {
                header: "Replicas",
                cell: (hpa) => (
                    <span title="current → desired">
                        {hpa.currentReplicas}
                        <span className="text-muted-foreground"> → </span>
                        <span className="text-success">
                            {hpa.desiredReplicas}
                        </span>
                    </span>
                ),
                sortValue: (hpa) => hpa.currentReplicas,
            },
            {
                header: "CPU",
                cell: (hpa) =>
                    hpa.currentCpuUtilizationPercent != null ? (
                        <span
                            title="current / target utilization"
                            className={
                                hpa.targetCpuUtilizationPercent != null &&
                                hpa.currentCpuUtilizationPercent >
                                    hpa.targetCpuUtilizationPercent
                                    ? "text-warning"
                                    : "text-muted-foreground"
                            }
                        >
                            {hpa.currentCpuUtilizationPercent}%
                            {hpa.targetCpuUtilizationPercent != null && (
                                <span className="text-muted-foreground">
                                    {" "}
                                    / {hpa.targetCpuUtilizationPercent}%
                                </span>
                            )}
                        </span>
                    ) : (
                        "—"
                    ),
                sortValue: (hpa) => hpa.currentCpuUtilizationPercent ?? -1,
            },
            {
                header: "Type",
                cell: (hpa) => (
                    <div className="flex flex-wrap gap-1">
                        {hpa.isKedaManaged ? (
                            <span className="rounded bg-purple-500/20 px-1.5 py-0.5 text-xs text-purple-500">
                                KEDA
                            </span>
                        ) : (
                            <span className="rounded px-1.5 py-0.5 text-xs text-muted-foreground">
                                HPA
                            </span>
                        )}
                        {hpa.isScalingDisabled && (
                            <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                                Disabled
                            </span>
                        )}
                    </div>
                ),
            },
            {
                header: "Actions",
                className: "py-2 pr-4 w-px whitespace-nowrap",
                cell: (hpa) => (
                    <div
                        className="flex items-center gap-1"
                        onClick={(e) => e.stopPropagation()}
                    >
                        <button
                            onClick={() => setScaleTarget(hpa)}
                            className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                            data-testid={`hpa-scale-${hpa.name}`}
                        >
                            Scale
                        </button>
                        <button
                            onClick={(e) =>
                                ws.showContextMenu(e, buildMenu(hpa))
                            }
                            className="rounded border border-border px-1.5 py-1 text-xs text-muted-foreground hover:bg-accent/50"
                            aria-label={`More actions for ${hpa.name}`}
                            title="More actions"
                            data-testid={`hpa-actions-${hpa.name}`}
                        >
                            <MoreHorizontal className="h-3.5 w-3.5" />
                        </button>
                    </div>
                ),
            },
        ],
        [ws, buildMenu],
    );

    return (
        <div className="p-4">
            <ResourceTable
                data={hpas}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                testIdPrefix="hpa"
                tableBodyTestId="hpas-table-body"
                emptyMessage="No HPAs found"
                onRowClick={(hpa) =>
                    ws.openYaml(
                        "horizontalpodautoscaler",
                        hpa.name,
                        hpa.namespace,
                    )
                }
                onRowContextMenu={handleRowContextMenu}
                columns={columns}
                defaultSort={{
                    sortValue: (hpa) =>
                        hpa.isScalingDisabled
                            ? -1
                            : hpa.currentReplicas === hpa.desiredReplicas
                              ? 1
                              : 0,
                    direction: "asc",
                }}
            />

            {scaleTarget && (
                <ScaleHpaForm
                    hpa={scaleTarget}
                    onCancel={() => setScaleTarget(null)}
                    onSave={handleScale}
                    isSaving={scaleMutation.isPending}
                />
            )}
        </div>
    );
}

/**
 * A modal, not the inline panel this used to render under the table: the panel
 * appeared and disappeared below the rows, shifting everything under it and
 * scrolling the HPA you were acting on out of view.
 */
function ScaleHpaForm({
    hpa,
    onCancel,
    onSave,
    isSaving,
}: {
    hpa: HpaInfo;
    onCancel: () => void;
    onSave: (min: number, max: number) => void;
    isSaving: boolean;
}) {
    const [min, setMin] = useState(hpa.minReplicas);
    const [max, setMax] = useState(hpa.maxReplicas);
    const invalid = min > max;

    return (
        <Dialog
            onClose={onCancel}
            label={`Scale HPA ${hpa.name}`}
            testId="aks-hpa-scale-dialog"
            widthClassName="w-[420px]"
        >
            <div className="flex items-start justify-between gap-3 border-b px-4 py-3">
                <div className="min-w-0">
                    <h2 className="text-sm font-semibold">Scale autoscaler</h2>
                    <p
                        className="truncate text-xs text-muted-foreground"
                        title={`${hpa.namespace}/${hpa.name}`}
                    >
                        {hpa.namespace} /{" "}
                        <span className="font-medium text-foreground">
                            {hpa.name}
                        </span>
                    </p>
                </div>
                <button
                    onClick={onCancel}
                    className="shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Close"
                    data-testid="aks-hpa-scale-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="space-y-3 p-4">
                <div className="flex items-end gap-3">
                    <label className="text-xs">
                        Min replicas
                        <input
                            type="number"
                            min={1}
                            value={min}
                            onChange={(e) =>
                                setMin(
                                    Math.max(
                                        1,
                                        parseInt(e.target.value, 10) || 0,
                                    ),
                                )
                            }
                            onKeyDown={(e) => {
                                if (e.key === "Enter" && !invalid && !isSaving)
                                    onSave(min, max);
                            }}
                            autoFocus
                            className="mt-1 block w-24 rounded-md border bg-background px-2 py-1.5 text-sm tabular-nums"
                            data-testid="aks-hpa-scale-min"
                        />
                    </label>
                    <label className="text-xs">
                        Max replicas
                        <input
                            type="number"
                            min={1}
                            value={max}
                            onChange={(e) =>
                                setMax(
                                    Math.max(
                                        1,
                                        parseInt(e.target.value, 10) || 0,
                                    ),
                                )
                            }
                            onKeyDown={(e) => {
                                if (e.key === "Enter" && !invalid && !isSaving)
                                    onSave(min, max);
                            }}
                            className="mt-1 block w-24 rounded-md border bg-background px-2 py-1.5 text-sm tabular-nums"
                            data-testid="aks-hpa-scale-max"
                        />
                    </label>
                    <span className="pb-1.5 text-xs text-muted-foreground">
                        currently {hpa.minReplicas}–{hpa.maxReplicas}
                    </span>
                </div>
                {invalid && (
                    <p
                        className="text-xs text-destructive"
                        data-testid="aks-hpa-scale-error"
                    >
                        Min replicas cannot exceed max replicas.
                    </p>
                )}
            </div>

            <div className="flex justify-end gap-2 border-t px-4 py-3">
                <button
                    onClick={onCancel}
                    disabled={isSaving}
                    title={isSaving ? "Saving…" : undefined}
                    className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
                    data-testid="aks-hpa-scale-cancel"
                >
                    Cancel
                </button>
                <button
                    onClick={() => onSave(min, max)}
                    disabled={isSaving || invalid}
                    title={
                        isSaving
                            ? "Scaling…"
                            : invalid
                              ? "Min must be ≤ max and both ≥ 0"
                              : undefined
                    }
                    className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                    data-testid="aks-hpa-scale-save"
                >
                    {isSaving ? "Scaling…" : "Save"}
                </button>
            </div>
        </Dialog>
    );
}
