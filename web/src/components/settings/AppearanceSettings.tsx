import { useSettingsStore, type Theme } from "@/lib/stores/settings";
import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { FATHOM_UNLOCK_THRESHOLD, type UserSettings } from "@/lib/types";

const THEMES: { id: Theme; label: string; desc: string }[] = [
  { id: "dark", label: "Aurora Dark", desc: "Deep navy with indigo glows" },
  { id: "light", label: "Aurora Light", desc: "Soft lavender with vibrant accents" },
  { id: "fancy", label: "✨ Fancy ✨", desc: "Maximum vibes. Zero professionalism." },
  { id: "fathom-dark", label: "Fathom Abyss", desc: "Gold light sinking into dark water" },
  { id: "fathom-light", label: "Fathom Shallows", desc: "Sunlit surface, a hint of gold" },
];

export function AppearanceSettings() {
  const { theme, setTheme } = useSettingsStore();
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  const sessionCount = settings.sessionCount;
  const fathomAvailable = settings.fathomUnlocked || settings.fathomDeveloperOverride || false;
  const fathomProgress = Math.min(100, Math.round((sessionCount / FATHOM_UNLOCK_THRESHOLD) * 100));

  const selectTheme = (id: Theme) => {
    setTheme(id);
    updateSettings.mutate({ ...settings, theme: id });
  };

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
          {THEMES.map((t) => {
            const isFathom = t.id === "fathom-dark" || t.id === "fathom-light";
            const locked = isFathom && !fathomAvailable;

            return (
              <button
                key={t.id}
                onClick={() => !locked && selectTheme(t.id)}
                disabled={locked}
                className={`group relative overflow-hidden rounded-xl border-2 p-4 text-left transition-all duration-200 ${
                  locked
                    ? "cursor-default border-border opacity-60"
                    : theme === t.id
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
                        : t.id === "fathom-dark"
                          ? "bg-[linear-gradient(180deg,#16321f_0%,#0a1a22_40%,#050b12_100%)]"
                          : t.id === "fathom-light"
                            ? "bg-[linear-gradient(180deg,#fdf6e3_0%,#eaf5ee_40%,#cfe9e6_100%)]"
                            : "bg-[oklch(0.98_0.008_250)]"
                  }`}
                >
                  {t.id === "fancy" ? (
                    <span className="text-2xl drop-shadow-[0_0_6px_rgba(255,255,255,0.9)]">✨🌈💅</span>
                  ) : isFathom ? (
                    locked ? (
                      <FathomGauge percent={fathomProgress} />
                    ) : (
                      <div className="h-6 w-6 rounded-full bg-[#e0a940] shadow-[0_0_10px_rgba(224,169,64,0.6)]" />
                    )
                  ) : (
                    <div className="flex gap-1.5">
                      <div className="h-6 w-6 rounded-full bg-[oklch(0.65_0.24_265)]" />
                      <div className="h-6 w-6 rounded-full bg-[oklch(0.62_0.22_295)]" />
                      <div className="h-6 w-6 rounded-full bg-[oklch(0.58_0.20_200)]" />
                    </div>
                  )}
                </div>
                <div className="text-sm font-medium">{t.label}</div>
                <div className="text-xs text-muted-foreground">
                  {locked ? `${sessionCount} / ${FATHOM_UNLOCK_THRESHOLD} sessions` : t.desc}
                </div>
                {theme === t.id && !locked && (
                  <div className="absolute right-3 top-3 flex h-5 w-5 items-center justify-center rounded-full bg-primary text-primary-foreground">
                    <svg className="h-3 w-3" viewBox="0 0 12 12" fill="none">
                      <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  </div>
                )}
                {locked && (
                  <div className="absolute right-3 top-3 text-muted-foreground" aria-label="Locked" title={`Unlocks at ${FATHOM_UNLOCK_THRESHOLD} sessions`}>
                    🔒
                  </div>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {/* Font size */}
      <div>
        <label className="text-sm font-medium">Font Size</label>
        <select
          className="mt-2 rounded-lg border bg-card px-3 py-2 text-sm"
          data-testid="appearance-font-size"
          value={settings.fontSize ?? "medium"}
          onChange={(e) =>
            updateSettings.mutate({
              ...settings,
              fontSize: e.target.value as UserSettings["fontSize"],
            })
          }
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
          value={settings.density ?? "comfortable"}
          onChange={(e) =>
            updateSettings.mutate({
              ...settings,
              density: e.target.value as UserSettings["density"],
            })
          }
        >
          <option value="comfortable">Comfortable</option>
          <option value="compact">Compact</option>
        </select>
      </div>
    </div>
  );
}

function FathomGauge({ percent }: { percent: number }) {
  return (
    <div
      className="relative h-9 w-9 rounded-full"
      style={{ background: `conic-gradient(from -90deg, #e0a940 ${percent}%, rgba(255,255,255,0.15) ${percent}% 100%)` }}
    >
      <div className="absolute inset-[3px] flex items-center justify-center rounded-full bg-card text-[10px] font-semibold text-foreground">
        {percent}%
      </div>
    </div>
  );
}
