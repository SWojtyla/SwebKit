import { useState } from "react";
import type { AlertHistoryEntry } from "../../lib/api";
import { RuleMuteControl } from "./RuleMuteControl";
import {
    filterAndSortHistory,
    type HistorySeverityFilter,
    type HistorySortBy,
} from "./historyFilterSort";
import { formatLocalDateTime } from "@/lib/datetime";

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
    onMute,
    mutedUntilByRule = {},
}: {
    /** Durable history rows (monitoring-closed-loop 4a): persisted Fired/Suppressed/Resolved
     * entries merged with any live events not yet flushed to the store. */
    events: AlertHistoryEntry[];
    /** Per-rule mute (monitoring-closed-loop item 3) — the snooze action that used to
     * only hide the row for the session now sets the rule's `mutedUntil` server-side too. */
    onMute?: (ruleId: string, until: string | null) => void;
    /** ruleId → current `mutedUntil`, so the mute menu can offer Unmute on a muted rule. */
    mutedUntilByRule?: Record<string, string | null | undefined>;
}) {
    const [snoozed, setSnoozed] = useState<Record<string, boolean>>({});
    const [severityFilter, setSeverityFilter] =
        useState<HistorySeverityFilter>("All");
    const [sortBy, setSortBy] = useState<HistorySortBy>("time");

    if (events.length === 0) {
        return (
            <div
                className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground"
                data-testid="monitoring-history-empty"
            >
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
                        onChange={(e) =>
                            setSeverityFilter(
                                e.target.value as HistorySeverityFilter,
                            )
                        }
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
                        onChange={(e) =>
                            setSortBy(e.target.value as HistorySortBy)
                        }
                        className="rounded-md border bg-card px-2 py-1 text-xs"
                        data-testid="monitoring-history-sort"
                    >
                        <option value="time">Time</option>
                        <option value="severity">Severity</option>
                    </select>
                </label>
            </div>

            {visibleEvents.length === 0 ? (
                <div
                    className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground"
                    data-testid="monitoring-history-filtered-empty"
                >
                    No {severityFilter.toLowerCase()} alert events
                </div>
            ) : (
                <div
                    className="rounded-lg border"
                    data-testid="monitoring-history-panel"
                >
                    <table className="w-full text-sm">
                        <thead className="border-b bg-muted/50">
                            <tr>
                                <th className="px-3 py-2 text-left">Rule</th>
                                <th className="px-3 py-2 text-left">
                                    Severity
                                </th>
                                <th className="px-3 py-2 text-left">Time</th>
                                <th className="px-3 py-2 text-left">Message</th>
                                <th className="px-3 py-2 text-right">
                                    Actions
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {visibleEvents.map((evt, i) => {
                                const key = `${evt.id}-${i}`;
                                if (snoozed[key]) return null;
                                return (
                                    <tr
                                        key={key}
                                        className={`border-b last:border-0 ${severityRowAccent[evt.severity] ?? ""}`}
                                        data-testid={`monitoring-history-row-${i}`}
                                    >
                                        <td className="px-3 py-2 font-medium">
                                            {evt.ruleName}
                                        </td>
                                        <td className="px-3 py-2">
                                            <span
                                                className={`rounded px-2 py-0.5 text-xs ${severityBadge[evt.severity] ?? severityBadge.Warning}`}
                                            >
                                                {evt.severity}
                                            </span>
                                            {evt.kind === "Suppressed" && (
                                                <span
                                                    className="ml-1 rounded border px-1.5 py-0.5 text-[10px] text-muted-foreground"
                                                    title="Suppressed by a silence window or rule mute"
                                                    data-testid={`monitoring-history-silenced-${i}`}
                                                >
                                                    silenced
                                                </span>
                                            )}
                                            {evt.kind === "Resolved" && (
                                                <span
                                                    className="ml-1 rounded px-1.5 py-0.5 text-[10px] font-medium text-success-foreground bg-success"
                                                    title="The rule recovered — incident closed"
                                                    data-testid={`monitoring-history-resolved-${i}`}
                                                >
                                                    resolved
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-3 py-2 text-xs text-muted-foreground">
                                            {formatLocalDateTime(evt.at)}
                                        </td>
                                        <td className="px-3 py-2 text-xs">
                                            {evt.message}
                                        </td>
                                        <td className="px-3 py-2 text-right">
                                            <RuleMuteControl
                                                ruleId={evt.ruleId}
                                                mutedUntil={
                                                    mutedUntilByRule[evt.ruleId]
                                                }
                                                onMute={(ruleId, until) => {
                                                    if (until !== null) {
                                                        setSnoozed((s) => ({
                                                            ...s,
                                                            [key]: true,
                                                        }));
                                                    }
                                                    onMute?.(ruleId, until);
                                                }}
                                                title="Snooze — mute this rule"
                                                testIdPrefix={`monitoring-history-snooze-${i}`}
                                            />
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
