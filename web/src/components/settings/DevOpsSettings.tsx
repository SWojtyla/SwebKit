export function DevOpsSettings() {
  return (
    <div className="space-y-6" data-testid="devops-settings">
      <div>
        <h2 className="text-lg font-semibold">DevOps</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Configure Azure DevOps integration for pipelines and work items.
        </p>
      </div>

      <div className="space-y-4">
        <div>
          <label className="text-sm font-medium">Organization URL</label>
          <input
            type="text"
            placeholder="https://dev.azure.com/your-org"
            className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="devops-org-url"
          />
        </div>
        <div>
          <label className="text-sm font-medium">Personal Access Token</label>
          <input
            type="password"
            placeholder="Enter PAT..."
            className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="devops-pat"
          />
          <p className="mt-1 text-xs text-muted-foreground">
            Stored securely in local profile configuration.
          </p>
        </div>
        <div>
          <label className="text-sm font-medium">Default Project</label>
          <input
            type="text"
            placeholder="Project name"
            className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="devops-project"
          />
        </div>
        <button
          className="rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground hover:opacity-90"
          data-testid="devops-save-btn"
        >
          Save
        </button>
      </div>
    </div>
  );
}
