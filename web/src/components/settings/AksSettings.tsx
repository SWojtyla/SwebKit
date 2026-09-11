import { useProfile, useUpdateProfile } from "@/lib/hooks";
import type { AksConfig } from "@/lib/types";
import { DraftInput } from "./DraftInput";

export function AksSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();

  if (!profile) return null;

  const aks = profile.config.aksConfig ?? {
    kubeconfigPath: null,
    kubeconfigContext: null,
    defaultNamespace: "",
    watchedDeployments: [],
    logBufferSize: 10_000,
    autoRefreshIntervalSeconds: 30,
    monitoringEnabled: false,
    monitoredNamespaces: [],
  };

  // Updater form so concurrent edits queue against current state instead of each
  // PUTting a profile snapshot taken before the other landed.
  const update = (patch: Partial<AksConfig>) => {
    updateProfile.mutate((prev) => ({
      ...prev,
      config: {
        ...prev.config,
        aksConfig: { ...(prev.config.aksConfig ?? aks), ...patch },
      },
    }));
  };

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">AKS / Kubernetes</h2>

      <div className="space-y-3 rounded-lg border p-4">
        <div>
          <label className="mb-1 block text-sm font-medium">Kubeconfig Path</label>
          <DraftInput
            type="text"
            value={aks.kubeconfigPath ?? ""}
            onCommit={(v) => update({ kubeconfigPath: v || null })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="Leave empty for default ~/.kube/config"
          />
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium">Kubeconfig Context</label>
          <DraftInput
            type="text"
            value={aks.kubeconfigContext ?? ""}
            onCommit={(v) => update({ kubeconfigContext: v || null })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="Leave empty for current context"
          />
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium">Default Namespace</label>
          <DraftInput
            type="text"
            value={aks.defaultNamespace}
            onCommit={(v) => update({ defaultNamespace: v })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="default"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="mb-1 block text-sm font-medium">Auto-refresh (seconds)</label>
            <DraftInput
              type="number"
              value={String(aks.autoRefreshIntervalSeconds)}
              onCommit={(v) => update({ autoRefreshIntervalSeconds: parseInt(v) || 30 })}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium">Log Buffer Size</label>
            <DraftInput
              type="number"
              value={String(aks.logBufferSize)}
              onCommit={(v) => update({ logBufferSize: parseInt(v) || 10_000 })}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            />
          </div>
        </div>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={aks.monitoringEnabled}
            onChange={(e) => update({ monitoringEnabled: e.target.checked })}
          />
          Enable pod health monitoring
        </label>
      </div>
    </div>
  );
}
