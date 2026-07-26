export function AppearanceSettings() {
  return (
    <div className="space-y-6" data-testid="appearance-settings">
      <div>
        <h2 className="text-lg font-semibold">Appearance</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Customize the look and feel of the application.
        </p>
      </div>

      <div className="space-y-4">
        <div>
          <label className="text-sm font-medium">Theme</label>
          <div className="mt-2 flex gap-3">
            <button
              className="rounded-md border-2 border-primary bg-card px-4 py-2 text-sm"
              data-testid="appearance-theme-dark"
            >
              Dark
            </button>
            <button
              className="rounded-md border bg-card px-4 py-2 text-sm hover:border-primary"
              data-testid="appearance-theme-light"
            >
              Light
            </button>
            <button
              className="rounded-md border bg-card px-4 py-2 text-sm hover:border-primary"
              data-testid="appearance-theme-system"
            >
              System
            </button>
          </div>
        </div>

        <div>
          <label className="text-sm font-medium">Font Size</label>
          <select
            className="mt-1 rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="appearance-font-size"
          >
            <option value="small">Small</option>
            <option value="medium">Medium</option>
            <option value="large">Large</option>
          </select>
        </div>

        <div>
          <label className="text-sm font-medium">Density</label>
          <select
            className="mt-1 rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="appearance-density"
          >
            <option value="comfortable">Comfortable</option>
            <option value="compact">Compact</option>
          </select>
        </div>
      </div>
    </div>
  );
}
