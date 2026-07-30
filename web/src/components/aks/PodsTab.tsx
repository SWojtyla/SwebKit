import { useEffect, useRef, useMemo, useState } from "react";
import { useAksPods, useAksDeletePod, useAksPodMetrics } from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import { showNotification } from "@/lib/tauri-bridge";
import type { PodInfo, PodMetricInfo } from "@/lib/types";

interface PodsTabProps {
  ns: string;
  isMulti?: boolean;
  onPodClick?: (pod: PodInfo) => void;
  onContextMenu?: (e: React.MouseEvent, pod: PodInfo) => void;
  onDeletePod?: (pod: PodInfo) => void;
}

const CPU_CEILING_MILLICORES = 500;
const MEMORY_CEILING_MI = 512;

function formatCpu(cores: number): string {
  return `${(cores * 1000).toFixed(0)}m`;
}

function formatMemory(bytes: number): string {
  return `${Math.round(bytes / (1024 * 1024))}Mi`;
}

function cpuClass(cores: number): string {
  if (cores > 0.4) return "text-destructive";
  if (cores > 0.15) return "text-yellow-500";
  return "text-green-500";
}

function memoryClass(mi: number): string {
  if (mi > 400) return "text-destructive";
  if (mi > 200) return "text-yellow-500";
  return "text-green-500";
}

function formatAge(startTime: string | null | undefined): string {
  if (!startTime) return "—";
  const start = new Date(startTime);
  if (Number.isNaN(start.getTime())) return "—";
  const minutes = Math.floor((Date.now() - start.getTime()) / 60000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (days >= 1) return `${days}d`;
  if (hours >= 1) return `${hours}h`;
  return `${Math.max(0, minutes)}m`;
}

function MetricBar({ value, className }: { value: number; className: string }) {
  const pct = Math.min(100, Math.max(0, value * 100));
  return (
    <div className="h-1.5 w-16 rounded-full bg-muted">
      <div
        className={`h-1.5 rounded-full ${className}`}
        style={{ width: `${pct}%` }}
      />
    </div>
  );
}

function getPodMetrics(metrics: PodMetricInfo[] | undefined, pod: PodInfo) {
  return metrics?.find((m) => m.podName === pod.name && m.namespace === pod.namespace);
}

function aggregatePodUsage(metric: PodMetricInfo | undefined) {
  if (!metric || metric.containers.length === 0) return null;
  const cpu = metric.containers.reduce((sum, c) => sum + (c.cpuCores ?? 0), 0);
  const memory = metric.containers.reduce((sum, c) => sum + (c.memoryBytes ?? 0), 0);
  return { cpu, memory };
}

function PodMetricCell({ pod, metrics }: { pod: PodInfo; metrics: PodMetricInfo[] | undefined }) {
  const usage = useMemo(() => aggregatePodUsage(getPodMetrics(metrics, pod)), [metrics, pod]);

  if (!usage) {
    return (
      <>
        <td className="py-2 pr-4 text-xs text-muted-foreground" title="Metrics unavailable — metrics-server may not be installed or pod has no resource data">—</td>
        <td className="py-2 pr-4 text-xs text-muted-foreground" title="Metrics unavailable — metrics-server may not be installed or pod has no resource data">—</td>
      </>
    );
  }

  const cpuPct = (usage.cpu * 1000) / CPU_CEILING_MILLICORES;
  const memoryMi = usage.memory / (1024 * 1024);
  const memoryPct = memoryMi / MEMORY_CEILING_MI;

  return (
    <>
      <td className="py-2 pr-4">
        <div className="flex items-center gap-2" title={`${formatCpu(usage.cpu)} / ~${CPU_CEILING_MILLICORES}m`}>
          <span className={`text-xs font-mono ${cpuClass(usage.cpu)}`}>{formatCpu(usage.cpu)}</span>
          <MetricBar value={cpuPct} className={cpuClass(usage.cpu).replace("text-", "bg-")} />
        </div>
      </td>
      <td className="py-2 pr-4">
        <div className="flex items-center gap-2" title={`${formatMemory(usage.memory)} / ~${MEMORY_CEILING_MI}Mi`}>
          <span className={`text-xs font-mono ${memoryClass(memoryMi)}`}>{formatMemory(usage.memory)}</span>
          <MetricBar value={memoryPct} className={memoryClass(memoryMi).replace("text-", "bg-")} />
        </div>
      </td>
    </>
  );
}

export function PodsTab({ ns, isMulti, onPodClick, onContextMenu, onDeletePod }: PodsTabProps) {
  const { data: pods, isLoading } = useAksPods(ns);
  const { data: metrics } = useAksPodMetrics(ns);
  const [hideCompleted, setHideCompleted] = useState(true);
  const deleteMutation = useAksDeletePod();
  const { notify } = useNotification();
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

  const isCompletedPod = (pod: PodInfo) =>
    pod.phase === "Succeeded" || pod.status?.toLowerCase() === "completed";

  const visiblePods = hideCompleted ? pods?.filter((p) => !isCompletedPod(p)) : pods;

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!visiblePods || visiblePods.length === 0)
    return (
      <div className="p-4 text-sm text-muted-foreground">
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={hideCompleted}
            onChange={(e) => setHideCompleted(e.target.checked)}
          />
          Hide completed pods
        </label>
        <p className="mt-2">No pods found</p>
      </div>
    );

  const handleDelete = (pod: PodInfo) => {
    if (onDeletePod) {
      onDeletePod(pod);
      return;
    }
    if (!confirm(`Delete pod ${pod.name}? The controller will recreate it.`)) return;
    deleteMutation.mutate({ ns: pod.namespace, name: pod.name }, {
      onSuccess: () => notify("success", "Pod deleted", `${pod.namespace}/${pod.name}`),
      onError: (e) => notify("error", "Delete pod failed", String(e)),
    });
  };

  return (
    <div className="p-4">
      <div className="mb-2 flex items-center gap-2 px-4 pt-4">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={hideCompleted}
            onChange={(e) => setHideCompleted(e.target.checked)}
          />
          Hide completed pods
        </label>
      </div>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Ready</th>
            <th className="py-2 pr-4">CPU</th>
            <th className="py-2 pr-4">Memory</th>
            <th className="py-2 pr-4">Restarts</th>
            <th className="py-2 pr-4">Node</th>
            <th className="py-2 pr-4">Age</th>
            <th className="py-2 pr-4">Actions</th>
          </tr>
        </thead>
        <tbody data-testid="pods-table-body">
          {visiblePods.map((pod) => (
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
              <PodMetricCell pod={pod} metrics={metrics} />
              <td className="py-2 pr-4">
                {pod.restartCount > 0 ? (
                  <span className="text-yellow-500">{pod.restartCount}</span>
                ) : (
                  <span className="text-muted-foreground">0</span>
                )}
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{pod.nodeName ?? "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {formatAge(pod.startTime)}
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
