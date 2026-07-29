import { useProfile, useUpdateProfile } from "@/lib/hooks";
import type { AksConfig } from "@/lib/types";

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

  const update = (patch: Partial<AksConfig>) => {
    updateProfile.mutate({
      ...profile,
      config: {
        ...profile.config,
        aksConfig: { ...aks, ...patch },
      },
    });
  };

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">AKS / Kubernetes</h2>

      <div className="space-y-3 rounded-lg border p-4">
        <div>
          <label className="mb-1 block text-sm font-medium">Kubeconfig Path</label>
          <input
            type="text"
            value={aks.kubeconfigPath ?? ""}
            onChange={(e) => update({ kubeconfigPath: e.target.value || null })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="Leave empty for default ~/.kube/config"
          />
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium">Kubeconfig Context</label>
          <input
            type="text"
            value={aks.kubeconfigContext ?? ""}
            onChange={(e) => update({ kubeconfigContext: e.target.value || null })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="Leave empty for current context"
          />
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium">Default Namespace</label>
          <input
            type="text"
            value={aks.defaultNamespace}
            onChange={(e) => update({ defaultNamespace: e.target.value })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="default"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="mb-1 block text-sm font-medium">Auto-refresh (seconds)</label>
            <input
              type="number"
              value={aks.autoRefreshIntervalSeconds}
              onChange={(e) =>
                update({ autoRefreshIntervalSeconds: parseInt(e.target.value) || 30 })
              }
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium">Log Buffer Size</label>
            <input
              type="number"
              value={aks.logBufferSize}
              onChange={(e) =>
                update({ logBufferSize: parseInt(e.target.value) || 10_000 })
              }
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
