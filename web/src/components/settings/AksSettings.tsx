import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useAksTestConnection } from "@/lib/hooks/useAks";
import { useNotification } from "@/components/layout/notification-context";
import { clampInt } from "@/lib/clamp-int";
import type { AksConfig } from "@/lib/types";
import { DraftInput } from "./DraftInput";

export function AksSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();
  const { notify } = useNotification();
  // `enabled: false`: only fires when "Test connection" is clicked, not on every render —
  // AppLayout's footer connectivity indicator already runs this query automatically
  // elsewhere; this is a second, click-triggered observer on the same cache entry.
  const test = useAksTestConnection({ enabled: false });

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

  const commitAutoRefresh = (raw: string) => {
    const result = clampInt(raw, { min: 5, max: 3600, fallback: aks.autoRefreshIntervalSeconds });
    if (result.invalid) {
      notify(
        "error",
        "Invalid auto-refresh interval",
        `"${raw}" isn't a number — kept at ${aks.autoRefreshIntervalSeconds}s.`,
      );
    } else if (result.clamped) {
      notify(
        "error",
        "Auto-refresh interval out of range",
        `Must be between 5 and 3600 seconds. Clamped to ${result.value}s.`,
      );
    }
    update({ autoRefreshIntervalSeconds: result.value });
  };

  const commitLogBufferSize = (raw: string) => {
    const result = clampInt(raw, { min: 100, max: 1_000_000, fallback: aks.logBufferSize });
    if (result.invalid) {
      notify(
        "error",
        "Invalid log buffer size",
        `"${raw}" isn't a number — kept at ${aks.logBufferSize}.`,
      );
    } else if (result.clamped) {
      notify(
        "error",
        "Log buffer size out of range",
        `Must be between 100 and 1,000,000 lines. Clamped to ${result.value.toLocaleString()}.`,
      );
    }
    update({ logBufferSize: result.value });
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
          <p className="mt-1 text-xs text-muted-foreground">
            Which kubeconfig context to connect with. Leave empty to use whichever context is
            currently active for that kubeconfig file (as shown by <code>kubectl config
            current-context</code>).
          </p>
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
              onCommit={commitAutoRefresh}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              data-testid="aks-auto-refresh-interval"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium">Log Buffer Size</label>
            <DraftInput
              type="number"
              value={String(aks.logBufferSize)}
              onCommit={commitLogBufferSize}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              data-testid="aks-log-buffer-size"
            />
            <p className="mt-1 text-xs text-muted-foreground">
              How many log lines to keep in memory per pod view before the oldest lines are
              dropped. Higher values use more memory.
            </p>
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

        <div className="flex items-center gap-2 pt-1">
          <button
            onClick={() => test.refetch()}
            disabled={test.isFetching}
            title={test.isFetching ? "Testing…" : undefined}
            className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="aks-test-connection"
          >
            {test.isFetching ? "Testing…" : "Test connection"}
          </button>
          {test.data && (
            <span
              className={`text-xs ${test.data.connected ? "text-success" : "text-destructive"}`}
              data-testid="aks-test-result"
            >
              {test.data.connected ? "Connected" : `Failed: ${test.data.error ?? "unknown error"}`}
            </span>
          )}
          {test.isError && <span className="text-xs text-destructive">{String(test.error)}</span>}
        </div>
      </div>
    </div>
  );
}
