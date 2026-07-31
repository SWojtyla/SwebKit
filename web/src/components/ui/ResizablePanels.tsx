import { useState, useRef, useCallback, useEffect, type ReactNode, Fragment } from "react";
import { loadPanelWidths, savePanelWidths } from "@/lib/stores/panel-preferences";
import {
  resolvePanelWidths,
  resizePair,
  parseFraction,
  type PanelWidthSpec,
} from "./resizable-widths";

/** Width of a single resizer handle in px; must match the `w-1.5` class below. */
const HANDLE_WIDTH = 6;

/** Keyboard resize step, and the larger step when Shift is held. */
const KEY_STEP = 16;
const KEY_STEP_LARGE = 64;

interface ResizablePanelsProps {
  children: ReactNode[];
  /**
   * Per-panel width: a number/`"260px"`/`"25%"` for fixed, `"Nfr"` for a share of
   * the leftover space, `null` for `"1fr"`.
   */
  initialWidths?: PanelWidthSpec[];
  minWidths?: number[];
  /** When set, dragged widths are persisted under this key and restored on mount. */
  storageKey?: string;
  /** Accessible names for the panels, used to label the resizers. */
  panelLabels?: string[];
  className?: string;
}

export function ResizablePanels({
  children,
  initialWidths,
  minWidths = [180, 280, 280],
  storageKey,
  panelLabels,
  className = "",
}: ResizablePanelsProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const validChildren = Array.isArray(children) ? children : [children];
  const panelCount = validChildren.length;

  const [widths, setWidths] = useState<number[]>([]);
  const widthsRef = useRef(widths);
  widthsRef.current = widths;

  const activeRef = useRef<number | null>(null);
  const startXRef = useRef(0);
  const startWidthsRef = useRef<number[]>([]);
  /** Defaults captured on mount, so double-click can restore them. */
  const defaultWidthsRef = useRef<number[]>([]);
  /** False until the first resolved width lands, so hydration is not persisted. */
  const hydratedRef = useRef(false);

  const handlesWidth = Math.max(0, panelCount - 1) * HANDLE_WIDTH;

  const computeDefaults = useCallback(
    (containerWidth: number) =>
      resolvePanelWidths({
        specs: initialWidths ?? [],
        containerWidth,
        minWidths,
        handlesWidth,
      }),
    // `initialWidths`/`minWidths` are literals at every call site; depending on
    // their identity would recompute defaults on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [handlesWidth],
  );

  // Resolve widths once the container has a measurable width.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const containerWidth = container.clientWidth;
    if (containerWidth === 0) return;

    const defaults = computeDefaults(containerWidth);
    defaultWidthsRef.current = defaults;

    const stored = storageKey ? loadPanelWidths(storageKey, panelCount) : null;
    if (stored) {
      // Stored widths were measured in a possibly different window size; scale
      // them so a restored layout still fills the container.
      const storedTotal = stored.reduce<number>((sum, w) => sum + (w ?? 0), 0);
      const available = containerWidth - handlesWidth;
      if (storedTotal > 0 && available > 0) {
        const scale = available / storedTotal;
        setWidths(
          stored.map((w, i) => Math.max(minWidths[i] ?? 0, Math.round((w ?? 0) * scale))),
        );
        return;
      }
    }
    setWidths(defaults);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [computeDefaults, panelCount, storageKey]);

  // Keep the layout filling the container when the window resizes. Fixed panels
  // hold their width; fractional panels absorb the change.
  useEffect(() => {
    const container = containerRef.current;
    if (!container || typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver(() => {
      const containerWidth = container.clientWidth;
      if (containerWidth === 0) return;
      const current = widthsRef.current;
      if (current.length !== panelCount) return;

      const available = containerWidth - handlesWidth;
      const total = current.reduce((sum, w) => sum + w, 0);
      const drift = available - total;
      if (Math.abs(drift) < 1) return;

      const flexIndexes = (initialWidths ?? [])
        .map((spec, i) => (parseFraction(spec) !== null ? i : -1))
        .filter((i) => i >= 0);
      const targets = flexIndexes.length > 0 ? flexIndexes : current.map((_, i) => i);

      setWidths(
        current.map((w, i) => {
          if (!targets.includes(i)) return w;
          return Math.max(minWidths[i] ?? 0, Math.round(w + drift / targets.length));
        }),
      );
    });

    observer.observe(container);
    return () => observer.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [handlesWidth, panelCount]);

  const persist = useCallback(
    (next: number[]) => {
      if (storageKey) savePanelWidths(storageKey, next);
    },
    [storageKey],
  );

  /**
   * Persists width changes that did not come from a drag (keyboard, double-click,
   * window resize). Drags persist once on pointerup instead, so a 60fps pointermove
   * stream does not hammer localStorage. Skips the first resolved value so merely
   * mounting does not freeze the defaults.
   */
  useEffect(() => {
    if (widths.length === 0) return;
    if (!hydratedRef.current) {
      hydratedRef.current = true;
      return;
    }
    if (activeRef.current !== null) return;
    persist(widths);
  }, [widths, persist]);

  const applyPair = useCallback(
    (leftIndex: number, delta: number, baseWidths: number[]) => {
      const rightIndex = leftIndex + 1;
      if (rightIndex >= baseWidths.length) return;

      const [left, right] = resizePair(
        baseWidths[leftIndex],
        baseWidths[rightIndex],
        delta,
        minWidths[leftIndex] ?? 0,
        minWidths[rightIndex] ?? 0,
      );

      setWidths(() => {
        const next = [...baseWidths];
        next[leftIndex] = left;
        next[rightIndex] = right;
        return next;
      });
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  const handlePointerDown = useCallback((index: number, e: React.PointerEvent) => {
    e.preventDefault();
    (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
    activeRef.current = index;
    startXRef.current = e.clientX;
    startWidthsRef.current = [...widthsRef.current];
    // Without this, dragging selects the response body text under the cursor.
    document.body.style.userSelect = "none";
    document.body.style.cursor = "col-resize";
  }, []);

  useEffect(() => {
    const move = (e: PointerEvent) => {
      if (activeRef.current === null) return;
      applyPair(activeRef.current, e.clientX - startXRef.current, startWidthsRef.current);
    };
    const up = () => {
      if (activeRef.current === null) return;
      activeRef.current = null;
      document.body.style.userSelect = "";
      document.body.style.cursor = "";
      // Persist on drag end rather than on every pointermove.
      persist(widthsRef.current);
    };

    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    window.addEventListener("pointercancel", up);
    return () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      window.removeEventListener("pointercancel", up);
    };
  }, [applyPair, persist]);

  const handleKeyDown = useCallback(
    (index: number, e: React.KeyboardEvent) => {
      const rightIndex = index + 1;
      if (rightIndex >= widthsRef.current.length) return;

      const step = e.shiftKey ? KEY_STEP_LARGE : KEY_STEP;
      const direction = e.key === "ArrowLeft" ? -1 : e.key === "ArrowRight" ? 1 : 0;
      const toEdge = e.key === "Home" ? -1 : e.key === "End" ? 1 : 0;
      if (direction === 0 && toEdge === 0) return;
      e.preventDefault();

      // Each keystroke is relative to the *previous* width, so it must read from
      // the state updater rather than `widthsRef` — that ref is only refreshed on
      // render, so rapid repeats would all compute from the same stale base and
      // collapse into a single step.
      setWidths((prev) => {
        const combined = prev[index] + prev[rightIndex];
        const delta = toEdge !== 0 ? toEdge * combined : direction * step;
        const [left, right] = resizePair(
          prev[index],
          prev[rightIndex],
          delta,
          minWidths[index] ?? 0,
          minWidths[rightIndex] ?? 0,
        );
        const next = [...prev];
        next[index] = left;
        next[rightIndex] = right;
        return next;
      });
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  const handleDoubleClick = useCallback(
    (index: number) => {
      const defaults = defaultWidthsRef.current;
      if (defaults.length !== widthsRef.current.length) return;
      const rightIndex = index + 1;

      setWidths((prev) => {
        const next = [...prev];
        // Restore the pair's default proportion within the space they currently
        // occupy, so resetting one divider does not disturb the others.
        const combined = prev[index] + prev[rightIndex];
        const defaultCombined = defaults[index] + defaults[rightIndex];
        if (defaultCombined <= 0) return prev;
        next[index] = Math.round((combined * defaults[index]) / defaultCombined);
        next[rightIndex] = combined - next[index];
        return next;
      });
    },
    [],
  );

  if (panelCount === 0) return null;

  const totalWidth = widths.reduce((sum, w) => sum + w, 0);

  return (
    <div ref={containerRef} className={`flex h-full min-w-0 overflow-hidden ${className}`}>
      {validChildren.map((child, i) => (
        <Fragment key={i}>
          {i > 0 && (
            <div
              role="separator"
              aria-orientation="vertical"
              aria-label={
                panelLabels?.[i - 1] && panelLabels?.[i]
                  ? `Resize ${panelLabels[i - 1]} and ${panelLabels[i]}`
                  : `Resize panel ${i} and ${i + 1}`
              }
              aria-valuenow={totalWidth > 0 ? Math.round((widths[i - 1] / totalWidth) * 100) : 50}
              aria-valuemin={0}
              aria-valuemax={100}
              tabIndex={0}
              onPointerDown={(e) => handlePointerDown(i - 1, e)}
              onKeyDown={(e) => handleKeyDown(i - 1, e)}
              onDoubleClick={() => handleDoubleClick(i - 1)}
              title="Drag to resize, double-click to reset"
              className="w-1.5 shrink-0 cursor-col-resize bg-muted-foreground/30 transition-colors hover:bg-primary/50 focus-visible:bg-primary/70 active:bg-primary/70"
              data-testid={`resizer-${i - 1}`}
            />
          )}
          <div
            className="flex h-full min-w-0 flex-col overflow-hidden"
            data-testid={`panel-${i}`}
            style={
              widths[i] != null
                ? { width: widths[i], flex: "none", minWidth: minWidths[i] ?? 0 }
                : { flex: 1, minWidth: minWidths[i] ?? 0 }
            }
          >
            {child}
          </div>
        </Fragment>
      ))}
    </div>
  );
}

