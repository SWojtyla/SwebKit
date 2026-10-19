import { useSettingsStore, type Theme } from "@/lib/stores/settings";
import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { type UserSettings } from "@/lib/types";

// SwebOS families (docs/features/active/swebos-skin.md): Aurora is the cool
// indigo family, Clay the warm sand/terracotta one — each in light and dark.
const THEMES: { id: Theme; label: string; desc: string }[] = [
    {
        id: "light",
        label: "Aurora Light",
        desc: "Cool light surface, indigo accent",
    },
    {
        id: "dark",
        label: "Aurora Dark",
        desc: "Deep indigo surface, crisp cards",
    },
    {
        id: "clay",
        label: "Clay Light",
        desc: "Warm sand frame, terracotta accent",
    },
    {
        id: "clay-dark",
        label: "Clay Dark",
        desc: "Dark earth frame, soft sheets",
    },
];

function ThemePreview({ id }: { id: Theme }) {
    const isDark = id === "dark" || id === "clay-dark";
    const isClay = id === "clay" || id === "clay-dark";
    // Mini frame + card preview in the family's own palette.
    const frame = isClay
        ? isDark
            ? "bg-[oklch(0.155_0.012_50)]"
            : "bg-[oklch(0.925_0.018_68)]"
        : isDark
          ? "bg-[oklch(0.155_0.016_265)]"
          : "bg-[oklch(0.985_0.005_260)]";
    const card = isClay
        ? isDark
            ? "bg-[oklch(0.23_0.014_50)]"
            : "bg-[oklch(0.99_0.006_75)]"
        : isDark
          ? "bg-[oklch(0.19_0.018_265)]"
          : "bg-[oklch(1_0_0)]";
    const accent = isClay
        ? isDark
            ? "bg-[oklch(0.55_0.13_42)]"
            : "bg-[oklch(0.50_0.13_40)]"
        : isDark
          ? "bg-[oklch(0.55_0.20_268)]"
          : "bg-[oklch(0.52_0.20_268)]";
    const line = isDark ? "bg-white/15" : "bg-black/10";

    return (
        <div
            className={`mb-3 flex h-16 items-center justify-center rounded-lg ${frame}`}
        >
            <div
                className={`flex h-9 w-20 flex-col gap-1.5 rounded-md border border-black/5 p-1.5 shadow-sm ${card}`}
            >
                <div className={`h-1.5 w-8 rounded-full ${accent}`} />
                <div className={`h-1 w-12 rounded-full ${line}`} />
                <div className={`h-1 w-9 rounded-full ${line}`} />
            </div>
        </div>
    );
}

export function AppearanceSettings() {
    const { theme, setTheme } = useSettingsStore();
    const { data: settings, isLoading } = useUserSettings();
    const updateSettings = useUpdateUserSettings();

    if (isLoading || !settings) {
        return <div className="text-muted-foreground">Loading...</div>;
    }

    const selectTheme = (id: Theme) => {
        setTheme(id);
        updateSettings.mutate((prev) => ({ ...prev, theme: id }));
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
                <div className="mt-3 grid grid-cols-2 gap-3">
                    {THEMES.map((t) => (
                        <button
                            key={t.id}
                            onClick={() => selectTheme(t.id)}
                            className={`group relative overflow-hidden rounded-xl border-2 p-4 text-left transition-all duration-200 ${
                                theme === t.id
                                    ? "border-primary glow"
                                    : "border-border hover:border-primary/40"
                            }`}
                            data-testid={`appearance-theme-${t.id}`}
                        >
                            <ThemePreview id={t.id} />
                            <div className="text-sm font-medium">{t.label}</div>
                            <div className="text-xs text-muted-foreground">
                                {t.desc}
                            </div>
                            {theme === t.id && (
                                <div className="absolute right-3 top-3 flex h-5 w-5 items-center justify-center rounded-full bg-primary text-primary-foreground">
                                    <svg
                                        className="h-3 w-3"
                                        viewBox="0 0 12 12"
                                        fill="none"
                                    >
                                        <path
                                            d="M2.5 6L5 8.5L9.5 3.5"
                                            stroke="currentColor"
                                            strokeWidth="2"
                                            strokeLinecap="round"
                                            strokeLinejoin="round"
                                        />
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
                    value={settings.fontSize ?? "medium"}
                    onChange={(e) => {
                        const fontSize = e.target
                            .value as UserSettings["fontSize"];
                        updateSettings.mutate((prev) => ({
                            ...prev,
                            fontSize,
                        }));
                    }}
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
                    onChange={(e) => {
                        const density = e.target
                            .value as UserSettings["density"];
                        updateSettings.mutate((prev) => ({ ...prev, density }));
                    }}
                >
                    <option value="comfortable">Comfortable</option>
                    <option value="compact">Compact</option>
                </select>
            </div>
        </div>
    );
}
