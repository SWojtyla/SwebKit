import { useCallback, useMemo, useState } from "react";
import { useAksHpas, useAksScaleHpa, useAksDeleteHpa, useAksSetHpaScalingEnabled } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import { YamlViewer } from "./YamlViewer";
import type { HpaInfo } from "@/lib/types";

export function HpaTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
  const ws = useAksWorkspace();
  const { data: hpas, isLoading } = useAksHpas(ns);
  const scaleMutation = useAksScaleHpa();
  const deleteMutation = useAksDeleteHpa();
  const toggleMutation = useAksSetHpaScalingEnabled();

  const [scaleTarget, setScaleTarget] = useState<HpaInfo | null>(null);
  const [yamlTarget, setYamlTarget] = useState<HpaInfo | null>(null);

  const handleScale = useCallback(async (min: number, max: number) => {
    if (!scaleTarget) return;
    scaleMutation.mutate({ ns: scaleTarget.namespace, name: scaleTarget.name, minReplicas: min, maxReplicas: max });
    setScaleTarget(null);
  }, [scaleTarget, scaleMutation.mutate]);

  const handleDelete = useCallback((hpa: HpaInfo) => {
    ws.requestConfirm({
      message: `Delete HPA ${hpa.name} in ${hpa.namespace}?`,
      resourceName: hpa.name,
      onConfirm: () => deleteMutation.mutate({ ns: hpa.namespace, name: hpa.name }),
    });
  }, [ws, deleteMutation.mutate]);

  const handleToggleScaling = useCallback((hpa: HpaInfo) => {
    const next = !hpa.isScalingDisabled;
    const action = next ? "disable" : "enable";
    ws.requestConfirm({
      message: `${action === "disable" ? "Disable" : "Enable"} scaling for ${hpa.name}?`,
      resourceName: hpa.name,
      onConfirm: () => toggleMutation.mutate({ ns: hpa.namespace, name: hpa.name, enabled: !next }),
    });
  }, [ws, toggleMutation.mutate]);

  const columns: Column<HpaInfo>[] = useMemo(() => [
          { header: "Target", cell: (hpa) => <span className="text-xs text-muted-foreground">{hpa.targetKind}/{hpa.targetName}</span> },
          { header: "Min", cell: (hpa) => hpa.minReplicas },
          { header: "Max", cell: (hpa) => hpa.maxReplicas },
          { header: "Current", cell: (hpa) => hpa.currentReplicas },
          { header: "Desired", cell: (hpa) => <span className="text-success">{hpa.desiredReplicas}</span> },
          { header: "CPU%", cell: (hpa) => (
            hpa.currentCpuUtilizationPercent != null ? (
              <span className={
                hpa.targetCpuUtilizationPercent != null && hpa.currentCpuUtilizationPercent > hpa.targetCpuUtilizationPercent
                  ? "text-warning" : "text-muted-foreground"
              }>
                {hpa.currentCpuUtilizationPercent}%
              </span>
            ) : "—"
          )},
          { header: "Type", cell: (hpa) => (
            <div className="flex flex-wrap gap-1">
              {hpa.isKedaManaged ? (
                <span className="rounded bg-purple-500/20 px-1.5 py-0.5 text-xs text-purple-500">KEDA</span>
              ) : (
                <span className="rounded px-1.5 py-0.5 text-xs text-muted-foreground">HPA</span>
              )}
              {hpa.isScalingDisabled && (
                <>
                  {" "}
                  <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">Disabled</span>
                </>
              )}
            </div>
          )},
          { header: "Actions", cell: (hpa) => (
            <div className="flex flex-wrap gap-1" onClick={(e) => e.stopPropagation()}>
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
          )},
  ], [handleToggleScaling, toggleMutation.isPending, handleDelete, deleteMutation.isPending]);

  return (
    <div className="p-4">
      <ResourceTable
        data={hpas}
        isLoading={isLoading}
        isMulti={isMulti}
        testIdPrefix="hpa"
        tableBodyTestId="hpas-table-body"
        emptyMessage="No HPAs found"
        columns={columns}
      />

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
