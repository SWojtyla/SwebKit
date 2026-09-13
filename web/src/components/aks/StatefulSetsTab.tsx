import { useCallback, useMemo, useState, type MouseEvent } from "react";
import { useAksStatefulSets, useAksRestartStatefulSet, useAksScaleStatefulSet } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import { ScaleDialog } from "./ScaleDialog";
import type { ContextMenuItem } from "./ContextMenu";
import type { StatefulSetInfo } from "@/lib/types";

interface StatefulSetsTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<StatefulSetInfo>[] = [
  { header: "Ready", cell: (sts) => (
    <span className={sts.readyReplicas === sts.replicas ? "text-success" : "text-warning"}>
      {sts.readyReplicas}/{sts.replicas}
    </span>
  ), sortValue: (sts) => (sts.readyReplicas === sts.replicas ? 1 : 0) },
  { header: "Current Rev", cell: (sts) => <span className="text-xs text-muted-foreground">{sts.currentRevision ?? "—"}</span> },
  { header: "Update Rev", cell: (sts) => <span className="text-xs text-muted-foreground">{sts.updateRevision ?? "—"}</span> },
];

export function StatefulSetsTab({ ns, isMulti }: StatefulSetsTabProps) {
  const { data: statefulsets, isLoading, error } = useAksStatefulSets(ns);
  const ws = useAksWorkspace();
  const restartSts = useAksRestartStatefulSet();
  const scaleSts = useAksScaleStatefulSet();

  const [scaleTarget, setScaleTarget] = useState<StatefulSetInfo | null>(null);

  const confirmScale = useCallback((sts: StatefulSetInfo, replicas: number) => {
    setScaleTarget(null);
    ws.requestConfirm({
      message: `Scale stateful set "${sts.name}" to ${replicas} replicas?`,
      resourceName: sts.name,
      onConfirm: () => scaleSts.mutate({ ns: sts.namespace, name: sts.name, replicas }),
    });
  }, [ws, scaleSts.mutate]);

  const allColumns: Column<StatefulSetInfo>[] = useMemo(() => [
    ...columns,
    {
      header: "Actions",
      className: "py-2 pr-4 w-px whitespace-nowrap",
      cell: (sts) => (
        <button
          onClick={(e) => {
            e.stopPropagation();
            setScaleTarget(sts);
          }}
          className="rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid={`statefulset-scale-${sts.name}`}
        >
          Scale
        </button>
      ),
    },
  ], []);

  const buildMenu = useCallback((sts: StatefulSetInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(sts.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("statefulset", sts.name, sts.namespace) },
    { label: "View Logs", icon: "☰", onClick: async () => {
      const pods = await ws.resolvePodsForSelector(sts.namespace, sts.selectorLabels);
      if (pods.length > 0) ws.openLogs(pods[0]);
    } },
    { label: "Container Details", icon: "⚙", onClick: async () => {
      const pods = await ws.resolvePodsForSelector(sts.namespace, sts.selectorLabels);
      if (pods.length > 0) ws.openContainerDetails(pods[0].name, pods[0].namespace);
    } },
    { label: "Analyze network", icon: "📶", onClick: () => ws.navigateToAnalysis() },
    { label: "", separator: true, onClick: () => {} },
    { label: "Restart", icon: "↻", onClick: () => {
      ws.requestConfirm({
        message: `Restart stateful set "${sts.name}"?`,
        resourceName: sts.name,
        onConfirm: () => restartSts.mutate({ ns: sts.namespace, name: sts.name }),
      });
    }},
    // Was a native `prompt()`, which in the Tauri webview is an unstyled OS-level
    // modal with no validation and no idea which namespace it is acting on.
    { label: "Scale...", icon: "⇳", onClick: () => setScaleTarget(sts) },
  ], [ws, restartSts.mutate]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, sts: StatefulSetInfo) => ws.showContextMenu(e, buildMenu(sts)),
    [ws, buildMenu],
  );

  return (
    <>
      <ResourceTable
        data={statefulsets}
        isLoading={isLoading}
        error={error}
        isMulti={isMulti}
        testIdPrefix="statefulset"
        tableBodyTestId="statefulsets-table-body"
        emptyMessage="No stateful sets found"
        onRowClick={(sts) => ws.openYaml("statefulset", sts.name, sts.namespace)}
        onRowContextMenu={handleRowContextMenu}
        columns={allColumns}
        defaultSort={{ sortValue: (sts) => (sts.readyReplicas === sts.replicas ? 1 : 0), direction: "asc" }}
      />

      {scaleTarget && (
        <ScaleDialog
          kind="StatefulSet"
          name={scaleTarget.name}
          namespace={scaleTarget.namespace}
          currentReplicas={scaleTarget.replicas}
          isSaving={scaleSts.isPending}
          onCancel={() => setScaleTarget(null)}
          onConfirm={(replicas) => confirmScale(scaleTarget, replicas)}
        />
      )}
    </>
  );
}
