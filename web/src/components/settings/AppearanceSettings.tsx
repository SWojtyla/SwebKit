import { useSettingsStore } from "@/lib/stores/settings";

export function AppearanceSettings() {
  const { theme, setTheme } = useSettingsStore();

  const themes: { id: "dark" | "light" | "fancy"; label: string; desc: string }[] = [
    { id: "dark", label: "Aurora Dark", desc: "Deep navy with indigo glows" },
    { id: "light", label: "Aurora Light", desc: "Soft lavender with vibrant accents" },
    { id: "fancy", label: "✨ Fancy ✨", desc: "Maximum vibes. Zero professionalism." },
  ];

  return (
    <div className="space-y-6" data-testid="appearance-settings">
      <div>
        <h2 className="text-lg font-semibold">Appearance</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Customize the look and feel of the application.
        </p>
      </div>

      {/* Theme cards */}
      <div>
        <label className="text-sm font-medium">Theme</label>
        <div className="mt-3 grid grid-cols-3 gap-3">
          {themes.map((t) => (
            <button
              key={t.id}
              onClick={() => setTheme(t.id)}
              className={`group relative overflow-hidden rounded-xl border-2 p-4 text-left transition-all duration-200 ${
                theme === t.id
                  ? "border-primary glow"
                  : "border-border hover:border-primary/40"
              }`}
              data-testid={`appearance-theme-${t.id}`}
            >
              {/* Mini preview */}
              <div
                className={`mb-3 flex h-16 items-center justify-center rounded-lg ${
                  t.id === "dark"
                    ? "bg-[oklch(0.16_0.018_260)]"
                    : t.id === "fancy"
                      ? "bg-[linear-gradient(120deg,#ff00cc,#3333ff,#00e5ff,#ff00cc)] bg-[length:300%_300%] animate-[fancy-rainbow_3s_ease_infinite]"
                      : "bg-[oklch(0.98_0.008_250)]"
                }`}
              >
                {t.id === "fancy" ? (
                  <span className="text-2xl drop-shadow-[0_0_6px_rgba(255,255,255,0.9)]">✨🌈💅</span>
                ) : (
                  <div className="flex gap-1.5">
                    <div className="h-6 w-6 rounded-full bg-[oklch(0.65_0.24_265)]" />
                    <div className="h-6 w-6 rounded-full bg-[oklch(0.62_0.22_295)]" />
                    <div className="h-6 w-6 rounded-full bg-[oklch(0.58_0.20_200)]" />
                  </div>
                )}
              </div>
              <div className="text-sm font-medium">{t.label}</div>
              <div className="text-xs text-muted-foreground">{t.desc}</div>
              {theme === t.id && (
                <div className="absolute right-3 top-3 flex h-5 w-5 items-center justify-center rounded-full bg-primary text-primary-foreground">
                  <svg className="h-3 w-3" viewBox="0 0 12 12" fill="none">
                    <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </div>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* Font size */}
      <div>
        <label className="text-sm font-medium">Font Size</label>
        <select
          className="mt-2 rounded-lg border bg-card px-3 py-2 text-sm"
          data-testid="appearance-font-size"
        >
          <option value="small">Small</option>
          <option value="medium">Medium</option>
          <option value="large">Large</option>
        </select>
      </div>

      {/* Density */}
      <div>
        <label className="text-sm font-medium">Density</label>
        <select
          className="mt-2 rounded-lg border bg-card px-3 py-2 text-sm"
          data-testid="appearance-density"
        >
          <option value="comfortable">Comfortable</option>
          <option value="compact">Compact</option>
        </select>
      </div>
    </div>
  );
}
