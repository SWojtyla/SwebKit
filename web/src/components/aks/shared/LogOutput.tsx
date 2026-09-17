import { forwardRef } from "react";
import { getLogLineClass } from "@/lib/logLevel";
import { formatLogTimestamp, type LogEntry, type TimestampMode } from "@/lib/log-window";
import { LogLineText } from "./LogLineText";

/// The scrolling log body, shared by both AKS log views.
///
/// `testId` is a prop rather than a constant because the two views' ids are a Playwright
/// contract: `aks-ux.spec.ts` asserts `.log-tok-*` classes inside `log-output`, and
/// `aks-deferred.spec.ts` looks for `multi-pod-log-output`.
///
/// Highlighting is entirely `getLogLineClass` (whole-line severity) plus the memoised
/// `LogLineText` (per-token). Neither is reimplemented here — `LogLineText` is memoised
/// precisely because this list re-renders on every buffer flush.

export interface LogOutputProps {
  entries: readonly LogEntry[];
  /** Index of the first entry within the filtered list, so line keys stay stable while paging. */
  startIndex: number;
  timestampMode: TimestampMode;
  highlightTerm?: string;
  /** Shows the emitting pod before each line. On for the multi-pod view. */
  showPod?: boolean;
  emptyMessage: string;
  testId: string;
}

export const LogOutput = forwardRef<HTMLDivElement, LogOutputProps>(function LogOutput(
  { entries, startIndex, timestampMode, highlightTerm = "", showPod = false, emptyMessage, testId },
  ref,
) {
  return (
    <div ref={ref} className="flex-1 overflow-auto p-2 font-mono text-xs" data-testid={testId}>
      {entries.length === 0 ? (
        <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
          {emptyMessage}
        </div>
      ) : (
        entries.map((entry, i) => {
          if (entry.searchKind === "gap") {
            return (
              <div key={entry.seq} className="my-1 text-center text-muted-foreground" data-testid="log-context-gap">
                ── {entry.omitted} lines ──
              </div>
            );
          }
          const stamp = formatLogTimestamp(entry.ts, timestampMode);
          return (
            <div
              key={entry.seq}
              className={`whitespace-pre-wrap break-all ${entry.searchKind === "context" ? "opacity-60" : ""}`}
              data-testid={`log-line-${startIndex + i}`}
              data-search-kind={entry.searchKind}
            >
              {stamp && <span className="mr-2 text-muted-foreground">{stamp}</span>}
              {showPod && entry.pod && (
                <span className="mr-2 text-muted-foreground">{entry.pod}:</span>
              )}
              <span className={`log-line ${getLogLineClass(entry.text)}`}>
                <LogLineText line={entry.text} highlightTerm={entry.searchKind === "match" ? highlightTerm : ""} />
              </span>
            </div>
          );
        })
      )}
    </div>
  );
});
