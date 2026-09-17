import { useCallback, useMemo, useState } from "react";
import {
  computeLogWindow,
  filterLogEntries,
  searchLogEntries,
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
  searchMode: "filter" | "context";
  setSearchMode: (mode: "filter" | "context") => void;
  contextLines: number;
  setContextLines: (lines: number) => void;
  matchCount: number;
  canShowPreviousMatch: boolean;
  canShowNextMatch: boolean;
  showPreviousMatch: () => void;
  showNextMatch: () => void;
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
  const [searchMode, setSearchModeRaw] = useState<"filter" | "context">("filter");
  const [contextLines, setContextLinesRaw] = useState(5);
  const [currentMatch, setCurrentMatch] = useState(-1);
  const [paused, setPaused] = useState(false);
  const [pageFromNewest, setPageFromNewest] = useState(0);
  const [frozenTotal, setFrozenTotal] = useState<number | null>(null);

  const filtered = useMemo(
    () => searchMode === "context"
      ? searchLogEntries(entries, textFilter, contextLines)
      : filterLogEntries(entries, textFilter),
    [entries, textFilter, searchMode, contextLines],
  );
  const matchIndexes = useMemo(
    () => filtered.map((entry, index) => entry.searchKind === "match" ? index : -1).filter((index) => index >= 0),
    [filtered],
  );

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
      setCurrentMatch(-1);
      if (frozen) setFrozenTotal(filtered.length);
    },
    [frozen, filtered.length],
  );

  const setSearchMode = useCallback((mode: "filter" | "context") => {
    setSearchModeRaw(mode);
    setCurrentMatch(-1);
    setPageFromNewest(0);
    setFrozenTotal(null);
  }, []);

  const setContextLines = useCallback((lines: number) => {
    setContextLinesRaw(lines);
    setCurrentMatch(-1);
    setPageFromNewest(0);
    setFrozenTotal(null);
  }, []);

  const navigateMatch = useCallback((direction: -1 | 1) => {
    if (matchIndexes.length === 0) return;
    const next = currentMatch < 0
      ? (direction > 0 ? 0 : matchIndexes.length - 1)
      : Math.min(matchIndexes.length - 1, Math.max(0, currentMatch + direction));
    setCurrentMatch(next);
    const targetIndex = matchIndexes[next];
    setFrozenTotal(filtered.length);
    setPageFromNewest(Math.floor((filtered.length - 1 - targetIndex) / pageSize));
  }, [matchIndexes, currentMatch, filtered.length, pageSize]);

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
    searchMode,
    setSearchMode,
    contextLines,
    setContextLines,
    matchCount: matchIndexes.length,
    canShowPreviousMatch: matchIndexes.length > 0 && currentMatch !== 0,
    canShowNextMatch: matchIndexes.length > 0 && currentMatch < matchIndexes.length - 1,
    showPreviousMatch: () => navigateMatch(-1),
    showNextMatch: () => navigateMatch(1),
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
