import { useState } from "react";
import { useAksDeployments, useAksRestartDeployment, useAksScaleDeployment } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { DeploymentInfo } from "@/lib/types";

interface DeploymentsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function DeploymentsTab({ ns, isMulti }: DeploymentsTabProps) {
  const { data: deployments, isLoading } = useAksDeployments(ns);
  const ws = useAksWorkspace();
  const restartMutation = useAksRestartDeployment();
  const scaleMutation = useAksScaleDeployment();

  const [scaling, setScaling] = useState<string | null>(null);
  const [scaleValue, setScaleValue] = useState(0);

  const scale = (dep: DeploymentInfo) => {
    ws.requestConfirm({
      message: `Scale deployment "${dep.name}" to ${scaleValue} replicas?`,
      resourceName: dep.name,
      onConfirm: () => scaleMutation.mutate({ ns: dep.namespace, name: dep.name, replicas: scaleValue }),
    });
    setScaling(null);
  };

  const restart = (dep: DeploymentInfo) => {
    ws.requestConfirm({
      message: `Restart deployment "${dep.name}"?`,
      resourceName: dep.name,
      onConfirm: () => restartMutation.mutate({ ns: dep.namespace, name: dep.name }),
    });
  };

  const buildMenu = (dep: DeploymentInfo): ContextMenuItem[] => [
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
    { label: "Probe failures", icon: "🚧", onClick: () => {}, disabled: true },
    { label: "Placement", icon: "📍", onClick: () => {}, disabled: true },
    { label: "", separator: true, onClick: () => {} },
    { label: "Restart Deployment", icon: "↻", onClick: () => restart(dep) },
    { label: "Scale...", icon: "⇳", onClick: () => {
      setScaling(dep.name);
      setScaleValue(dep.replicas);
    }},
  ];

  return (
    <div className="p-4">
      <ResourceTable
        data={deployments}
        isLoading={isLoading}
        isMulti={isMulti}
        testIdPrefix="deployment"
        tableBodyTestId="deployments-table-body"
        emptyMessage="No deployments found"
        onRowContextMenu={(e, dep) => ws.showContextMenu(e, buildMenu(dep))}
        columns={[
          { header: "Ready", cell: (dep) => (
            <span className={dep.readyReplicas === dep.replicas ? "text-green-500" : "text-yellow-500"}>
              {dep.readyReplicas}/{dep.replicas}
            </span>
          )},
          { header: "Status", cell: (dep) => <StatusBadge status={dep.status} /> },
          { header: "Image", cell: (dep) => <span className="text-muted-foreground">{dep.imageTag ?? "—"}</span> },
          { header: "Actions", cell: (dep) => (
            <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
              <button
                onClick={() => restart(dep)}
                disabled={restartMutation.isPending}
                className="rounded border px-2 py-1 text-xs hover:bg-accent"
              >
                Restart
              </button>
              {scaling === dep.name ? (
                <>
                  <input
                    type="number"
                    value={scaleValue}
                    onChange={(e) => setScaleValue(parseInt(e.target.value) || 0)}
                    className="w-16 rounded border bg-card px-2 py-1 text-xs"
                    autoFocus
                  />
                  <button
                    onClick={() => scale(dep)}
                    className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground"
                  >
                    OK
                  </button>
                  <button
                    onClick={() => setScaling(null)}
                    className="rounded border px-2 py-1 text-xs"
                  >
                    Cancel
                  </button>
                </>
              ) : (
                <button
                  onClick={() => {
                    setScaling(dep.name);
                    setScaleValue(dep.replicas);
                  }}
                  className="rounded border px-2 py-1 text-xs hover:bg-accent"
                >
                  Scale
                </button>
              )}
            </div>
          )},
        ]}
      />
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const color =
    status === "Available" ? "text-green-500" :
    status === "Progressing" ? "text-yellow-500" :
    "text-destructive";
  return <span className={color}>{status}</span>;
}
