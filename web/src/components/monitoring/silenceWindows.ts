import { formatLocalDateTime } from "@/lib/datetime";
import type { MonitoringSilence } from "../../lib/api";

/**
 * Pure helpers for the silence/mute UI (monitoring-closed-loop item 3). All times are ISO
 * strings on the wire; these helpers do the local-time math so components stay declarative
 * and the tricky bits (next-midnight, the indefinite sentinel) are unit-testable.
 */

export type MuteDuration = "1h" | "tomorrow" | "indefinite";

/** Sentinel timestamp for "mute until I unmute" — far enough out to be effectively forever,
 * far enough in-bounds to round-trip through DateTimeOffset/Date without overflow. */
export const INDEFINITE_UNTIL = "9999-12-31T00:00:00Z";

export const MUTE_DURATIONS: { id: MuteDuration; label: string }[] = [
    { id: "1h", label: "Mute for 1 hour" },
    { id: "tomorrow", label: "Mute until tomorrow" },
    { id: "indefinite", label: "Mute until I unmute" },
];

/** Computes the `mutedUntil` value for a duration preset relative to `now`. */
export function muteUntilFor(duration: MuteDuration, now: Date = new Date()): string {
    switch (duration) {
        case "1h":
            return new Date(now.getTime() + 60 * 60 * 1000).toISOString();
        case "tomorrow": {
            // Next local midnight — "until tomorrow" reads as "for the rest of today".
            const d = new Date(now);
            d.setHours(24, 0, 0, 0);
            return d.toISOString();
        }
        case "indefinite":
            return INDEFINITE_UNTIL;
    }
}

/** True while `mutedUntil` is a valid timestamp strictly in the future. */
export function isRuleMuted(
    mutedUntil: string | null | undefined,
    now: Date = new Date(),
): boolean {
    if (!mutedUntil) return false;
    const d = new Date(mutedUntil);
    return !Number.isNaN(d.getTime()) && d.getTime() > now.getTime();
}

/** True for the effectively-forever sentinel — rendered as "muted", not a silly date. */
export function isIndefiniteMute(mutedUntil: string | null | undefined): boolean {
    if (!mutedUntil) return false;
    const d = new Date(mutedUntil);
    return !Number.isNaN(d.getTime()) && d.getFullYear() >= 9998;
}

/** Badge text for a muted rule row: "muted" for indefinite, "muted until <local time>" otherwise. */
export function muteBadgeLabel(
    mutedUntil: string | null | undefined,
    now: Date = new Date(),
): string {
    if (!isRuleMuted(mutedUntil, now)) return "";
    if (isIndefiniteMute(mutedUntil)) return "muted";
    return `muted until ${formatLocalDateTime(mutedUntil)}`;
}

export type SilenceDuration = "1h" | "4h" | "8h" | "tomorrow";

export const SILENCE_DURATIONS: { id: SilenceDuration; label: string }[] = [
    { id: "1h", label: "1 hour" },
    { id: "4h", label: "4 hours" },
    { id: "8h", label: "8 hours" },
    { id: "tomorrow", label: "Until tomorrow" },
];

/** End timestamp for a silence created now with the given duration preset. */
export function silenceEndFor(
    duration: SilenceDuration,
    now: Date = new Date(),
): string {
    switch (duration) {
        case "tomorrow": {
            const d = new Date(now);
            d.setHours(24, 0, 0, 0);
            return d.toISOString();
        }
        default:
            return new Date(
                now.getTime() + parseInt(duration, 10) * 60 * 60 * 1000,
            ).toISOString();
    }
}

/** Silences the UI lists — active and upcoming windows; fully expired ones are hidden
 * (they remain on disk as history but add only noise here). */
export function isSilenceCurrent(
    silence: MonitoringSilence,
    now: Date = new Date(),
): boolean {
    const end = new Date(silence.endUtc);
    return !Number.isNaN(end.getTime()) && end.getTime() > now.getTime();
}

/** Whether a silence is in its active window right now (vs. upcoming). */
export function isSilenceActive(
    silence: MonitoringSilence,
    now: Date = new Date(),
): boolean {
    const start = new Date(silence.startUtc);
    const end = new Date(silence.endUtc);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()))
        return false;
    return start.getTime() <= now.getTime() && now.getTime() < end.getTime();
}

/** Short scope label for a silence row: "All rules" or the named rules it covers. */
export function silenceScopeLabel(
    silence: MonitoringSilence,
    ruleName: (id: string) => string,
): string {
    if (!silence.ruleIds || silence.ruleIds.length === 0) return "All rules";
    return silence.ruleIds.map(ruleName).join(", ");
}
