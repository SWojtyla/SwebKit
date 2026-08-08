/// Persisted split-panel widths, following the same plain-localStorage shape as
/// `sb-preferences.ts` — load/save functions, defaults on any failure, no store.

const STORAGE_PREFIX = "panel-widths";

/**
 * Bumped when the meaning of stored widths changes. The API Client moved from
 * fixed pixel widths `[260, 540, flex]` to `[300, 1fr, 1fr]`; restoring a user's
 * old dragged widths would put them straight back into the cramped proportions
 * this change exists to fix, so a version mismatch discards rather than migrates.
 * See docs/features/active/api-client-ux-overhaul/decisions.md DEC-6.
 */
export const PANEL_WIDTHS_VERSION = 2;

interface StoredPanelWidths {
  version: number;
  widths: (number | null)[];
}

function getKey(key: string): string {
  return `${STORAGE_PREFIX}:${key}`;
}

/**
 * Returns stored widths, or `null` when nothing usable is stored so the caller
 * applies its own defaults. A panel-count mismatch counts as unusable: the
 * layout has changed shape since the widths were written.
 */
export function loadPanelWidths(key: string, expectedCount: number): (number | null)[] | null {
  try {
    const raw = localStorage.getItem(getKey(key));
    if (!raw) return null;

    const parsed = JSON.parse(raw) as StoredPanelWidths;
    if (parsed?.version !== PANEL_WIDTHS_VERSION) return null;
    if (!Array.isArray(parsed.widths) || parsed.widths.length !== expectedCount) return null;
    if (!parsed.widths.every((w) => w === null || (typeof w === "number" && Number.isFinite(w) && w > 0))) {
      return null;
    }
    return parsed.widths;
  } catch {
    return null;
  }
}

export function savePanelWidths(key: string, widths: (number | null)[]): void {
  try {
    const payload: StoredPanelWidths = { version: PANEL_WIDTHS_VERSION, widths };
    localStorage.setItem(getKey(key), JSON.stringify(payload));
  } catch {
    // Quota exceeded or storage unavailable (private mode) — a lost layout
    // preference is not worth surfacing.
  }
}

/** Generic JSON view preference (string, boolean, or plain object). */
export function loadViewPreference<T extends string | boolean | object>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(`view-pref:${key}`);
    if (raw === null) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

export function saveViewPreference(key: string, value: string | boolean | object): void {
  try {
    localStorage.setItem(`view-pref:${key}`, JSON.stringify(value));
  } catch {
    // ignore storage errors
  }
}
