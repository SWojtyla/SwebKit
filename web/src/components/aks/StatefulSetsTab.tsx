import { useCallback, useMemo, useState, type MouseEvent } from "react";
import {
    useAksStatefulSets,
    useAksRestartStatefulSet,
    useAksScaleStatefulSet,
} from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import { ScaleDialog } from "./ScaleDialog";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, StatefulSetInfo } from "@/lib/types";

interface StatefulSetsTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

const columns: Column<StatefulSetInfo>[] = [
    {
        header: "Ready",
        cell: (sts) => (
            <span
                className={
                    sts.readyReplicas === sts.replicas
                        ? "text-state-done-ink"
                        : "text-state-needs-ink"
                }
            >
                {sts.readyReplicas}/{sts.replicas}
            </span>
        ),
        sortValue: (sts) => (sts.readyReplicas === sts.replicas ? 1 : 0),
    },
    {
        header: "Current Rev",
        cell: (sts) => (
            <span className="text-xs text-fg-muted">
                {sts.currentRevision ?? "—"}
            </span>
        ),
    },
    {
        header: "Update Rev",
        cell: (sts) => (
            <span className="text-xs text-fg-muted">
                {sts.updateRevision ?? "—"}
            </span>
        ),
    },
];

export function StatefulSetsTab({
    targets,
    isMulti,
    showContext,
}: StatefulSetsTabProps) {
    const {
        data: statefulsets,
        isLoading,
        error,
        contextErrors,
    } = useAksStatefulSets(targets);
    const ws = useAksActions();
    const restartSts = useAksRestartStatefulSet();
    const scaleSts = useAksScaleStatefulSet();

    const [scaleTarget, setScaleTarget] = useState<StatefulSetInfo | null>(
        null,
    );

    const confirmScale = useCallback(
        (sts: StatefulSetInfo, replicas: number) => {
            setScaleTarget(null);
            ws.requestConfirm({
                message: `Scale stateful set "${sts.name}" to ${replicas} replicas${sts.context ? ` in ${sts.context}` : ""}?`,
                resourceName: sts.name,
                onConfirm: () =>
                    scaleSts.mutate({
                        ns: sts.namespace,
                        name: sts.name,
                        replicas,
                        context: sts.context,
                    }),
            });
        },
        [ws, scaleSts],
    );

    const allColumns: Column<StatefulSetInfo>[] = useMemo(
        () => [
            ...columns,
            {
                header: "Actions",
                sortable: false,
                className: "w-px whitespace-nowrap py-1 pr-4",
                cell: (sts) => (
                    <button
                        onClick={(e) => {
                            e.stopPropagation();
                            setScaleTarget(sts);
                        }}
                        className="h-7 rounded-md border bg-surface-1 px-2 text-xs text-fg-muted opacity-0 transition-opacity hover:bg-surface-2 hover:text-fg group-hover:opacity-100 focus:opacity-100"
                        data-testid={`statefulset-scale-${sts.name}`}
                    >
                        Scale
                    </button>
                ),
            },
        ],
        [],
    );

    const buildMenu = useCallback(
        (sts: StatefulSetInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(sts.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml(
                        "statefulset",
                        sts.name,
                        sts.namespace,
                        sts.context,
                    ),
            },
            {
                label: "View Logs",
                icon: "☰",
                onClick: async () => {
                    const pods = await ws.resolvePodsForSelector(
                        sts.namespace,
                        sts.selectorLabels,
                        sts.context,
                    );
                    if (pods.length > 0) ws.openLogs(pods[0]);
                },
            },
            {
                label: "Container Details",
                icon: "⚙",
                onClick: async () => {
                    const pods = await ws.resolvePodsForSelector(
                        sts.namespace,
                        sts.selectorLabels,
                        sts.context,
                    );
                    if (pods.length > 0)
                        ws.openContainerDetails(
                            pods[0].name,
                            pods[0].namespace,
                            pods[0].context,
                        );
                },
            },
            {
                label: "Analyze network",
                icon: "📶",
                onClick: () => ws.navigateToAnalysis(),
            },
            { label: "", separator: true, onClick: () => {} },
            {
                label: "Restart",
                icon: "↻",
                onClick: () => {
                    ws.requestConfirm({
                        message: `Restart stateful set "${sts.name}"${sts.context ? ` in ${sts.context}` : ""}?`,
                        resourceName: sts.name,
                        onConfirm: () =>
                            restartSts.mutate({
                                ns: sts.namespace,
                                name: sts.name,
                                context: sts.context,
                            }),
                    });
                },
            },
            // Was a native `prompt()`, which in the Tauri webview is an unstyled OS-level
            // modal with no validation and no idea which namespace it is acting on.
            {
                label: "Scale...",
                icon: "⇳",
                onClick: () => setScaleTarget(sts),
            },
        ],
        [ws, restartSts],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, sts: StatefulSetInfo) =>
            ws.showContextMenu(e, buildMenu(sts)),
        [ws, buildMenu],
    );

    return (
        <>
            <ResourceTable
                data={statefulsets}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                showContext={showContext}
                contextErrors={contextErrors}
                testIdPrefix="statefulset"
                tableBodyTestId="statefulsets-table-body"
                emptyMessage="No stateful sets found"
                onRowClick={(sts) =>
                    ws.openYaml(
                        "statefulset",
                        sts.name,
                        sts.namespace,
                        sts.context,
                    )
                }
                onRowContextMenu={handleRowContextMenu}
                columns={allColumns}
                defaultSort={{
                    sortValue: (sts) =>
                        sts.readyReplicas === sts.replicas ? 1 : 0,
                    direction: "asc",
                }}
            />

            {scaleTarget && (
                <ScaleDialog
                    kind="StatefulSet"
                    name={scaleTarget.name}
                    namespace={scaleTarget.namespace}
                    currentReplicas={scaleTarget.replicas}
                    isSaving={scaleSts.isPending}
                    onCancel={() => setScaleTarget(null)}
                    onConfirm={(replicas) =>
                        confirmScale(scaleTarget, replicas)
                    }
                />
            )}
        </>
    );
}
