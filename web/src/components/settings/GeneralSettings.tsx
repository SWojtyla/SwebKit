import { useRef, useState } from "react";
import { CheckCircle2, Circle, Download, Upload } from "lucide-react";
import { useProfile, useUserSettings, useUpdateUserSettings, useExportSettings, useImportSettings } from "@/lib/hooks";
import { useSettingsStore } from "@/lib/stores/settings";
import { useNotification } from "@/components/layout/NotificationSystem";

export function GeneralSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const { data: profile } = useProfile();
  const updateSettings = useUpdateUserSettings();
  const exportSettings = useExportSettings();
  const importSettings = useImportSettings();
  const { theme, toggleTheme } = useSettingsStore();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [importStatus, setImportStatus] = useState<string | null>(null);
  const { notify } = useNotification();

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
            {theme === "dark" ? "Dark" : theme === "fancy" ? "✨ Fancy ✨" : "Light"}
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

      <section>
        <h2 className="mb-3 text-lg font-semibold">Backup & Restore</h2>
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={async () => {
              try {
                const data = await exportSettings.mutateAsync();
                const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
                const url = URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = url;
                a.download = `swebkit-settings-${new Date().toISOString().slice(0, 10)}.json`;
                a.click();
                URL.revokeObjectURL(url);
                notify("success", "Settings exported", a.download);
              } catch {
                setImportStatus("Export failed");
                notify("error", "Export failed");
              }
            }}
            disabled={exportSettings.isPending}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
          >
            <Download className="h-4 w-4" />
            Export settings
          </button>
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={importSettings.isPending}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
          >
            <Upload className="h-4 w-4" />
            Import settings
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            onChange={async (e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              try {
                const text = await file.text();
                const bundle = JSON.parse(text);
                if (window.confirm("Importing will replace your current profiles, collections, environments, and settings. Continue?")) {
                  await importSettings.mutateAsync(bundle);
                  notify("success", "Settings imported", "Restart the app to ensure all changes are loaded.");
                  setImportStatus("Import successful. Restart the app to ensure all changes are loaded.");
                }
              } catch {
                setImportStatus("Import failed: invalid file");
                notify("error", "Import failed", "Invalid settings file");
              } finally {
                e.target.value = "";
              }
            }}
          />
        </div>
        {importStatus && (
          <p className="mt-2 text-xs text-muted-foreground">{importStatus}</p>
        )}
      </section>
    </div>
  );
}
