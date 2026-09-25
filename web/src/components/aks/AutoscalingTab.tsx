import { useCallback, useMemo, useState, type MouseEvent } from "react";
import {
    useAksHpas,
    useAksScaleHpa,
    useAksDeleteHpa,
    useAksSetHpaScalingEnabled,
    useAksScaledJobs,
    useAksScaleScaledJob,
    useAksDeleteScaledJob,
    useAksSetScaledJobScalingEnabled,
} from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/AksWorkspaceContext";
import { Dialog } from "@/components/shared/Dialog";
import { MoreHorizontal, X } from "lucide-react";
import type { ContextMenuItem } from "./ContextMenu";
import type { HpaInfo, ScaledJobInfo } from "@/lib/types";

/**
 * The "Autoscaling" tab: everything that scales workloads on the cluster in one
 * place — plain HPAs, KEDA ScaledObjects (which surface as KEDA-managed HPAs),
 * and KEDA ScaledJobs (which scale Jobs and therefore never appear as HPAs).
 */
export function AutoscalingTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
    const { data: hpas, isLoading, error } = useAksHpas(ns);
    const scaledJobs = useAksScaledJobs(ns);

    return (
        <div className="space-y-6 p-4">
            <section>
                <h2 className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                    Horizontal Pod Autoscalers
                </h2>
                <HpaTable
                    hpas={hpas}
                    isLoading={isLoading}
                    error={error}
                    isMulti={isMulti}
                />
            </section>
            <section>
                <h2 className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                    KEDA ScaledJobs
                </h2>
                <ScaledJobsTable
                    scaledJobs={scaledJobs.data}
                    isLoading={scaledJobs.isLoading}
                    error={scaledJobs.error}
                    isMulti={isMulti}
                />
            </section>
        </div>
    );
}

function HpaTable({
    hpas,
    isLoading,
    error,
    isMulti,
}: {
    hpas: HpaInfo[] | undefined;
    isLoading: boolean;
    error: unknown;
    isMulti?: boolean;
}) {
    const ws = useAksActions();
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
                header: "Target",
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
                            <span
                                className="rounded bg-purple-500/20 px-1.5 py-0.5 text-xs text-purple-500"
                                title={`KEDA ScaledObject${hpa.scaledObjectName ? `: ${hpa.scaledObjectName}` : ""}`}
                            >
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
        <>
            <ResourceTable
                data={hpas}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                compact
                testIdPrefix="hpa"
                tableBodyTestId="hpas-table-body"
                emptyMessage="No autoscalers found — no HPAs or KEDA ScaledObjects in this scope"
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
                <ScaleDialog
                    title={`Scale HPA ${scaleTarget.name}`}
                    name={scaleTarget.name}
                    namespace={scaleTarget.namespace}
                    min={scaleTarget.minReplicas}
                    max={scaleTarget.maxReplicas}
                    minAllowed={1}
                    onCancel={() => setScaleTarget(null)}
                    onSave={handleScale}
                    isSaving={scaleMutation.isPending}
                    testIdPrefix="aks-hpa-scale"
                />
            )}
        </>
    );
}

function ScaledJobsTable({
    scaledJobs,
    isLoading,
    error,
    isMulti,
}: {
    scaledJobs: ScaledJobInfo[] | undefined;
    isLoading: boolean;
    error: unknown;
    isMulti?: boolean;
}) {
    const ws = useAksActions();
    const scaleMutation = useAksScaleScaledJob();
    const deleteMutation = useAksDeleteScaledJob();
    const toggleMutation = useAksSetScaledJobScalingEnabled();

    const [scaleTarget, setScaleTarget] = useState<ScaledJobInfo | null>(null);

    const handleScale = useCallback(
        (min: number, max: number) => {
            if (!scaleTarget) return;
            const job = scaleTarget;
            setScaleTarget(null);
            ws.requestConfirm({
                message: `Scale ScaledJob "${job.name}" to min ${min} / max ${max} replicas?`,
                resourceName: job.name,
                onConfirm: () =>
                    scaleMutation.mutate({
                        ns: job.namespace,
                        name: job.name,
                        minReplicas: min,
                        maxReplicas: max,
                    }),
            });
        },
        [ws, scaleTarget, scaleMutation],
    );

    const handleDelete = useCallback(
        (job: ScaledJobInfo) => {
            ws.requestConfirm({
                message: `Delete ScaledJob ${job.name} in ${job.namespace}?`,
                resourceName: job.name,
                onConfirm: () =>
                    deleteMutation.mutate({
                        ns: job.namespace,
                        name: job.name,
                    }),
            });
        },
        [ws, deleteMutation],
    );

    const handleToggleScaling = useCallback(
        (job: ScaledJobInfo) => {
            const enable = job.isPaused;
            ws.requestConfirm({
                message: `${enable ? "Resume" : "Pause"} KEDA scaling for ${job.name}?`,
                resourceName: job.name,
                onConfirm: () =>
                    toggleMutation.mutate({
                        ns: job.namespace,
                        name: job.name,
                        enabled: enable,
                    }),
            });
        },
        [ws, toggleMutation],
    );

    const buildMenu = useCallback(
        (job: ScaledJobInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(job.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml("scaledjob", job.name, job.namespace),
            },
            { label: "", separator: true, onClick: () => {} },
            { label: "Scale…", icon: "⇳", onClick: () => setScaleTarget(job) },
            {
                label: job.isPaused ? "Resume scaling" : "Pause scaling",
                icon: "⏸",
                onClick: () => handleToggleScaling(job),
            },
            { label: "", separator: true, onClick: () => {} },
            {
                label: "Delete",
                icon: "✕",
                onClick: () => handleDelete(job),
                destructive: true,
            },
        ],
        [ws, handleToggleScaling, handleDelete],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, job: ScaledJobInfo) =>
            ws.showContextMenu(e, buildMenu(job)),
        [ws, buildMenu],
    );

    const columns: Column<ScaledJobInfo>[] = useMemo(
        () => [
            {
                header: "Triggers",
                cell: (job) =>
                    job.triggers.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                            {job.triggers.map((t) => (
                                <span
                                    key={t}
                                    className="rounded bg-accent px-1.5 py-0.5 font-mono text-xs"
                                >
                                    {t}
                                </span>
                            ))}
                        </div>
                    ) : (
                        <span className="text-muted-foreground">—</span>
                    ),
            },
            {
                header: "Bounds",
                cell: (job) => (
                    <span title="min – max replicas (0 = no explicit bound)">
                        {job.minReplicas}–{job.maxReplicas}
                    </span>
                ),
                sortValue: (job) => job.minReplicas,
            },
            {
                header: "State",
                cell: (job) =>
                    job.isPaused ? (
                        <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                            Paused
                        </span>
                    ) : (
                        <span className="text-xs text-success">Active</span>
                    ),
                sortValue: (job) => (job.isPaused ? 0 : 1),
            },
            {
                header: "Actions",
                className: "py-2 pr-4 w-px whitespace-nowrap",
                cell: (job) => (
                    <div
                        className="flex items-center gap-1"
                        onClick={(e) => e.stopPropagation()}
                    >
                        <button
                            onClick={() => setScaleTarget(job)}
                            className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                            data-testid={`scaledjob-scale-${job.name}`}
                        >
                            Scale
                        </button>
                        <button
                            onClick={(e) =>
                                ws.showContextMenu(e, buildMenu(job))
                            }
                            className="rounded border border-border px-1.5 py-1 text-xs text-muted-foreground hover:bg-accent/50"
                            aria-label={`More actions for ${job.name}`}
                            title="More actions"
                            data-testid={`scaledjob-actions-${job.name}`}
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
        <>
            <ResourceTable
                data={scaledJobs}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                compact
                testIdPrefix="scaledjob"
                tableBodyTestId="scaledjobs-table-body"
                emptyMessage="No KEDA ScaledJobs found"
                onRowClick={(job) =>
                    ws.openYaml("scaledjob", job.name, job.namespace)
                }
                onRowContextMenu={handleRowContextMenu}
                columns={columns}
            />

            {scaleTarget && (
                <ScaleDialog
                    title={`Scale ScaledJob ${scaleTarget.name}`}
                    name={scaleTarget.name}
                    namespace={scaleTarget.namespace}
                    min={scaleTarget.minReplicas}
                    max={scaleTarget.maxReplicas}
                    minAllowed={0}
                    onCancel={() => setScaleTarget(null)}
                    onSave={handleScale}
                    isSaving={scaleMutation.isPending}
                    testIdPrefix="aks-scaledjob-scale"
                />
            )}
        </>
    );
}

/**
 * Shared min/max replicas dialog for both resource kinds on this tab. A modal,
 * not an inline panel under the table: the panel used to appear and disappear
 * below the rows, shifting everything under it and scrolling the resource being
 * acted on out of view.
 */
function ScaleDialog({
    title,
    name,
    namespace,
    min: initialMin,
    max: initialMax,
    minAllowed,
    onCancel,
    onSave,
    isSaving,
    testIdPrefix,
}: {
    title: string;
    name: string;
    namespace: string;
    min: number;
    max: number;
    minAllowed: number;
    onCancel: () => void;
    onSave: (min: number, max: number) => void;
    isSaving: boolean;
    testIdPrefix: string;
}) {
    const [min, setMin] = useState(initialMin);
    const [max, setMax] = useState(initialMax);
    const invalid = min > max;

    return (
        <Dialog
            onClose={onCancel}
            label={title}
            testId={`${testIdPrefix}-dialog`}
            widthClassName="w-[420px]"
        >
            <div className="flex items-start justify-between gap-3 border-b px-4 py-3">
                <div className="min-w-0">
                    <h2 className="text-sm font-semibold">Scale autoscaler</h2>
                    <p
                        className="truncate text-xs text-muted-foreground"
                        title={`${namespace}/${name}`}
                    >
                        {namespace} /{" "}
                        <span className="font-medium text-foreground">
                            {name}
                        </span>
                    </p>
                </div>
                <button
                    onClick={onCancel}
                    className="shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Close"
                    data-testid={`${testIdPrefix}-close`}
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
                            min={minAllowed}
                            value={min}
                            onChange={(e) =>
                                setMin(
                                    Math.max(
                                        minAllowed,
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
                            data-testid={`${testIdPrefix}-min`}
                        />
                    </label>
                    <label className="text-xs">
                        Max replicas
                        <input
                            type="number"
                            min={minAllowed}
                            value={max}
                            onChange={(e) =>
                                setMax(
                                    Math.max(
                                        minAllowed,
                                        parseInt(e.target.value, 10) || 0,
                                    ),
                                )
                            }
                            onKeyDown={(e) => {
                                if (e.key === "Enter" && !invalid && !isSaving)
                                    onSave(min, max);
                            }}
                            className="mt-1 block w-24 rounded-md border bg-background px-2 py-1.5 text-sm tabular-nums"
                            data-testid={`${testIdPrefix}-max`}
                        />
                    </label>
                    <span className="pb-1.5 text-xs text-muted-foreground">
                        currently {initialMin}–{initialMax}
                    </span>
                </div>
                {invalid && (
                    <p
                        className="text-xs text-destructive"
                        data-testid={`${testIdPrefix}-error`}
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
                    data-testid={`${testIdPrefix}-cancel`}
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
                              ? `Min must be ≤ max and both ≥ ${minAllowed}`
                              : undefined
                    }
                    className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                    data-testid={`${testIdPrefix}-save`}
                >
                    {isSaving ? "Scaling…" : "Save"}
                </button>
            </div>
        </Dialog>
    );
}
