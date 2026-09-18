import { useCallback, useMemo, useState, type MouseEvent } from "react";
import { useAksDeployments, useAksRestartDeployment, useAksScaleDeployment } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import { ScaleDialog } from "./ScaleDialog";
import type { ContextMenuItem } from "./ContextMenu";
import type { DeploymentInfo } from "@/lib/types";

interface DeploymentsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function DeploymentsTab({ ns, isMulti }: DeploymentsTabProps) {
  const { data: deployments, isLoading, error } = useAksDeployments(ns);
  const ws = useAksWorkspace();
  const restartMutation = useAksRestartDeployment();
  const scaleMutation = useAksScaleDeployment();

  // The whole deployment, not just its name: the dialog needs the namespace and
  // the current replica count, and re-deriving them from `deployments` would
  // reopen at the wrong value the moment an auto-refresh lands mid-edit.
  const [scaleTarget, setScaleTarget] = useState<DeploymentInfo | null>(null);

  const confirmScale = useCallback((dep: DeploymentInfo, replicas: number) => {
    setScaleTarget(null);
    ws.requestConfirm({
      message: `Scale deployment "${dep.name}" to ${replicas} replicas?`,
      resourceName: dep.name,
      onConfirm: () => scaleMutation.mutate({ ns: dep.namespace, name: dep.name, replicas }),
    });
  }, [ws, scaleMutation]);

  const restart = useCallback((dep: DeploymentInfo) => {
    ws.requestConfirm({
      message: `Restart deployment "${dep.name}"?`,
      resourceName: dep.name,
      onConfirm: () => restartMutation.mutate({ ns: dep.namespace, name: dep.name }),
    });
  }, [ws, restartMutation]);

  const buildMenu = useCallback((dep: DeploymentInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(dep.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("deployment", dep.name, dep.namespace) },
    { label: "Edit YAML", icon: "✎", onClick: () => ws.openYaml("deployment", dep.name, dep.namespace) },
    { label: "View Logs", icon: "☰", onClick: async () => {
      const pods = await ws.resolvePodsForSelector(dep.namespace, dep.selectorLabels);
      if (pods.length > 0) ws.openLogs(pods[0]);
    } },
    { label: "Logs for all pods", icon: "¦", onClick: async () => {
      const pods = await ws.resolvePodsForSelector(dep.namespace, dep.selectorLabels);
      ws.openMultiPodLogs(pods);
    } },
    { label: "Container Details", icon: "⚙", onClick: async () => {
      const pods = await ws.resolvePodsForSelector(dep.namespace, dep.selectorLabels);
      if (pods.length > 0) ws.openContainerDetails(pods[0].name, pods[0].namespace);
    } },
    { label: "Analyze network", icon: "📶", onClick: () => ws.navigateToAnalysis() },
    { label: "", separator: true, onClick: () => {} },
    { label: "Restart Deployment", icon: "↻", onClick: () => restart(dep) },
    { label: "Scale...", icon: "⇳", onClick: () => setScaleTarget(dep) },
  ], [ws, restart]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, dep: DeploymentInfo) => ws.showContextMenu(e, buildMenu(dep)),
    [ws, buildMenu],
  );

  const columns: Column<DeploymentInfo>[] = useMemo(() => [
    { header: "Ready", cell: (dep) => (
      <span className={dep.readyReplicas === dep.replicas ? "text-success" : "text-warning"}>
        {dep.readyReplicas}/{dep.replicas}
      </span>
    ), sortValue: (dep) => (dep.readyReplicas === dep.replicas ? 1 : 0) },
    { header: "Status", cell: (dep) => <StatusBadge status={dep.status} />, sortValue: (dep) => dep.status },
    { header: "Image", cell: (dep) => <span className="text-muted-foreground">{dep.imageTag ?? "—"}</span> },
    {
      header: "Actions",
      className: "py-2 pr-4 w-px whitespace-nowrap",
      cell: (dep) => (
        <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
          <button
            onClick={() => restart(dep)}
            disabled={restartMutation.isPending}
            title={restartMutation.isPending ? "Restarting…" : undefined}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
          >
            Restart
          </button>
          <button
            onClick={() => setScaleTarget(dep)}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid={`deployment-scale-${dep.name}`}
          >
            Scale
          </button>
        </div>
      ),
    },
  ], [restart, restartMutation.isPending]);

  return (
    <div className="p-4">
      <ResourceTable
        data={deployments}
        isLoading={isLoading}
        error={error}
        isMulti={isMulti}
        testIdPrefix="deployment"
        tableBodyTestId="deployments-table-body"
        emptyMessage="No deployments found"
        onRowClick={(dep) => ws.openYaml("deployment", dep.name, dep.namespace)}
        onRowContextMenu={handleRowContextMenu}
        defaultSort={{ sortValue: (dep) => (dep.readyReplicas === dep.replicas ? 1 : 0), direction: "asc" }}
        columns={columns}
      />

      {scaleTarget && (
        <ScaleDialog
          kind="Deployment"
          name={scaleTarget.name}
          namespace={scaleTarget.namespace}
          currentReplicas={scaleTarget.replicas}
          isSaving={scaleMutation.isPending}
          onCancel={() => setScaleTarget(null)}
          onConfirm={(replicas) => confirmScale(scaleTarget, replicas)}
        />
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const color =
    status === "Available" ? "text-success" :
    status === "Progressing" ? "text-warning" :
    "text-destructive";
  return <span className={color}>{status}</span>;
}
