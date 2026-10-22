import { create } from "zustand";
import { loadViewPreference, saveViewPreference } from "./panel-preferences";

// Local mirrors of the sidecar-held appearance, read by public/boot-appearance.js before first
// paint so a launch starts on the user's theme instead of flashing the default first.
export const BOOT_THEME_PREF = "boot-theme";
export const BOOT_FONT_SIZE_PREF = "boot-font-size";

// SwebOS families: Aurora light/dark reuse the historic `light`/`dark` ids so
// existing user-settings.json values keep meaning; Clay is the second family.
export type Theme = "light" | "dark" | "clay" | "clay-dark";

// The quick-cycle button/shortcut rotates through every available theme.
const THEME_CYCLE: Theme[] = ["dark", "light", "clay-dark", "clay"];

// DOM contract: aurora-light → :root, aurora-dark → .dark,
// clay-light → .clay, clay-dark → .clay.dark.
const THEME_CLASSES = ["dark", "clay", "clay-dark"];

const ALL_THEMES: Theme[] = ["light", "dark", "clay", "clay-dark"];

// Retired palettes map to the nearest Aurora mode — a stale user-settings.json
// value should land somewhere sensible, not on the default.
const LEGACY_THEME_MAP: Record<string, Theme> = {
    fancy: "dark",
    "fathom-dark": "dark",
    "letterpress-dark": "dark",
    "cascade-dark": "dark",
    "fathom-light": "light",
    "letterpress-light": "light",
    "cascade-light": "light",
};

// Guards the value coming back from user-settings.json — it's a free-form string on that side,
// and could be empty (never saved before) or stale (a theme id that no longer exists).
export function isTheme(value: string | null | undefined): value is Theme {
    return !!value && (ALL_THEMES as string[]).includes(value);
}

// Resolves a persisted theme string to a live Theme: current ids pass through,
// retired ids migrate to their nearest Aurora mode, unknown/empty → null.
export function migrateTheme(value: string | null | undefined): Theme | null {
    if (!value) return null;
    if (isTheme(value)) return value;
    return LEGACY_THEME_MAP[value] ?? null;
}

interface SettingsState {
    theme: Theme;
    toggleTheme: () => void;
    setTheme: (theme: Theme) => void;
}

function applyThemeClass(theme: Theme) {
    document.documentElement.classList.remove(...THEME_CLASSES);
    if (theme === "dark") {
        document.documentElement.classList.add("dark");
    } else if (theme === "clay") {
        document.documentElement.classList.add("clay");
    } else if (theme === "clay-dark") {
        document.documentElement.classList.add("clay", "dark");
    }
    saveViewPreference(BOOT_THEME_PREF, theme);
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
    // Starts from the boot mirror, which boot-appearance.js has already applied to the DOM.
    theme: migrateTheme(loadViewPreference(BOOT_THEME_PREF, "")) ?? "dark",
    toggleTheme: () => {
        const next =
            THEME_CYCLE[
                (THEME_CYCLE.indexOf(get().theme) + 1) % THEME_CYCLE.length
            ];
        set({ theme: next });
        applyThemeClass(next);
    },
    setTheme: (theme) => {
        set({ theme });
        applyThemeClass(theme);
    },
}));
