export function DiagnosticsSettings() {
  return (
    <div className="space-y-6" data-testid="diagnostics-settings">
      <div>
        <h2 className="text-lg font-semibold">Diagnostics</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          View diagnostic information and logs.
        </p>
      </div>

      <div className="space-y-4">
        <div className="rounded-lg border p-4">
          <h3 className="text-sm font-semibold">Configuration Health</h3>
          <div className="mt-3 space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span>Sidecar connection</span>
              <span className="text-green-500" data-testid="diag-sidecar-status">OK</span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span>Profile loaded</span>
              <span className="text-green-500" data-testid="diag-profile-status">OK</span>
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
