import { useCallback, type MouseEvent } from "react";
import { useAksStatefulSets, useAksRestartStatefulSet, useAksScaleStatefulSet } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { StatefulSetInfo } from "@/lib/types";

interface StatefulSetsTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<StatefulSetInfo>[] = [
  { header: "Ready", cell: (sts) => (
    <span className={sts.readyReplicas === sts.replicas ? "text-green-500" : "text-yellow-500"}>
      {sts.readyReplicas}/{sts.replicas}
    </span>
  )},
  { header: "Current Rev", cell: (sts) => <span className="text-xs text-muted-foreground">{sts.currentRevision ?? "—"}</span> },
  { header: "Update Rev", cell: (sts) => <span className="text-xs text-muted-foreground">{sts.updateRevision ?? "—"}</span> },
];

export function StatefulSetsTab({ ns, isMulti }: StatefulSetsTabProps) {
  const { data: statefulsets, isLoading } = useAksStatefulSets(ns);
  const ws = useAksWorkspace();
  const restartSts = useAksRestartStatefulSet();
  const scaleSts = useAksScaleStatefulSet();

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
    { label: "Scale...", icon: "⇳", onClick: () => {
      const replicas = prompt(`Scale stateful set "${sts.name}" to how many replicas?`, String(sts.replicas));
      if (replicas === null) return;
      const n = parseInt(replicas, 10);
      if (isNaN(n) || n < 0) return;
      ws.requestConfirm({
        message: `Scale stateful set "${sts.name}" to ${n} replicas?`,
        resourceName: sts.name,
        onConfirm: () => scaleSts.mutate({ ns: sts.namespace, name: sts.name, replicas: n }),
      });
    }},
  ], [ws, restartSts.mutate, scaleSts.mutate]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, sts: StatefulSetInfo) => ws.showContextMenu(e, buildMenu(sts)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={statefulsets}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="statefulset"
      tableBodyTestId="statefulsets-table-body"
      emptyMessage="No stateful sets found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
