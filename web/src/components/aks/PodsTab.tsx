import { useEffect, useRef } from "react";
import { useAksPods, useAksDeletePod } from "@/lib/hooks";
import { showNotification } from "@/lib/tauri-bridge";
import type { PodInfo } from "@/lib/types";

interface PodsTabProps {
  ns: string;
  isMulti?: boolean;
  onPodClick?: (pod: PodInfo) => void;
  onContextMenu?: (e: React.MouseEvent, pod: PodInfo) => void;
}

export function PodsTab({ ns, isMulti, onPodClick, onContextMenu }: PodsTabProps) {
  const { data: pods, isLoading } = useAksPods(ns);
  const deleteMutation = useAksDeletePod();
  const prevStatusesRef = useRef<Map<string, string>>(new Map());
  const prevNsRef = useRef(ns);

  // Fires a native notification the moment a pod actually transitions into
  // Failed (not on initial load, which would spam notifications for
  // already-failed pods on open) — restores the "the app notices when
  // something breaks" behavior the MAUI health monitor had, for both demo
  // and real clusters.
  useEffect(() => {
    if (!pods) return;
    if (prevNsRef.current !== ns) {
      prevStatusesRef.current = new Map();
      prevNsRef.current = ns;
    }
    const prev = prevStatusesRef.current;
    for (const pod of pods) {
      const prevStatus = prev.get(pod.name);
      if (prevStatus && prevStatus !== "Failed" && pod.status === "Failed") {
        showNotification("Pod failed", `${pod.name} in ${ns} transitioned to Failed`).catch(() => {});
      }
    }
    prevStatusesRef.current = new Map(pods.map((p) => [p.name, p.status]));
  }, [pods, ns]);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!pods || pods.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No pods found</div>;

  const handleDelete = (pod: PodInfo) => {
    if (!confirm(`Delete pod ${pod.name}? The controller will recreate it.`)) return;
    deleteMutation.mutate({ ns: pod.namespace, name: pod.name });
  };

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
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
            <tr key={`${pod.namespace}/${pod.name}`} data-testid={`pod-row-${pod.name}`} className={`border-b last:border-0 ${onPodClick ? "cursor-pointer hover:bg-accent/50" : ""}`} onClick={() => onPodClick?.(pod)} onContextMenu={(e) => onContextMenu?.(e, pod)}>
              <td className="py-2 pr-4 font-medium">{pod.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{pod.namespace}</td>}
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
              <td className="py-2 pr-4" onClick={(e) => e.stopPropagation()}>
                <button
                  onClick={() => handleDelete(pod)}
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
