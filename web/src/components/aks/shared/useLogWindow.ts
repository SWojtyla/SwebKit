import { useCallback, useMemo, useState } from "react";
import {
  computeLogWindow,
  filterLogEntries,
  windowSummary,
  type LogEntry,
} from "@/lib/log-window";

/// Paging and pause state over a live log list, shared by both AKS log views.
///
/// The subtle part is the *anchor*. Once the user pauses or pages back, new lines keep
/// arriving; without freezing the total, the window would slide under them and the line
/// they stopped to read would scroll away. `frozenTotal` pins the count at the moment
/// the view froze, and everything after that is reported as pending instead.
///
/// Lifted from `PodLogView` rather than rewritten — it is the fiddliest logic in that
/// file and the most likely thing to regress.

export interface UseLogWindowResult {
  /** Entries after the text filter, before windowing. */
  filtered: LogEntry[];
  /** The slice to render. */
  visible: LogEntry[];
  /** Index of the first visible entry within `filtered`, for stable line keys. */
  visibleStart: number;
  textFilter: string;
  setTextFilter: (value: string) => void;
  paused: boolean;
  togglePause: () => void;
  /** True while the window is pinned — paused, or paged away from the newest lines. */
  frozen: boolean;
  /** Lines held back because the window is frozen. */
  heldBack: number;
  summary: string;
  canShowOlder: boolean;
  canShowNewer: boolean;
  showOlder: () => void;
  showNewer: () => void;
  jumpToLatest: () => void;
  /** Drops the anchor and returns to the newest page. */
  reset: () => void;
}

export function useLogWindow(entries: readonly LogEntry[], pageSize: number): UseLogWindowResult {
  const [textFilter, setTextFilterRaw] = useState("");
  const [paused, setPaused] = useState(false);
  const [pageFromNewest, setPageFromNewest] = useState(0);
  const [frozenTotal, setFrozenTotal] = useState<number | null>(null);

  const filtered = useMemo(() => filterLogEntries(entries, textFilter), [entries, textFilter]);

  const frozen = paused || pageFromNewest > 0;
  const total =
    frozen && frozenTotal !== null ? Math.min(frozenTotal, filtered.length) : filtered.length;

  const { start, end, maxPage, safePage } = computeLogWindow({
    total,
    pageFromNewest,
    visible: pageSize,
  });

  const visible = useMemo(() => filtered.slice(start, end), [filtered, start, end]);

  // Re-anchoring on a filter change matters: narrowing the filter shrinks the list under
  // a frozen window, which would otherwise jump to a different part of the log.
  const setTextFilter = useCallback(
    (value: string) => {
      setTextFilterRaw(value);
      if (frozen) setFrozenTotal(filtered.length);
    },
    [frozen, filtered.length],
  );

  const togglePause = useCallback(() => {
    setPaused((wasPaused) => {
      if (wasPaused) {
        setFrozenTotal(null);
        setPageFromNewest(0);
      } else {
        setFrozenTotal(filtered.length);
      }
      return !wasPaused;
    });
  }, [filtered.length]);

  const showOlder = useCallback(() => {
    if (safePage >= maxPage) return;
    if (!frozen) setFrozenTotal(filtered.length);
    setPageFromNewest((p) => p + 1);
  }, [safePage, maxPage, frozen, filtered.length]);

  const showNewer = useCallback(() => {
    setPageFromNewest((p) => {
      const next = Math.max(0, p - 1);
      if (next === 0 && !paused) setFrozenTotal(null);
      return next;
    });
  }, [paused]);

  const jumpToLatest = useCallback(() => {
    setPageFromNewest(0);
    setFrozenTotal(paused ? filtered.length : null);
  }, [paused, filtered.length]);

  const reset = useCallback(() => {
    setPaused(false);
    setPageFromNewest(0);
    setFrozenTotal(null);
  }, []);

  return {
    filtered,
    visible,
    visibleStart: start,
    textFilter,
    setTextFilter,
    paused,
    togglePause,
    frozen,
    heldBack: Math.max(0, filtered.length - total),
    summary: windowSummary(start, end, total),
    canShowOlder: safePage < maxPage,
    canShowNewer: safePage > 0,
    showOlder,
    showNewer,
    jumpToLatest,
    reset,
  };
}
