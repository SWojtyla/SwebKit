import type { AlertHistoryBucket } from "../../lib/api";

/**
 * Compact duration for the Ops dashboard's MTTR stats — "45s", "12m", "3h 20m",
 * "2d 4h". Input is seconds (nullable — the summary deliberately reports null
 * when no resolved pair qualified rather than fabricating a zero).
 */
export function formatDurationSeconds(
    seconds: number | null | undefined,
): string {
    if (seconds === null || seconds === undefined) return "—";
    const s = Math.max(0, Math.round(seconds));
    if (s < 60) return `${s}s`;
    const minutes = Math.floor(s / 60);
    if (minutes < 60) {
        const rem = s % 60;
        return rem > 0 ? `${minutes}m ${rem}s` : `${minutes}m`;
    }
    const hours = Math.floor(minutes / 60);
    if (hours < 48) {
        const remMin = minutes % 60;
        return remMin > 0 ? `${hours}h ${remMin}m` : `${hours}h`;
    }
    const days = Math.floor(hours / 24);
    const remHours = hours % 24;
    return remHours > 0 ? `${days}d ${remHours}h` : `${days}d`;
}

/** Suppressed share of all firings, 0..1. Zero firings → 0 (not NaN). */
export function suppressionRatio(fired: number, suppressed: number): number {
    const total = fired + suppressed;
    return total === 0 ? 0 : suppressed / total;
}

/**
 * Total bar height percentages for the firings-per-hour chart, one per bucket —
 * the component renders each column at this height and splits it into a notified
 * segment and a suppressed segment. Always sums to the same scale so quiet
 * windows don't exaggerate a single spike.
 */
export function firingsBarHeights(buckets: AlertHistoryBucket[]): number[] {
    const max = Math.max(0, ...buckets.map((b) => b.fired + b.suppressed));
    if (max === 0) return buckets.map(() => 0);
    return buckets.map((b) => ((b.fired + b.suppressed) / max) * 100);
}

/**
 * Severity name → share of all firings (0..1), for the severity-breakdown bars.
 * Preserves only severities that actually fired — an all-quiet window yields {}.
 */
export function severityShares(
    severityCounts: Record<string, number>,
): Record<string, number> {
    const total = Object.values(severityCounts).reduce((a, b) => a + b, 0);
    if (total === 0) return {};
    return Object.fromEntries(
        Object.entries(severityCounts).map(([k, v]) => [k, v / total]),
    );
}
