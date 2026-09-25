import { useRef, useState } from "react";
import { CheckCircle2, Circle, Download, Upload } from "lucide-react";
import { useProfile, useUpdateProfile, useUserSettings, useUpdateUserSettings, useExportSettings, useImportSettings, useSettingsReadiness } from "@/lib/hooks";
import { useNotification } from "@/components/layout/notification-context";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

export function GeneralSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const { data: profile } = useProfile();
  const readinessState = useSettingsReadiness();
  const updateProfile = useUpdateProfile();
  const updateSettings = useUpdateUserSettings();
  const exportSettings = useExportSettings();
  const importSettings = useImportSettings();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [importStatus, setImportStatus] = useState<string | null>(null);
  const [pendingImportBundle, setPendingImportBundle] = useState<unknown>(null);
  const { notify } = useNotification();

  const runImport = async (bundle: unknown) => {
    try {
      await importSettings.mutateAsync(bundle);
      notify("success", "Settings imported", "Restart the app to ensure all changes are loaded.");
      setImportStatus("Import successful. Restart the app to ensure all changes are loaded.");
    } catch {
      setImportStatus("Import failed: invalid file");
      notify("error", "Import failed", "Invalid settings file");
    } finally {
      setPendingImportBundle(null);
    }
  };

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  const readiness = [
    { id: "aks", label: "Connect an AKS cluster", ready: readinessState?.aks ?? false },
    { id: "service-bus", label: "Connect a Service Bus namespace", ready: readinessState?.["service-bus"] ?? false },
    { id: "redis", label: "Connect a Redis cache", ready: readinessState?.redis ?? false },
    { id: "sql", label: "Connect a SQL database", ready: readinessState?.sql ?? false },
    { id: "storage", label: "Connect a Storage account", ready: readinessState?.storage ?? false },
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
        <h2 className="mb-3 text-lg font-semibold">API Client</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.verifyApiClientSsl}
            onChange={(e) => {
              const verifyApiClientSsl = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, verifyApiClientSsl }));
            }}
          />
          Verify SSL certificates
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Reject requests to hosts with an invalid/self-signed TLS certificate. Turn off only
          for local/dev endpoints you trust.
        </p>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.apiClientRequestTabs}
            onChange={(e) => {
              const apiClientRequestTabs = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, apiClientRequestTabs }));
            }}
          />
          Enable request tabs
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Open each request in its own tab so several stay open side by side, instead of one
          request replacing the last.
        </p>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.autoSaveRequests}
            onChange={(e) => {
              const autoSaveRequests = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, autoSaveRequests }));
            }}
          />
          Auto-save request changes
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Save edits to a request (URL, headers, body) back to its collection as you make
          them, instead of only when you explicitly save.
        </p>

        {profile && (
          <div className="mt-5" data-testid="key-vaults-section">
            <h3 className="mb-1 text-sm font-medium">Azure Key Vaults</h3>
            <p className="mb-2 text-xs text-muted-foreground">
              Named vaults used by environment variables of type "Key Vault". Authentication uses your Azure CLI identity.
            </p>
            {profile.config.keyVaults.map((kv, i) => (
              <div key={kv.id} className="mb-2 flex items-center gap-2">
                <DraftInput
                  type="text"
                  value={kv.name}
                  onCommit={(name) =>
                    updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.map((v) => (v.id === kv.id ? { ...v, name } : v)) } }))
                  }
                  placeholder="Name"
                  className="w-40 rounded border bg-background px-2 py-1 text-sm"
                  data-testid={`kv-name-${i}`}
                />
                <DraftInput
                  type="text"
                  value={kv.url}
                  onCommit={(url) =>
                    updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.map((v) => (v.id === kv.id ? { ...v, url } : v)) } }))
                  }
                  placeholder="https://my-vault.vault.azure.net/"
                  className="flex-1 rounded border bg-background px-2 py-1 text-sm"
                  data-testid={`kv-url-${i}`}
                />
                <button
                  onClick={() =>
                    updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.filter((v) => v.id !== kv.id) } }))
                  }
                  className="rounded border px-2 py-1 text-xs hover:bg-accent"
                  data-testid={`kv-remove-${i}`}
                >
                  Remove
                </button>
              </div>
            ))}
            <button
              onClick={() =>
                updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: [...prev.config.keyVaults, { id: crypto.randomUUID(), name: "", url: "" }] } }))
              }
              className="mt-1 rounded border px-2 py-1 text-xs hover:bg-accent"
              data-testid="kv-add"
            >
              + Add Key Vault
            </button>
          </div>
        )}
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Startup</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.warmupConnectionsOnStartup}
            onChange={(e) => {
              const warmupConnectionsOnStartup = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, warmupConnectionsOnStartup }));
            }}
          />
          Warm up connections on startup
        </label>
        <p className="mt-0.5 pl-6 text-xs text-muted-foreground">
          Connect to your configured AKS/Service Bus/Redis/Storage services as soon as the app
          opens, so the first tab you visit isn't the one waiting on a cold connection.
        </p>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.restoreLastWorkspaceOnStartup !== false}
            onChange={(e) => {
              const restoreLastWorkspaceOnStartup = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, restoreLastWorkspaceOnStartup }));
            }}
            data-testid="restore-workspace-toggle"
          />
          Restore last workspace on launch
        </label>
        <p className="mt-0.5 pl-6 text-xs text-muted-foreground">
          Reopen the page — including its selections — that was open when the app last closed,
          instead of always starting on the dashboard.
        </p>
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
            title={exportSettings.isPending ? "Exporting…" : undefined}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
          >
            <Download className="h-4 w-4" />
            Export settings
          </button>
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={importSettings.isPending}
            title={importSettings.isPending ? "Importing…" : undefined}
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
                setPendingImportBundle(bundle);
              } catch {
                setImportStatus("Import failed: invalid file");
                notify("error", "Import failed", "Invalid settings file");
              } finally {
                e.target.value = "";
              }
            }}
          />
        </div>
        {pendingImportBundle !== null && (
          <div className="mt-2">
            <ConfirmBar
              message="Importing will replace your current profiles, collections, environments, and settings. Continue?"
              confirmLabel="Import"
              onConfirm={() => runImport(pendingImportBundle)}
              onCancel={() => setPendingImportBundle(null)}
              testId="settings-import-confirm"
            />
          </div>
        )}
        {importStatus && (
          <p className="mt-2 text-xs text-muted-foreground">{importStatus}</p>
        )}
      </section>
    </div>
  );
}
