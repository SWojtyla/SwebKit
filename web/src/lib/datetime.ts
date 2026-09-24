/**
 * Central timestamp formatting. Every date/time the UI renders goes through these
 * helpers so the app has one consistent answer to "which timezone is this shown in?":
 * always the user's local (OS/browser) timezone. Raw `new Date(x).toLocaleString()`
 * calls elsewhere in the codebase should migrate here — that also keeps every
 * timestamp on the same format instead of each call site picking its own.
 *
 * The status bar shows {@link localTimeZoneName} so the "local or UTC?" question
 * is answered visibly, not by convention.
 */

const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
});

const dateTimeSecondsFormat = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium",
});

const dateFormat = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
});

const timeFormat = new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
});

type DateInput = string | Date | number | null | undefined;

function toDate(value: DateInput): Date | null {
    if (value === null || value === undefined || value === "") return null;
    const date = value instanceof Date ? value : new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
}

/** "Mar 21, 2026, 14:32" in local time. Empty string for missing/invalid input. */
export function formatLocalDateTime(value: DateInput): string {
    const date = toDate(value);
    return date ? dateTimeFormat.format(date) : "";
}

/** Same as {@link formatLocalDateTime} but with seconds. */
export function formatLocalDateTimeSeconds(value: DateInput): string {
    const date = toDate(value);
    return date ? dateTimeSecondsFormat.format(date) : "";
}

/** Date only, in local time. */
export function formatLocalDate(value: DateInput): string {
    const date = toDate(value);
    return date ? dateFormat.format(date) : "";
}

/** Time only (HH:mm:ss), in local time. */
export function formatLocalTime(value: DateInput): string {
    const date = toDate(value);
    return date ? timeFormat.format(date) : "";
}

/** The IANA name of the local timezone, e.g. "Europe/Brussels". */
export function localTimeZoneName(): string {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "local time";
}

/**
 * Short label for the status bar, e.g. "CEST" / "GMT+2" / "UTC".
 * Falls back to the UTC offset when the runtime can't produce an abbreviation.
 */
export function localTimeZoneAbbrev(): string {
    const parts = new Intl.DateTimeFormat(undefined, {
        timeZoneName: "short",
    }).formatToParts(new Date());
    const name = parts.find((p) => p.type === "timeZoneName")?.value;
    return name || localUtcOffsetLabel() || localTimeZoneName();
}

/** UTC offset label like "UTC+02:00" / "UTC-05:00" for tooltips. */
export function localUtcOffsetLabel(): string {
    const minutes = -new Date().getTimezoneOffset();
    if (minutes === 0) return "UTC";
    const sign = minutes > 0 ? "+" : "-";
    const abs = Math.abs(minutes);
    const hh = String(Math.floor(abs / 60)).padStart(2, "0");
    const mm = String(abs % 60).padStart(2, "0");
    return `UTC${sign}${hh}:${mm}`;
}
