import { useState } from "react";
import { BellOff } from "lucide-react";
import type { AlertFiredEvent } from "../../lib/api";
import { filterAndSortHistory, type HistorySeverityFilter, type HistorySortBy } from "./historyFilterSort";

const severityBadge: Record<string, string> = {
  Critical: "font-semibold text-destructive-foreground bg-destructive",
  Warning: "font-medium text-warning-foreground bg-warning",
};

const severityRowAccent: Record<string, string> = {
  Critical: "border-l-2 border-l-destructive",
  Warning: "",
};

export function AlertHistoryPanel({
  events,
  onSnooze,
}: {
  events: AlertFiredEvent[];
  onSnooze?: (evt: AlertFiredEvent) => void;
}) {
  const [snoozed, setSnoozed] = useState<Record<string, boolean>>({});
  const [severityFilter, setSeverityFilter] = useState<HistorySeverityFilter>("All");
  const [sortBy, setSortBy] = useState<HistorySortBy>("time");

  if (events.length === 0) {
    return (
      <div className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground" data-testid="monitoring-history-empty">
        No alert events yet
      </div>
    );
  }

  const visibleEvents = filterAndSortHistory(events, severityFilter, sortBy);

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <label className="flex items-center gap-1">
          Severity
          <select
            value={severityFilter}
            onChange={(e) => setSeverityFilter(e.target.value as HistorySeverityFilter)}
            className="rounded-md border bg-card px-2 py-1 text-xs"
            data-testid="monitoring-history-severity-filter"
          >
            <option value="All">All</option>
            <option value="Critical">Critical</option>
            <option value="Warning">Warning</option>
          </select>
        </label>
        <label className="flex items-center gap-1">
          Sort by
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as HistorySortBy)}
            className="rounded-md border bg-card px-2 py-1 text-xs"
            data-testid="monitoring-history-sort"
          >
            <option value="time">Time</option>
            <option value="severity">Severity</option>
          </select>
        </label>
      </div>

      {visibleEvents.length === 0 ? (
        <div className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground" data-testid="monitoring-history-filtered-empty">
          No {severityFilter.toLowerCase()} alert events
        </div>
      ) : (
        <div className="rounded-lg border" data-testid="monitoring-history-panel">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Rule</th>
                <th className="px-3 py-2 text-left">Severity</th>
                <th className="px-3 py-2 text-left">Time</th>
                <th className="px-3 py-2 text-left">Message</th>
                <th className="px-3 py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {visibleEvents.map((evt, i) => {
                const key = `${evt.ruleId}-${evt.firedAt}-${i}`;
                if (snoozed[key]) return null;
                return (
                  <tr
                    key={key}
                    className={`border-b last:border-0 ${severityRowAccent[evt.severity] ?? ""}`}
                    data-testid={`monitoring-history-row-${i}`}
                  >
                    <td className="px-3 py-2 font-medium">{evt.ruleName}</td>
                    <td className="px-3 py-2">
                      <span className={`rounded px-2 py-0.5 text-xs ${severityBadge[evt.severity] ?? severityBadge.Warning}`}>
                        {evt.severity}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-xs text-muted-foreground">
                      {new Date(evt.firedAt).toLocaleString()}
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{evt.message}</div>
                      {evt.detail && <div className="text-muted-foreground">{evt.detail}</div>}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button
                        onClick={() => {
                          setSnoozed((s) => ({ ...s, [key]: true }));
                          onSnooze?.(evt);
                        }}
                        className="rounded p-1 hover:bg-accent"
                        title="Snooze (this session)"
                        data-testid={`monitoring-history-snooze-${i}`}
                      >
                        <BellOff className="h-3.5 w-3.5" />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
