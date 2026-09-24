/**
 * Display helpers for persisted AI insight reports (ai-insight-reports). Pure so
 * they're unit-testable without rendering.
 */

/** Badge classes for the model-assessed investigation severity (low/medium/high) —
 * deliberately a different vocabulary from the rule severity palette
 * (Warning/Critical) used by alert rows, so the two can't be confused. */
export function insightSeverityBadge(severity?: string | null): string {
    switch (severity?.toLowerCase()) {
        case "high":
            return "font-semibold text-destructive-foreground bg-destructive";
        case "medium":
            return "font-medium text-warning-foreground bg-warning";
        default:
            return "font-medium bg-muted text-muted-foreground";
    }
}

/** Normalized severity label — the model's output is free-form, so anything
 * outside low/medium/high renders as "unknown" rather than verbatim. */
export function insightSeverityLabel(severity?: string | null): string {
    const s = severity?.toLowerCase();
    return s === "high" || s === "medium" || s === "low" ? s : "unknown";
}

export function formatInsightTime(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
