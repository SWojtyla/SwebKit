import { useState } from "react";
import { useAksHpas, useAksScaleHpa, useAksDeleteHpa, useAksSetHpaScalingEnabled } from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import { YamlViewer } from "./YamlViewer";
import type { HpaInfo } from "@/lib/types";

export function HpaTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
  const { notify } = useNotification();
  const { data: hpas, isLoading } = useAksHpas(ns);
  const scaleMutation = useAksScaleHpa();
  const deleteMutation = useAksDeleteHpa();
  const toggleMutation = useAksSetHpaScalingEnabled();

  const [scaleTarget, setScaleTarget] = useState<HpaInfo | null>(null);
  const [yamlTarget, setYamlTarget] = useState<HpaInfo | null>(null);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!hpas || hpas.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No HPAs found</div>;

  const handleScale = async (min: number, max: number) => {
    if (!scaleTarget) return;
    try {
      await scaleMutation.mutateAsync({ ns: scaleTarget.namespace, name: scaleTarget.name, minReplicas: min, maxReplicas: max });
      notify("success", "HPA scaled", `${scaleTarget.namespace}/${scaleTarget.name}: ${min}–${max} replicas`);
      setScaleTarget(null);
    } catch (e) {
      notify("error", "Scale HPA failed", String(e));
    }
  };

  const handleDelete = async (hpa: HpaInfo) => {
    if (!confirm(`Delete HPA ${hpa.name} in ${hpa.namespace}?`)) return;
    try {
      await deleteMutation.mutateAsync({ ns: hpa.namespace, name: hpa.name });
      notify("success", "HPA deleted", `${hpa.namespace}/${hpa.name}`);
    } catch (e) {
      notify("error", "Delete HPA failed", String(e));
    }
  };

  const handleToggleScaling = async (hpa: HpaInfo) => {
    const next = !hpa.isScalingDisabled;
    const action = next ? "disable" : "enable";
    if (!confirm(`${action === "disable" ? "Disable" : "Enable"} scaling for ${hpa.name}?`)) return;
    try {
      await toggleMutation.mutateAsync({ ns: hpa.namespace, name: hpa.name, enabled: !next });
      notify("success", `Scaling ${action}d`, `${hpa.namespace}/${hpa.name}`);
    } catch (e) {
      notify("error", `Scale ${action} failed`, String(e));
    }
  };

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Target</th>
            <th className="py-2 pr-4">Min</th>
            <th className="py-2 pr-4">Max</th>
            <th className="py-2 pr-4">Current</th>
            <th className="py-2 pr-4">Desired</th>
            <th className="py-2 pr-4">CPU%</th>
            <th className="py-2 pr-4">Type</th>
            <th className="py-2 pr-4">Actions</th>
          </tr>
        </thead>
        <tbody data-testid="hpas-table-body">
          {hpas.map((hpa) => (
            <tr key={`${hpa.namespace}/${hpa.name}`} data-testid={`hpa-row-${hpa.name}`} className="border-b last:border-0">
              <td className="py-2 pr-4 font-medium">{hpa.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{hpa.namespace}</td>}
              <td className="py-2 pr-4 text-xs text-muted-foreground">{hpa.targetKind}/{hpa.targetName}</td>
              <td className="py-2 pr-4">{hpa.minReplicas}</td>
              <td className="py-2 pr-4">{hpa.maxReplicas}</td>
              <td className="py-2 pr-4">{hpa.currentReplicas}</td>
              <td className="py-2 pr-4 text-green-500">{hpa.desiredReplicas}</td>
              <td className="py-2 pr-4">
                {hpa.currentCpuUtilizationPercent != null ? (
                  <span className={
                    hpa.targetCpuUtilizationPercent != null && hpa.currentCpuUtilizationPercent > hpa.targetCpuUtilizationPercent
                      ? "text-yellow-500" : "text-muted-foreground"
                  }>
                    {hpa.currentCpuUtilizationPercent}%
                  </span>
                ) : "—"}
              </td>
              <td className="py-2 pr-4">
                <div className="flex flex-wrap gap-1">
                  {hpa.isKedaManaged ? (
                    <span className="rounded bg-purple-500/20 px-1.5 py-0.5 text-xs text-purple-500">KEDA</span>
                  ) : (
                    <span className="rounded px-1.5 py-0.5 text-xs text-muted-foreground">HPA</span>
                  )}
                  {hpa.isScalingDisabled && (
                    <span className="rounded bg-red-500/20 px-1.5 py-0.5 text-xs text-red-500">Disabled</span>
                  )}
                </div>
              </td>
              <td className="py-2 pr-4">
                <div className="flex flex-wrap gap-1">
                  <button
                    onClick={() => setScaleTarget(hpa)}
                    className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                  >
                    Scale
                  </button>
                  <button
                    onClick={() => handleToggleScaling(hpa)}
                    disabled={toggleMutation.isPending}
                    className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                  >
                    {hpa.isScalingDisabled ? "Enable" : "Disable"}
                  </button>
                  <button
                    onClick={() => setYamlTarget(hpa)}
                    className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                  >
                    YAML
                  </button>
                  <button
                    onClick={() => handleDelete(hpa)}
                    disabled={deleteMutation.isPending}
                    className="rounded border border-destructive px-2 py-1 text-xs text-destructive hover:bg-destructive/10"
                  >
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {scaleTarget && (
        <ScaleHpaForm
          hpa={scaleTarget}
          onCancel={() => setScaleTarget(null)}
          onSave={handleScale}
          isSaving={scaleMutation.isPending}
        />
      )}

      {yamlTarget && (
        <div className="mt-4 rounded border">
          <YamlViewer
            ns={yamlTarget.namespace}
            kind="HorizontalPodAutoscaler"
            name={yamlTarget.name}
            onClose={() => setYamlTarget(null)}
          />
        </div>
      )}
    </div>
  );
}

function ScaleHpaForm({
  hpa,
  onCancel,
  onSave,
  isSaving,
}: {
  hpa: HpaInfo;
  onCancel: () => void;
  onSave: (min: number, max: number) => void;
  isSaving: boolean;
}) {
  const [min, setMin] = useState(hpa.minReplicas);
  const [max, setMax] = useState(hpa.maxReplicas);

  return (
    <div className="mt-4 rounded border p-4">
      <h4 className="mb-2 text-sm font-medium">Scale {hpa.name}</h4>
      <div className="flex items-end gap-2">
        <label className="text-xs">
          Min replicas
          <input
            type="number"
            min={1}
            value={min}
            onChange={(e) => setMin(Math.max(1, parseInt(e.target.value, 10) || 0))}
            className="mt-1 block w-24 rounded border bg-background px-2 py-1 text-sm"
          />
        </label>
        <label className="text-xs">
          Max replicas
          <input
            type="number"
            min={1}
            value={max}
            onChange={(e) => setMax(Math.max(1, parseInt(e.target.value, 10) || 0))}
            className="mt-1 block w-24 rounded border bg-background px-2 py-1 text-sm"
          />
        </label>
        <button
          onClick={() => onSave(min, max)}
          disabled={isSaving || min > max}
          className="rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
        >
          Save
        </button>
        <button
          onClick={onCancel}
          disabled={isSaving}
          className="rounded border border-border px-3 py-1 text-xs hover:bg-accent/50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
