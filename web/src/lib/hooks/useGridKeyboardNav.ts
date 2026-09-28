import {
    useCallback,
    useEffect,
    useRef,
    useState,
    type RefObject,
} from "react";
import {
    clampGridIndex,
    GRID_NAV_ROW_ATTR,
    isActivatableEventTarget,
    isEditableEventTarget,
    resolveGridNavKey,
} from "../grid-nav";

// ── useGridKeyboardNav (ux-power-pack §4) ────────────────────────────────────
//
// Window-level keydown listener implementing the shared vim-style row
// navigation contract:
//   j / ArrowDown  → next row          g → first row
//   k / ArrowUp    → previous row      G → last row
//   e / Enter      → inspect the focused row (onInspect)
//   Space          → toggle-select the focused row (onToggleSelect, opt-in)
//   /              → focus the grid's filter field (getFilterInput, opt-in)
//   h / ArrowLeft  → collapse / move to parent (tree grids, onCollapse)
//   l / ArrowRight → expand / move to first child (tree grids, onExpand)
//
// Guard discipline (see docs/pitfalls/react-frontend.md's keyboard section):
// keys are ignored when the event target sits inside
// `input,textarea,select,[contenteditable],.cm-editor`, and Enter/Space defer
// to focused buttons/links inside the grid (native activation). Plain-letter
// keys only act when focus is inside the grid's `containerRef` or inert
// (body/documentElement) — never hijacked from other focusable UI.
//
// Integration contract: mark each row with `data-grid-nav-row="<index>"` and
// give it `tabIndex` (the hook focuses it after scrolling). Virtualized grids
// pass `scrollToIndex` (TanStack Virtual's `virtualizer.scrollToIndex`) so a
// focused row is mounted before `.focus()` runs — the focus lands on the next
// frame, after React commits the newly mounted row.

export interface UseGridKeyboardNavOptions {
    /** Element containing the rows — the scope check for keydown targets. */
    containerRef: RefObject<HTMLElement | null>;
    itemCount: number;
    /** When false, no listener is attached (e.g. a different tab is active). */
    enabled?: boolean;
    /** Resets the focused index when it changes (entity/cache/result swap). */
    resetKey?: unknown;
    onInspect?: (index: number) => void;
    onToggleSelect?: (index: number) => void;
    /** Tree grids: return true when the key was handled (collapsed a namespace
     *  or moved focus to the parent); false lets the key fall through. */
    onCollapse?: (index: number) => boolean;
    onExpand?: (index: number) => boolean;
    /** `/` focuses this element — a lazy accessor rather than a RefObject so
     *  grids can point at inputs they don't own (e.g. inside a toolbar
     *  component) without threading ref props. */
    getFilterInput?: () => HTMLElement | null;
    scrollToIndex?: (index: number) => void;
}

export interface GridKeyboardNav {
    focusedIndex: number | null;
    /** Sets focus without DOM focus — for click/focus sync in row markup. */
    setFocusedIndex: (index: number | null) => void;
    /** Sets focus AND focuses the row element (scrolling it into view). */
    focusIndex: (index: number) => void;
}

export function useGridKeyboardNav(
    options: UseGridKeyboardNavOptions,
): GridKeyboardNav {
    const {
        containerRef,
        itemCount,
        enabled = true,
        resetKey,
        onInspect,
        onToggleSelect,
        onCollapse,
        onExpand,
        getFilterInput,
        scrollToIndex,
    } = options;

    const [focusedIndex, setFocusedIndex] = useState<number | null>(null);

    // Reset on scope change (entity switch, new result set) — the
    // set-state-during-render pattern, same as the pages' own selection resets.
    const [prevResetKey, setPrevResetKey] = useState(resetKey);
    if (prevResetKey !== resetKey) {
        setPrevResetKey(resetKey);
        setFocusedIndex(null);
    }

    // Clamp when the row count shrinks under the focus — filtering a list
    // can't leave focus pointing past the last visible row.
    const clamped = focusedIndex === null ? null : clampGridIndex(focusedIndex, itemCount);
    if (clamped !== focusedIndex) setFocusedIndex(clamped);

    // Latest-values refs: the window listener reads through them, so moving row
    // focus doesn't churn listener attach/detach on every index change. Synced
    // in an effect — writing refs during render is flagged by react-hooks/refs.
    const latest = useRef({
        itemCount,
        onInspect,
        onToggleSelect,
        onCollapse,
        onExpand,
        getFilterInput,
        scrollToIndex,
    });
    const focusedRef = useRef(focusedIndex);
    useEffect(() => {
        latest.current = {
            itemCount,
            onInspect,
            onToggleSelect,
            onCollapse,
            onExpand,
            getFilterInput,
            scrollToIndex,
        };
        focusedRef.current = focusedIndex;
    });

    const focusIndex = useCallback(
        (index: number) => {
            const clampedIndex = clampGridIndex(index, latest.current.itemCount);
            if (clampedIndex === null) return;
            setFocusedIndex(clampedIndex);
            focusedRef.current = clampedIndex;
            // Mount the row first (virtualized grids), then move DOM focus on
            // the next frame — focusing an unmounted row is a no-op.
            latest.current.scrollToIndex?.(clampedIndex);
            requestAnimationFrame(() => {
                const row = containerRef.current?.querySelector<HTMLElement>(
                    `[${GRID_NAV_ROW_ATTR}="${clampedIndex}"]`,
                );
                row?.focus();
            });
        },
        [containerRef],
    );

    useEffect(() => {
        if (!enabled) return;
        const handler = (e: KeyboardEvent) => {
            if (e.ctrlKey || e.metaKey || e.altKey) return;
            const target = e.target;
            if (isEditableEventTarget(target)) return;
            const container = containerRef.current;
            const inGrid =
                container !== null &&
                container !== undefined &&
                target instanceof Node &&
                container.contains(target);
            const inert =
                target === document.body ||
                target === document.documentElement ||
                target === null;
            if (!inGrid && !inert) return;

            const { itemCount: count } = latest.current;
            if (count <= 0) return;
            const focused = focusedRef.current;
            const action = resolveGridNavKey(e.key, {
                tree: !!(latest.current.onCollapse || latest.current.onExpand),
            });
            if (!action) return;

            const move = (index: number) => {
                e.preventDefault();
                focusIndex(index);
            };

            switch (action) {
                case "down":
                    move(focused === null ? 0 : focused + 1);
                    break;
                case "up":
                    move(focused === null ? count - 1 : focused - 1);
                    break;
                case "first":
                    move(0);
                    break;
                case "last":
                    move(count - 1);
                    break;
                case "filter": {
                    const input = latest.current.getFilterInput?.();
                    if (input) {
                        e.preventDefault();
                        input.focus();
                    }
                    break;
                }
                case "inspect":
                    // Enter on a focused button/link inside the grid must
                    // activate it, not inspect the row under it.
                    if (isActivatableEventTarget(target)) return;
                    if (focused !== null) {
                        e.preventDefault();
                        latest.current.onInspect?.(focused);
                    }
                    break;
                case "toggle-select":
                    if (isActivatableEventTarget(target)) return;
                    if (focused !== null && latest.current.onToggleSelect) {
                        e.preventDefault();
                        latest.current.onToggleSelect(focused);
                    }
                    break;
                case "collapse":
                    if (
                        focused !== null &&
                        latest.current.onCollapse?.(focused)
                    )
                        e.preventDefault();
                    break;
                case "expand":
                    if (
                        focused !== null &&
                        latest.current.onExpand?.(focused)
                    )
                        e.preventDefault();
                    break;
            }
        };
        window.addEventListener("keydown", handler);
        return () => window.removeEventListener("keydown", handler);
    }, [enabled, containerRef, focusIndex]);

    return { focusedIndex, setFocusedIndex, focusIndex };
}
