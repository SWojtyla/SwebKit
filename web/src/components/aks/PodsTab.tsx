import { useAksPods, useAksDeletePod } from "@/lib/hooks";

export function PodsTab({ ns }: { ns: string }) {
  const { data: pods, isLoading } = useAksPods(ns);
  const deleteMutation = useAksDeletePod();

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!pods || pods.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No pods found</div>;

  const handleDelete = (name: string) => {
    if (!confirm(`Delete pod ${name}? The controller will recreate it.`)) return;
    deleteMutation.mutate({ ns, name });
  };

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Ready</th>
            <th className="py-2 pr-4">Restarts</th>
            <th className="py-2 pr-4">Node</th>
            <th className="py-2 pr-4">Age</th>
            <th className="py-2 pr-4">Actions</th>
          </tr>
        </thead>
        <tbody data-testid="pods-table-body">
          {pods.map((pod) => (
            <tr key={pod.name} data-testid={`pod-row-${pod.name}`} className="border-b last:border-0">
              <td className="py-2 pr-4 font-medium">{pod.name}</td>
              <td className="py-2 pr-4">
                <PodStatusBadge status={pod.status} />
              </td>
              <td className="py-2 pr-4">
                <span className={pod.ready ? "text-green-500" : "text-yellow-500"}>
                  {pod.readyDisplay}
                </span>
              </td>
              <td className="py-2 pr-4">
                {pod.restartCount > 0 ? (
                  <span className="text-yellow-500">{pod.restartCount}</span>
                ) : (
                  <span className="text-muted-foreground">0</span>
                )}
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{pod.nodeName ?? "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {pod.startTime ? new Date(pod.startTime).toLocaleDateString() : "—"}
              </td>
              <td className="py-2 pr-4">
                <button
                  onClick={() => handleDelete(pod.name)}
                  disabled={deleteMutation.isPending}
                  className="rounded border border-destructive px-2 py-1 text-xs text-destructive hover:bg-destructive/10"
                >
                  Delete
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PodStatusBadge({ status }: { status: string }) {
  const color =
    status === "Running" ? "text-green-500" :
    status === "Pending" ? "text-yellow-500" :
    status === "Failed" || status.includes("BackOff") || status.includes("Error") ? "text-destructive" :
    "text-muted-foreground";
  return <span className={color}>{status}</span>;
}
