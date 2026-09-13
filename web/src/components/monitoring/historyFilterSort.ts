import type { AlertFiredEvent, AlertSeverity } from "../../lib/api";

export type HistorySeverityFilter = "All" | AlertSeverity;
export type HistorySortBy = "time" | "severity";

const SEVERITY_RANK: Record<AlertSeverity, number> = {
  Critical: 0,
  Warning: 1,
};

/**
 * Applies the History tab's severity filter and sort on top of the already time-sorted
 * `mergedHistory` feed. Sorting by severity is a stable secondary ordering (ties keep their
 * existing, already-time-sorted relative order) so switching to "Severity" doesn't scramble
 * same-severity events out of chronological order.
 */
export function filterAndSortHistory(
  events: AlertFiredEvent[],
  severityFilter: HistorySeverityFilter,
  sortBy: HistorySortBy,
): AlertFiredEvent[] {
  const filtered = severityFilter === "All" ? events : events.filter((e) => e.severity === severityFilter);

  if (sortBy !== "severity") return filtered;

  return [...filtered].sort((a, b) => (SEVERITY_RANK[a.severity] ?? 99) - (SEVERITY_RANK[b.severity] ?? 99));
}
