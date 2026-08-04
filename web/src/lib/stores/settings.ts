import { create } from "zustand";

export type Theme = "light" | "dark" | "fancy" | "fathom-dark" | "fathom-light";

// Fathom is unlockable (Settings → Appearance gates it on usage) and deliberately left out of the
// quick-cycle button/shortcut — that control has no way to know the unlock state from here, so it
// only ever cycles the three themes everyone always has.
const THEME_CYCLE: Theme[] = ["dark", "light", "fancy"];

const THEME_CLASSES: Theme[] = ["dark", "fancy", "fathom-dark", "fathom-light"];

const ALL_THEMES: Theme[] = ["light", "dark", "fancy", "fathom-dark", "fathom-light"];

// Guards the value coming back from user-settings.json — it's a free-form string on that side,
// and could be empty (never saved before) or stale (a theme id that no longer exists).
export function isTheme(value: string | null | undefined): value is Theme {
  return !!value && (ALL_THEMES as string[]).includes(value);
}

interface SettingsState {
  theme: Theme;
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
}

function applyThemeClass(theme: Theme) {
  document.documentElement.classList.remove(...THEME_CLASSES);
  if (theme !== "light") {
    document.documentElement.classList.add(theme);
  }
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  theme: "dark",
  toggleTheme: () => {
    const next = THEME_CYCLE[(THEME_CYCLE.indexOf(get().theme) + 1) % THEME_CYCLE.length];
    set({ theme: next });
    applyThemeClass(next);
  },
  setTheme: (theme) => {
    set({ theme });
    applyThemeClass(theme);
  },
}));
