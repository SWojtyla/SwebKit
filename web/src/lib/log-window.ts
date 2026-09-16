/// Pure helpers behind both AKS log views: timestamp parsing, filtering, the paging
/// window arithmetic, and the chronological merge the multi-pod view needs.
///
/// Kept free of React and of `@codemirror`/DOM imports so vitest can cover it — the
/// runner here is node-only and collects `src/**/*.test.ts`, with no DOM shim.

/** How much of a line's timestamp to show. Persisted as a view preference. */
export type TimestampMode = "off" | "time" | "full";

export interface LogEntry {
  /** The message, with any timestamp prefix already stripped. */
  text: string;
  searchKind?: "match" | "context" | "gap";
  omitted?: number;
  /** The pod that emitted it. Undefined in the single-pod view, where it would be noise. */
  pod?: string;
  /** The raw timestamp prefix, or `null` when the line carried none. */
  ts: string | null;
  /** Arrival order. The tie-breaker that keeps the merge stable. */
  seq: number;
}

/// Mirrors `LogLineTimestamp` on the backend. Two wire shapes reach us: Kubernetes
/// RFC3339Nano (`2026-09-09T10:22:30.118456789Z msg`) and the demo client's
/// space-separated form (`2026-09-09 10:22:30.118  msg`). Both must parse, or the
/// parser is wrong against one of the two clients.
const TIMESTAMP_PREFIX_RE =
  /^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)\s+([\s\S]*)$/;

/**
 * Splits a raw log line into its timestamp prefix and message. A line with no
 * recognisable prefix comes back whole, so this is safe on output from a stream that
 * was not asked for timestamps.
 */
export function parseLogLine(raw: string): { ts: string | null; text: string } {
  if (!raw) return { ts: null, text: raw ?? "" };

  const match = TIMESTAMP_PREFIX_RE.exec(raw);
  if (!match) return { ts: null, text: raw };

  // Matching the shape is not the same as being a date. Without this check,
  // `2026-13-45T99:99:99Z` would be stripped off and part of the message lost.
  const [, ts, text] = match;
  return Number.isNaN(Date.parse(normaliseForParsing(ts)))
    ? { ts: null, text: raw }
    : { ts, text };
}

/// `Date.parse` is only required to understand the ISO form, so the demo client's
/// space separator needs swapping for a `T` before it is handed over. A value with
/// neither `Z` nor an offset is read as local time, which is what the demo intends.
function normaliseForParsing(ts: string): string {
  return ts.replace(" ", "T");
}

/** Milliseconds since the epoch for a parsed prefix, or `null` when there isn't one. */
export function timestampMs(ts: string | null): number | null {
  if (ts === null) return null;
  const ms = Date.parse(normaliseForParsing(ts));
  return Number.isNaN(ms) ? null : ms;
}

const TIME_FORMAT = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});

/** Renders a timestamp for display. Returns `""` when there is nothing to show. */
export function formatLogTimestamp(ts: string | null, mode: TimestampMode): string {
  if (mode === "off" || ts === null) return "";
  if (mode === "full") return ts;

  const ms = timestampMs(ts);
  if (ms === null) return "";

  // Milliseconds are the whole point when correlating two pods, and `Intl` will not
  // format them, so they are appended by hand.
  const fraction = String(ms % 1000).padStart(3, "0");
  return `${TIME_FORMAT.format(new Date(ms))}.${fraction}`;
}

/** Case-insensitive substring match over the message text. Never matches the timestamp. */
export function filterLogEntries(entries: readonly LogEntry[], term: string): LogEntry[] {
  const needle = term.trim().toLowerCase();
  if (!needle) return entries as LogEntry[];
  return entries.filter((e) => e.text.toLowerCase().includes(needle));
}

export function searchLogEntries(entries: readonly LogEntry[], term: string, contextLines: number): LogEntry[] {
  const needle = term.trim().toLowerCase();
  if (!needle) return entries as LogEntry[];
  const radius = Math.max(0, Math.trunc(contextLines));
  const matches = entries
    .map((entry, index) => entry.text.toLowerCase().includes(needle) ? index : -1)
    .filter((index) => index >= 0);
  if (matches.length === 0) return [];

  const matchSet = new Set(matches);
  const included = new Set<number>();
  for (const match of matches) {
    for (let index = Math.max(0, match - radius); index <= Math.min(entries.length - 1, match + radius); index++)
      included.add(index);
  }

  const result: LogEntry[] = [];
  let previous = -1;
  for (const index of [...included].sort((a, b) => a - b)) {
    if (previous >= 0 && index > previous + 1) {
      result.push({ text: "", ts: null, seq: Number.MIN_SAFE_INTEGER + index, searchKind: "gap", omitted: index - previous - 1 });
    }
    result.push({
      ...entries[index],
      searchKind: matchSet.has(index) ? "match" : "context",
    });
    previous = index;
  }
  return result;
}

export interface LogWindow {
  /** Index of the first visible entry. */
  start: number;
  /** Index one past the last visible entry. */
  end: number;
  maxPage: number;
  /** `pageFromNewest` clamped into range. */
  safePage: number;
}

/**
 * The slice of a log list to render, counting pages back from the newest line.
 *
 * Page 0 is the newest. The arithmetic lived inline in `PodLogView`; it is here so its
 * boundaries can be tested, since an off-by-one silently hides the newest line — the
 * one being watched.
 */
export function computeLogWindow(args: {
  total: number;
  pageFromNewest: number;
  visible: number;
}): LogWindow {
  const total = Math.max(0, args.total);
  const visible = Math.max(1, args.visible);
  const maxPage = Math.max(0, Math.ceil(total / visible) - 1);
  const safePage = Math.min(Math.max(0, args.pageFromNewest), maxPage);
  const end = Math.max(0, total - safePage * visible);
  const start = Math.max(0, end - visible);
  return { start, end, maxPage, safePage };
}

/** The "Showing 1-200 of 3,400" caption, or a bare count when there is nothing to page. */
export function windowSummary(start: number, end: number, total: number): string {
  if (total === 0) return "0 lines";
  return `Showing ${start + 1}-${Math.min(end, total)} of ${total}`;
}

/**
 * Orders interleaved entries by the time their pod actually logged them.
 *
 * This is the whole reason the multi-pod view is worth opening: arrival order reflects
 * network jitter, so two lines seconds apart can arrive back to front and read as
 * simultaneous.
 *
 * A line with no timestamp of its own — a stack frame, a wrapped message — inherits the
 * last timestamp seen *for its own pod*, which keeps a multi-line exception attached to
 * its header instead of drifting to one end. Entries that tie fall back to arrival order,
 * so the sort is stable.
 */
export function mergeByTimestamp(entries: readonly LogEntry[]): LogEntry[] {
  const lastSeenByPod = new Map<string, number>();
  let lastSeenAny: number | null = null;

  const keyed = entries.map((entry) => {
    const podKey = entry.pod ?? "";
    let ms = timestampMs(entry.ts);

    if (ms === null) {
      ms = lastSeenByPod.get(podKey) ?? lastSeenAny;
    } else {
      lastSeenByPod.set(podKey, ms);
      lastSeenAny = ms;
    }

    // Still null only for lines before any timestamp has been seen at all; they sort
    // to the front, preserving arrival order among themselves.
    return { entry, ms: ms ?? Number.NEGATIVE_INFINITY };
  });

  keyed.sort((a, b) => (a.ms === b.ms ? a.entry.seq - b.entry.seq : a.ms - b.ms));
  return keyed.map((k) => k.entry);
}
