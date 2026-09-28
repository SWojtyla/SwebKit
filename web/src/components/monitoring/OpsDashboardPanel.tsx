import { useState } from "react";
import { AlertCircle } from "lucide-react";
import { SkeletonRows } from "@/components/shared/Skeleton";
import { useMonitoringHistorySummary } from "../../lib/hooks";
import { formatLocalDateTime, formatLocalTime } from "@/lib/datetime";
import {
    formatDurationSeconds,
    suppressionRatio,
    firingsBarHeights,
    severityShares,
} from "./opsSummaryFormat";
import type { AlertHistorySummary } from "../../lib/api";

const WINDOW_OPTIONS = [
    { hours: 6, label: "6h" },
    { hours: 24, label: "24h" },
    { hours: 72, label: "3d" },
    { hours: 168, label: "7d" },
] as const;

const severityBadge: Record<string, string> = {
    Critical: "font-semibold text-destructive-foreground bg-destructive",
    Warning: "font-medium text-warning-foreground bg-warning",
};

const severityBarColor: Record<string, string> = {
    Critical: "bg-destructive",
    Warning: "bg-warning",
};

function StatCard({
    label,
    value,
    hint,
    testId,
}: {
    label: string;
    value: string;
    hint?: string;
    testId: string;
}) {
    return (
        <div
            className="rounded-lg border bg-card px-4 py-3"
            data-testid={testId}
        >
            <div className="text-xs text-muted-foreground">{label}</div>
            <div className="mt-1 text-xl font-semibold tabular-nums">
                {value}
            </div>
            {hint && (
                <div className="mt-0.5 text-xs text-muted-foreground">
                    {hint}
                </div>
            )}
        </div>
    );
}

/**
 * The Monitoring "Ops" tab (monitoring-closed-loop item 4) — an aggregate view
 * over the durable alert-history store: firings/hour timeline, severity split,
 * open incidents, and MTTR. Rendering is intentionally dependency-free: plain
 * flexbox/CSS bars instead of a chart library.
 */
export function OpsDashboardPanel() {
    const [windowHours, setWindowHours] = useState(24);
    const {
        data: summary,
        isLoading,
        isError,
        error,
    } = useMonitoringHistorySummary(windowHours);

    return (
        <div className="space-y-4" data-testid="monitoring-ops">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <label className="flex items-center gap-1 text-xs text-muted-foreground">
                    Window
                    <select
                        value={windowHours}
                        onChange={(e) =>
                            setWindowHours(Number(e.target.value))
                        }
                        className="rounded-md border bg-card px-2 py-1 text-xs"
                        data-testid="ops-window-select"
                    >
                        {WINDOW_OPTIONS.map((w) => (
                            <option key={w.hours} value={w.hours}>
                                Last {w.label}
                            </option>
                        ))}
                    </select>
                </label>
                {summary && (
                    <span className="text-xs text-muted-foreground">
                        Updated {formatLocalTime(summary.generatedAtUtc)}
                    </span>
                )}
            </div>

            {isError ? (
                <div
                    className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-3 text-sm text-destructive"
                    data-testid="monitoring-ops-error"
                >
                    <AlertCircle className="h-4 w-4 shrink-0" />
                    <span>
                        {error instanceof Error ? error.message : String(error)}
                    </span>
                </div>
            ) : isLoading || !summary ? (
                <SkeletonRows count={4} />
            ) : (
                <OpsSummaryView summary={summary} />
            )}
        </div>
    );
}

function OpsSummaryView({ summary }: { summary: AlertHistorySummary }) {
    const totalFirings = summary.firedCount + summary.suppressedCount;
    const suppressedPct = Math.round(
        suppressionRatio(summary.firedCount, summary.suppressedCount) * 100,
    );
    const heights = firingsBarHeights(summary.firingsPerHour);
    const shares = severityShares(summary.severityCounts);
    const severities = Object.entries(shares).sort(
        (a, b) => b[1] - a[1],
    );

    return (
        <>
            {/* Headline stats */}
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
                <StatCard
                    label="Firings"
                    value={String(totalFirings)}
                    hint={`${summary.firedCount} notified`}
                    testId="ops-stat-firings"
                />
                <StatCard
                    label="Suppressed"
                    value={String(summary.suppressedCount)}
                    hint={`${suppressedPct}% of firings`}
                    testId="ops-stat-suppressed"
                />
                <StatCard
                    label="Resolved"
                    value={String(summary.resolvedCount)}
                    testId="ops-stat-resolved"
                />
                <StatCard
                    label="Open incidents"
                    value={String(summary.openIncidentCount)}
                    testId="ops-stat-open"
                />
                <StatCard
                    label="MTTR (mean)"
                    value={formatDurationSeconds(summary.mttr.meanSeconds)}
                    hint={
                        summary.mttr.resolvedPairCount > 0
                            ? `${summary.mttr.resolvedPairCount} resolved`
                            : "no resolved incidents"
                    }
                    testId="ops-stat-mttr"
                />
            </div>

            {/* Firings-per-hour timeline — CSS bars, suppressed share stacked on top */}
            <div className="rounded-lg border p-4">
                <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                    <span>Firings per hour</span>
                    <span>
                        <span className="mr-3 inline-flex items-center gap-1">
                            <span className="inline-block h-2 w-2 rounded-sm bg-primary" />
                            notified
                        </span>
                        <span className="inline-flex items-center gap-1">
                            <span className="inline-block h-2 w-2 rounded-sm bg-muted-foreground/40" />
                            suppressed
                        </span>
                    </span>
                </div>
                <div
                    className="flex h-24 items-end gap-px"
                    data-testid="ops-firings-chart"
                >
                    {summary.firingsPerHour.map((b, i) => {
                        const total = b.fired + b.suppressed;
                        const height = heights[i];
                        const suppressedShare =
                            total > 0 ? b.suppressed / total : 0;
                        return (
                            <div
                                key={b.bucketStartUtc}
                                className="flex h-full flex-1 flex-col justify-end"
                                title={`${formatLocalDateTime(b.bucketStartUtc)}: ${b.fired} fired, ${b.suppressed} suppressed`}
                                data-testid={`ops-firings-bar-${i}`}
                            >
                                {height > 0 && (
                                    <div
                                        className="flex w-full flex-col justify-end overflow-hidden rounded-sm"
                                        style={{ height: `${height}%` }}
                                    >
                                        <div
                                            className="w-full bg-muted-foreground/40"
                                            style={{
                                                height: `${suppressedShare * 100}%`,
                                            }}
                                        />
                                        <div className="w-full flex-1 bg-primary" />
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
                <div className="mt-1 flex justify-between text-[10px] text-muted-foreground">
                    <span>{formatLocalDateTime(summary.windowStartUtc)}</span>
                    <span>now</span>
                </div>
            </div>

            <div className="grid gap-3 lg:grid-cols-2">
                {/* Severity breakdown */}
                <div
                    className="rounded-lg border p-4"
                    data-testid="ops-severity"
                >
                    <div className="mb-3 text-xs text-muted-foreground">
                        Severity
                    </div>
                    {severities.length === 0 ? (
                        <div className="py-4 text-center text-sm text-muted-foreground">
                            No firings in this window
                        </div>
                    ) : (
                        <div className="space-y-2">
                            {severities.map(([name, share]) => (
                                <div
                                    key={name}
                                    className="flex items-center gap-3"
                                    data-testid={`ops-severity-row-${name}`}
                                >
                                    <span
                                        className={`w-16 rounded px-2 py-0.5 text-center text-xs ${severityBadge[name] ?? severityBadge.Warning}`}
                                    >
                                        {name}
                                    </span>
                                    <div className="h-2 flex-1 rounded bg-muted/50">
                                        <div
                                            className={`h-2 rounded ${severityBarColor[name] ?? "bg-muted-foreground/40"}`}
                                            style={{
                                                width: `${share * 100}%`,
                                            }}
                                        />
                                    </div>
                                    <span className="w-8 text-right text-xs tabular-nums">
                                        {summary.severityCounts[name]}
                                    </span>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                {/* Open incidents */}
                <div
                    className="rounded-lg border p-4"
                    data-testid="ops-open-incidents"
                >
                    <div className="mb-3 text-xs text-muted-foreground">
                        Open incidents
                    </div>
                    {summary.openIncidents.length === 0 ? (
                        <div
                            className="py-4 text-center text-sm text-muted-foreground"
                            data-testid="ops-open-incidents-empty"
                        >
                            No open incidents
                        </div>
                    ) : (
                        <div className="max-h-56 space-y-2 overflow-y-auto pr-1">
                            {summary.openIncidents.map((incident, i) => (
                                <div
                                    key={`${incident.ruleId}-${incident.sinceUtc}`}
                                    className="rounded-md border px-3 py-2 text-sm"
                                    data-testid={`ops-open-incident-${i}`}
                                >
                                    <div className="flex items-center justify-between gap-2">
                                        <span className="truncate font-medium">
                                            {incident.ruleName}
                                        </span>
                                        <span
                                            className={`rounded px-2 py-0.5 text-xs ${severityBadge[incident.severity] ?? severityBadge.Warning}`}
                                        >
                                            {incident.severity}
                                        </span>
                                    </div>
                                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-muted-foreground">
                                        <span>
                                            since{" "}
                                            {formatLocalDateTime(
                                                incident.sinceUtc,
                                            )}
                                        </span>
                                        {incident.refireCount > 0 && (
                                            <span>
                                                +{incident.refireCount} re-fires
                                            </span>
                                        )}
                                        {incident.suppressed && (
                                            <span title="The firing that opened this incident was suppressed by a silence or mute">
                                                silenced
                                            </span>
                                        )}
                                        <span
                                            title="Detection latency bound — the condition may have begun up to one eval interval before the alert"
                                            data-testid={`ops-open-incident-latency-${i}`}
                                        >
                                            ≤{" "}
                                            {incident.ruleIntervalSeconds != null
                                                ? formatDurationSeconds(
                                                      incident.ruleIntervalSeconds,
                                                  )
                                                : "?"}{" "}
                                            detection
                                        </span>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </div>

            {/* Honesty notes — the summary deliberately doesn't fabricate these */}
            <div
                className="rounded-lg border bg-muted/30 px-4 py-3 text-xs text-muted-foreground"
                data-testid="ops-notes"
            >
                <p>{summary.detectionLatencyNote}</p>
                <p className="mt-1">
                    MTTR covers incidents resolved inside the window
                    {summary.mttr.orphanedResolutions > 0 &&
                        ` — ${summary.mttr.orphanedResolutions} resolved row${summary.mttr.orphanedResolutions === 1 ? "" : "s"} excluded because the opening firing is no longer in retained history`}
                    . Open incidents may include recoveries that happened while
                    the app wasn't running.
                </p>
            </div>
        </>
    );
}
