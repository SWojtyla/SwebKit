import { useRef, useState } from "react";
import { CheckCircle2, Circle, Download, RefreshCw, Upload } from "lucide-react";
import { useProfile, useUpdateProfile, useUserSettings, useUpdateUserSettings, useExportSettings, useImportSettings, useExportTeamPack, useImportTeamPack, useSettingsReadiness, useUpdateCheck } from "@/lib/hooks";
import { openExternal } from "@/lib/tauri-bridge";
import type { TeamPackImportResult } from "@/lib/api";
import { useNotification } from "@/components/layout/notification-context";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

const ENV_TAG_PRESETS = ["dev", "stg", "prd"];

export function GeneralSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const { data: profile } = useProfile();
  const readinessState = useSettingsReadiness();
  const updateProfile = useUpdateProfile();
  const updateSettings = useUpdateUserSettings();
  const exportSettings = useExportSettings();
  const importSettings = useImportSettings();
  const exportTeamPack = useExportTeamPack();
  const importTeamPack = useImportTeamPack();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const packInputRef = useRef<HTMLInputElement>(null);
  const [importStatus, setImportStatus] = useState<string | null>(null);
  const [pendingImportBundle, setPendingImportBundle] = useState<unknown>(null);
  const [pendingPack, setPendingPack] = useState<unknown>(null);
  const [packReport, setPackReport] = useState<TeamPackImportResult | null>(null);
  const [packStrategy, setPackStrategy] = useState<"merge" | "replace">("merge");
  const [packStatus, setPackStatus] = useState<string | null>(null);
  const updateCheck = useUpdateCheck();
  const { notify } = useNotification();

  // A tag profiles.json holds that isn't one of the presets keeps its own option
  // so the select shows it instead of snapping to Auto.
  const envTag = profile?.config.environmentTag ?? "";
  const customEnvTag =
    envTag && !ENV_TAG_PRESETS.includes(envTag) ? envTag : null;

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

  // Preview (dry run) is the whole point of the flow: the user sees exactly what the pack
  // would add/update/skip — plus which secrets still need re-linking — before anything persists.
  const previewPack = async (pack: unknown, strategy: "merge" | "replace") => {
    setPendingPack(pack);
    setPackReport(null);
    try {
      const report = await importTeamPack.mutateAsync({ pack, opts: { dryRun: true, strategy } });
      setPackReport(report);
    } catch {
      setPendingPack(null);
      setPackStatus("Pack preview failed: not a valid team pack file");
      notify("error", "Pack preview failed", "Not a valid team pack file");
    }
  };

  const applyPack = async () => {
    if (pendingPack === null) return;
    try {
      const report = await importTeamPack.mutateAsync({
        pack: pendingPack,
        opts: { strategy: packStrategy },
      });
      notify(
        "success",
        "Team pack imported",
        `${report.added} added · ${report.updated} updated · ${report.skipped} skipped`,
      );
      setPackStatus(`Import applied: ${report.added} added · ${report.updated} updated · ${report.skipped} skipped`);
    } catch {
      setPackStatus("Import failed");
      notify("error", "Team pack import failed");
    } finally {
      setPendingPack(null);
      setPackReport(null);
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

      {profile && (
        <section data-testid="profile-settings">
          <h2 className="mb-1 text-lg font-semibold">Profile</h2>
          <p className="mb-3 text-sm text-muted-foreground">
            Workspace identity — drives the environment badge in the top bar.
          </p>
          <div className="space-y-3">
            <div>
              <label className="mb-1 block text-sm" htmlFor="profile-name-input">
                Profile name
              </label>
              <DraftInput
                id="profile-name-input"
                type="text"
                value={profile.config.name}
                onCommit={(name) =>
                  updateProfile.mutate((prev) => ({
                    ...prev,
                    config: { ...prev.config, name: name || "Default" },
                  }))
                }
                className="w-64 rounded border bg-background px-2 py-1 text-sm"
                data-testid="profile-name-input"
              />
            </div>
            <div>
              <label className="mb-1 block text-sm" htmlFor="profile-env-tag">
                Environment tag
              </label>
              {/* Discrete control → commit immediately (no DraftInput debounce). */}
              <select
                id="profile-env-tag"
                value={envTag}
                onChange={(e) => {
                  // Capture before mutate: the updater runs after React restores
                  // the DOM, so reading e.target inside it would see the reverted value.
                  const environmentTag = e.target.value || null;
                  updateProfile.mutate((prev) => ({
                    ...prev,
                    config: { ...prev.config, environmentTag },
                  }));
                }}
                className="rounded border bg-background px-2 py-1 text-sm"
                data-testid="profile-env-tag"
              >
                <option value="">Auto-detect</option>
                {ENV_TAG_PRESETS.map((tag) => (
                  <option key={tag} value={tag}>
                    {tag}
                  </option>
                ))}
                {customEnvTag && (
                  <option value={customEnvTag}>{customEnvTag}</option>
                )}
              </select>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Explicit tag wins over auto-detection; any value works, but
                dev/stg/prd get the tier tinting.
              </p>
            </div>
            <div>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={profile.config.isProduction}
                  onChange={(e) => {
                    const isProduction = e.target.checked;
                    updateProfile.mutate((prev) => ({
                      ...prev,
                      config: { ...prev.config, isProduction },
                    }));
                  }}
                  data-testid="profile-is-production"
                />
                Production profile
              </label>
              <p className="mt-0.5 pl-6 text-xs text-muted-foreground">
                Shows the PRD badge and a banner under the top bar, and tightens
                destructive-action prompts (e.g. AKS type-to-confirm).
              </p>
            </div>
          </div>
        </section>
      )}

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

      <section data-testid="team-pack-section">
        <h2 className="mb-1 text-lg font-semibold">Team workspace</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Share a secrets-free slice of this workspace — maps, saved queries, alert rules,
          collections, environments, and linked projects. Secrets never travel; the pack lists
          the references each teammate still has to re-link.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={async () => {
              try {
                const data = await exportTeamPack.mutateAsync();
                const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
                const url = URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = url;
                a.download = `swebkit-team-pack-${new Date().toISOString().slice(0, 10)}.json`;
                a.click();
                URL.revokeObjectURL(url);
                notify("success", "Team pack exported", a.download);
              } catch {
                setPackStatus("Export failed");
                notify("error", "Team pack export failed");
              }
            }}
            disabled={exportTeamPack.isPending}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="team-pack-export"
          >
            <Download className="h-4 w-4" />
            Export team pack
          </button>
          <button
            onClick={() => packInputRef.current?.click()}
            disabled={importTeamPack.isPending}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="team-pack-import"
          >
            <Upload className="h-4 w-4" />
            Import team pack
          </button>
          <input
            ref={packInputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            data-testid="team-pack-file-input"
            onChange={async (e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              try {
                const pack = JSON.parse(await file.text());
                await previewPack(pack, packStrategy);
              } catch {
                setPackStatus("Pack preview failed: invalid file");
                notify("error", "Pack preview failed", "Invalid pack file");
              } finally {
                e.target.value = "";
              }
            }}
          />
        </div>

        {pendingPack !== null && (
          <div className="mt-3 rounded-md border p-3" data-testid="team-pack-preview">
            {packReport ? (
              <>
                <p className="text-sm" data-testid="team-pack-report-counts">
                  {packReport.added} to add · {packReport.updated} to update · {packReport.skipped} to skip
                </p>
                {packReport.requiredCredentialRefs.length > 0 && (
                  <p className="mt-1 text-xs text-muted-foreground" data-testid="team-pack-credential-refs">
                    {packReport.requiredCredentialRefs.length} secret
                    {packReport.requiredCredentialRefs.length === 1 ? "" : "s"} to re-link after import:{" "}
                    {packReport.requiredCredentialRefs.map((r) => r.key).join(", ")}
                  </p>
                )}
                {packReport.warnings.map((w, i) => (
                  <p key={i} className="mt-1 text-xs text-warning" data-testid={`team-pack-warning-${i}`}>
                    {w}
                  </p>
                ))}
                {packReport.conflicts.map((c, i) => (
                  <p key={i} className="mt-1 text-xs text-muted-foreground" data-testid={`team-pack-conflict-${i}`}>
                    {c}
                  </p>
                ))}
                <div className="mt-2 flex items-center gap-3 text-sm">
                  <label className="flex items-center gap-1.5">
                    <input
                      type="radio"
                      name="team-pack-strategy"
                      checked={packStrategy === "merge"}
                      onChange={() => {
                        setPackStrategy("merge");
                        void previewPack(pendingPack, "merge");
                      }}
                      data-testid="team-pack-strategy-merge"
                    />
                    Merge with existing
                  </label>
                  <label className="flex items-center gap-1.5">
                    <input
                      type="radio"
                      name="team-pack-strategy"
                      checked={packStrategy === "replace"}
                      onChange={() => {
                        setPackStrategy("replace");
                        void previewPack(pendingPack, "replace");
                      }}
                      data-testid="team-pack-strategy-replace"
                    />
                    Replace existing sections
                  </label>
                </div>
                <div className="mt-2">
                  <ConfirmBar
                    message="Apply this team pack to your workspace?"
                    confirmLabel="Apply pack"
                    onConfirm={applyPack}
                    onCancel={() => {
                      setPendingPack(null);
                      setPackReport(null);
                    }}
                    testId="team-pack-apply"
                  />
                </div>
              </>
            ) : (
              <p className="text-sm text-muted-foreground">Analyzing pack…</p>
            )}
          </div>
        )}
        {packStatus && (
          <p className="mt-2 text-xs text-muted-foreground" data-testid="team-pack-status">{packStatus}</p>
        )}
      </section>

      <section data-testid="updates-section">
        <h2 className="mb-1 text-lg font-semibold">Updates</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          SwebKit checks for new releases but never installs them — you decide when to update.
        </p>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <span data-testid="current-version">
            Current version: {updateCheck.data?.currentVersion ?? "…"}
          </span>
          {updateCheck.data?.updateAvailable && (
            <span
              className="rounded-full bg-primary/15 px-2.5 py-0.5 text-xs font-medium text-primary"
              data-testid="update-available-badge"
            >
              Update available: v{updateCheck.data.latestVersion}
            </span>
          )}
          {updateCheck.data && !updateCheck.data.updateAvailable && updateCheck.data.latestVersion && (
            <span className="text-xs text-muted-foreground" data-testid="up-to-date">
              You're up to date
            </span>
          )}
          <button
            onClick={() => void updateCheck.refetch()}
            disabled={updateCheck.isFetching}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="check-for-updates"
          >
            <RefreshCw className={`h-4 w-4 ${updateCheck.isFetching ? "animate-spin" : ""}`} />
            Check for updates
          </button>
          {updateCheck.data?.updateAvailable && updateCheck.data.releaseUrl && (
            <button
              onClick={() => void openExternal(updateCheck.data!.releaseUrl!)}
              className="rounded-md border px-3 py-1.5 text-sm text-primary hover:bg-accent"
              data-testid="view-release"
            >
              View release
            </button>
          )}
        </div>
        {updateCheck.data?.source === "cache" && (
          <p className="mt-1 text-xs text-muted-foreground" data-testid="update-check-offline">
            Couldn't reach the update feed — showing the last known result.
          </p>
        )}
      </section>
    </div>
  );
}
