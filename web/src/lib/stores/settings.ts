import { create } from "zustand";

export type Theme = "light" | "dark" | "fancy";

const THEME_CYCLE: Theme[] = ["dark", "light", "fancy"];

interface SettingsState {
  theme: Theme;
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
}

function applyThemeClass(theme: Theme) {
  document.documentElement.classList.remove("dark", "fancy");
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
