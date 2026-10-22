import {
    isDarkTheme,
    useSettingsStore,
    THEME_LABELS,
    type Theme,
} from "@/lib/stores/settings";
import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { type UserSettings } from "@/lib/types";

// SwebOS families (docs/features/active/swebos-skin.md): Aurora is the cool
// indigo family, Clay the warm sand/terracotta one, Duck the pond-blue and
// rubber-duck-yellow one — each in light and dark.
const THEMES: { id: Theme; desc: string }[] = [
    { id: "light", desc: "Cool light surface, indigo accent" },
    { id: "dark", desc: "Deep indigo surface, crisp cards" },
    { id: "clay", desc: "Warm sand frame, terracotta accent" },
    { id: "clay-dark", desc: "Dark earth frame, soft sheets" },
    { id: "duck", desc: "Pond-blue sidebar, rubber-duck yellow" },
    { id: "duck-dark", desc: "Night pond, bill yellow and teal" },
];

// Mini-preview swatches in each theme's own palette (rail = sidebar strip).
const PREVIEW: Record<
    Theme,
    { frame: string; rail: string; card: string; accent: string }
> = {
    light: {
        frame: "oklch(0.985 0.005 260)",
        rail: "oklch(1 0 0)",
        card: "oklch(1 0 0)",
        accent: "oklch(0.52 0.20 268)",
    },
    dark: {
        frame: "oklch(0.155 0.016 265)",
        rail: "oklch(0.19 0.018 265)",
        card: "oklch(0.19 0.018 265)",
        accent: "oklch(0.55 0.20 268)",
    },
    clay: {
        frame: "oklch(0.925 0.018 68)",
        rail: "oklch(0.925 0.018 68)",
        card: "oklch(0.99 0.006 75)",
        accent: "oklch(0.50 0.13 40)",
    },
    "clay-dark": {
        frame: "oklch(0.155 0.012 50)",
        rail: "oklch(0.155 0.012 50)",
        card: "oklch(0.23 0.014 50)",
        accent: "oklch(0.55 0.13 42)",
    },
    duck: {
        frame: "oklch(0.975 0.012 225)",
        rail: "oklch(0.33 0.11 252)",
        card: "oklch(1 0 0)",
        accent: "oklch(0.86 0.17 92)",
    },
    "duck-dark": {
        frame: "oklch(0.17 0.035 248)",
        rail: "oklch(0.14 0.03 250)",
        card: "oklch(0.21 0.04 248)",
        accent: "oklch(0.85 0.16 90)",
    },
};

function ThemePreview({ id }: { id: Theme }) {
    const { frame, rail, card, accent } = PREVIEW[id];
    const line = isDarkTheme(id) ? "bg-white/15" : "bg-black/10";

    return (
        <div
            className="mb-3 flex h-16 overflow-hidden rounded-lg"
            style={{ background: frame }}
        >
            <div className="h-full w-5 shrink-0" style={{ background: rail }} />
            <div className="flex flex-1 items-center justify-center">
                <div
                    className="flex h-9 w-20 flex-col gap-1.5 rounded-md border border-black/5 p-1.5 shadow-sm"
                    style={{ background: card }}
                >
                    <div
                        className="h-1.5 w-8 rounded-full"
                        style={{ background: accent }}
                    />
                    <div className={`h-1 w-12 rounded-full ${line}`} />
                    <div className={`h-1 w-9 rounded-full ${line}`} />
                </div>
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
                            <div className="text-sm font-medium">
                                {THEME_LABELS[t.id]}
                            </div>
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
