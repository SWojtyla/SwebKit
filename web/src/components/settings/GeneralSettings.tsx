import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { useSettingsStore } from "@/lib/stores/settings";

export function GeneralSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();
  const { theme, toggleTheme } = useSettingsStore();

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  return (
    <div className="space-y-6">
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
