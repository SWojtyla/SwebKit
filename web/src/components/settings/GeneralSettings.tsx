import { CheckCircle2, Circle } from "lucide-react";
import { useProfile, useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { useSettingsStore } from "@/lib/stores/settings";

export function GeneralSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const { data: profile } = useProfile();
  const updateSettings = useUpdateUserSettings();
  const { theme, toggleTheme } = useSettingsStore();

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  const readiness = [
    { id: "aks", label: "Connect an AKS cluster", ready: !!profile?.config.aksConfig },
    { id: "service-bus", label: "Connect a Service Bus namespace", ready: (profile?.serviceBusNamespaces.length ?? 0) > 0 },
    { id: "redis", label: "Connect a Redis cache", ready: (profile?.config.redisConfig?.caches.length ?? 0) > 0 },
    { id: "storage", label: "Connect a Storage account", ready: (profile?.config.storageAccounts.length ?? 0) > 0 },
  ];

  return (
    <div className="space-y-6">
      <section data-testid="getting-started-checklist">
        <h2 className="mb-1 text-lg font-semibold">Getting started</h2>
        <p className="mb-3 text-sm text-muted-foreground">Connect the services you use to make the operator workspace ready.</p>
        <div className="space-y-2">
          {readiness.map((item) => (
            <div key={item.id} className="flex items-center gap-2 text-sm" data-testid={`getting-started-${item.id}`}>
              {item.ready ? <CheckCircle2 className="h-4 w-4 text-success" /> : <Circle className="h-4 w-4 text-muted-foreground" />}
              <span className={item.ready ? "text-foreground" : "text-muted-foreground"}>{item.label}</span>
              <span className="ml-auto text-xs text-muted-foreground">{item.ready ? "Ready" : "Not configured"}</span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Appearance</h2>
        <div className="flex items-center gap-3">
          <span className="text-sm">Theme:</span>
          <button
            onClick={toggleTheme}
            className="rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent"
          >
            {theme === "dark" ? "Dark" : "Light"}
          </button>
        </div>
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">API Client</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.verifyApiClientSsl}
            onChange={(e) =>
              updateSettings.mutate({
                ...settings,
                verifyApiClientSsl: e.target.checked,
              })
            }
          />
          Verify SSL certificates
        </label>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.apiClientRequestTabs}
            onChange={(e) =>
              updateSettings.mutate({
                ...settings,
                apiClientRequestTabs: e.target.checked,
              })
            }
          />
          Enable request tabs
        </label>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.autoSaveRequests}
            onChange={(e) =>
              updateSettings.mutate({
                ...settings,
                autoSaveRequests: e.target.checked,
              })
            }
          />
          Auto-save request changes
        </label>
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Startup</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.warmupConnectionsOnStartup}
            onChange={(e) =>
              updateSettings.mutate({
                ...settings,
                warmupConnectionsOnStartup: e.target.checked,
              })
            }
          />
          Warm up connections on startup
        </label>
      </section>
    </div>
  );
}
