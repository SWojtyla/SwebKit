// ── Grid keyboard navigation (ux-power-pack §4) ──────────────────────────────
//
// Pure logic for `useGridKeyboardNav` — kept hook-free so index clamping and
// the event-target guards are unit-testable without a DOM.
//
// Row contract: grids mark each navigable row element with
// `data-grid-nav-row="<index>"` and make it focusable (`tabIndex`). The hook
// resolves focus via that attribute after scrolling virtualized rows into view.

export type GridNavAction =
    | "up"
    | "down"
    | "first"
    | "last"
    | "inspect"
    | "toggle-select"
    | "filter"
    | "collapse"
    | "expand";

export const GRID_NAV_ROW_ATTR = "data-grid-nav-row";

/**
 * Targets that consume keystrokes themselves — typing `j` into a filter input
 * must never move row focus. Covers plain inputs AND the CodeMirror editor the
 * SQL page mounts (`.cm-editor`), per the keyboard-guard pitfall doc.
 */
export const GRID_NAV_EDITABLE_SELECTOR =
    "input,textarea,select,[contenteditable],.cm-editor";

/**
 * Elements that natively activate on Enter/Space — a row's expand chevron, a
 * link. When focus sits on one, those keys must reach the element (click),
 * not the grid's inspect/toggle-select action.
 */
export const GRID_NAV_ACTIVATABLE_SELECTOR = "button,a[href]";

export function isEditableEventTarget(target: unknown): boolean {
    return closestWithin(target, GRID_NAV_EDITABLE_SELECTOR) != null;
}

/** True when Enter/Space on this target should activate the element itself
 *  (native button/link behavior) rather than a grid row action. */
export function isActivatableEventTarget(target: unknown): boolean {
    return closestWithin(target, GRID_NAV_ACTIVATABLE_SELECTOR) != null;
}

function closestWithin(target: unknown, selector: string): unknown {
    if (target === null || typeof target !== "object") return null;
    const el = target as { closest?: (s: string) => unknown };
    return typeof el.closest === "function" ? el.closest(selector) : null;
}

/**
 * Maps a keydown `key` to a grid action, or null for keys the grid ignores.
 * `tree` gates `ArrowLeft`/`ArrowRight` — grids without expand/collapse keep
 * those keys free for horizontal scrolling; `h`/`l` map unconditionally so
 * tree grids get the vim bindings too.
 */
export function resolveGridNavKey(
    key: string,
    options?: { tree?: boolean },
): GridNavAction | null {
    switch (key) {
        case "j":
        case "ArrowDown":
            return "down";
        case "k":
        case "ArrowUp":
            return "up";
        case "g":
            return "first";
        case "G":
            return "last";
        case "e":
        case "Enter":
            return "inspect";
        case " ":
            return "toggle-select";
        case "/":
            return "filter";
        case "h":
            return "collapse";
        case "l":
            return "expand";
        case "ArrowLeft":
            return options?.tree ? "collapse" : null;
        case "ArrowRight":
            return options?.tree ? "expand" : null;
        default:
            return null;
    }
}

/** Clamps a row index into `[0, count)`; null when the grid is empty. */
export function clampGridIndex(
    index: number,
    count: number,
): number | null {
    if (count <= 0) return null;
    return Math.min(Math.max(index, 0), count - 1);
}
