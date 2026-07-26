import { useState } from "react";
import { useAksDeployments, useAksRestartDeployment, useAksScaleDeployment } from "@/lib/hooks";
import type { DeploymentInfo } from "@/lib/types";

interface DeploymentsTabProps {
  ns: string;
  onContextMenu?: (e: React.MouseEvent, dep: DeploymentInfo) => void;
}

export function DeploymentsTab({ ns, onContextMenu }: DeploymentsTabProps) {
  const { data: deployments, isLoading } = useAksDeployments(ns);
  const restartMutation = useAksRestartDeployment();
  const scaleMutation = useAksScaleDeployment();
  const [scaling, setScaling] = useState<string | null>(null);
  const [scaleValue, setScaleValue] = useState(0);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!deployments || deployments.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No deployments found</div>;

  const handleRestart = (name: string) => {
    if (!confirm(`Restart deployment ${name}?`)) return;
    restartMutation.mutate({ ns, name });
  };

  const handleScale = (dep: DeploymentInfo) => {
    scaleMutation.mutate({ ns, name: dep.name, replicas: scaleValue });
    setScaling(null);
  };

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Ready</th>
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Image</th>
            <th className="py-2 pr-4">Actions</th>
          </tr>
        </thead>
        <tbody data-testid="deployments-table-body">
          {deployments.map((dep) => (
            <tr key={dep.name} data-testid={`deployment-row-${dep.name}`} className="border-b last:border-0 hover:bg-accent/30" onContextMenu={(e) => onContextMenu?.(e, dep)}>
              <td className="py-2 pr-4 font-medium">{dep.name}</td>
              <td className="py-2 pr-4">
                <span className={dep.readyReplicas === dep.replicas ? "text-green-500" : "text-yellow-500"}>
                  {dep.readyReplicas}/{dep.replicas}
                </span>
              </td>
              <td className="py-2 pr-4">
                <StatusBadge status={dep.status} />
              </td>
              <td className="py-2 pr-4 text-muted-foreground">{dep.imageTag ?? "—"}</td>
              <td className="py-2 pr-4">
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleRestart(dep.name)}
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
                        onClick={() => handleScale(dep)}
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
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
