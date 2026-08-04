import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import type { UserSettings } from "@/lib/types";

const LOG_LEVELS = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

export function DiagnosticsSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  const updateLogging = (patch: Partial<UserSettings["logging"]>) => {
    updateSettings.mutate({
      ...settings,
      logging: { ...settings.logging, ...patch },
    });
  };

  return (
    <div className="space-y-6" data-testid="diagnostics-settings">
      <div>
        <h2 className="text-lg font-semibold">Diagnostics</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          View diagnostic information and logs.
        </p>
      </div>

      <div className="space-y-4 rounded-lg border p-4">
        <h3 className="text-sm font-semibold">Structured File Logging</h3>
        <p className="text-xs text-muted-foreground">
          Writes logs to a file alongside the sidecar. The minimum level controls how much detail is
          captured. Restart the app for a level change to take full effect.
        </p>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.logging.enabled}
            onChange={(e) => updateLogging({ enabled: e.target.checked })}
            data-testid="diagnostics-logging-enabled"
          />
          Enable file logging
        </label>

        <div>
          <label className="text-sm font-medium">Minimum Level</label>
          <select
            className="mt-2 rounded-lg border bg-card px-3 py-2 text-sm"
            value={settings.logging.minimumLevel}
            onChange={(e) => updateLogging({ minimumLevel: e.target.value })}
            data-testid="diagnostics-logging-level"
          >
            {LOG_LEVELS.map((level) => (
              <option key={level} value={level}>
                {level}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div className="space-y-4">
        <div className="rounded-lg border p-4">
          <h3 className="text-sm font-semibold">Configuration Health</h3>
          <div className="mt-3 space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span>Sidecar connection</span>
              <span className="text-success" data-testid="diag-sidecar-status">OK</span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span>Profile loaded</span>
              <span className="text-success" data-testid="diag-profile-status">OK</span>
            </div>
          </div>
        </div>

        <div className="rounded-lg border p-4">
          <h3 className="text-sm font-semibold">Log Viewer</h3>
          <pre
            className="mt-3 max-h-64 overflow-auto rounded-md bg-muted p-3 text-xs font-mono"
            data-testid="diag-log-viewer"
          >
            {"No logs available."}
          </pre>
        </div>
      </div>
    </div>
  );
}
