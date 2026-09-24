/**
 * Cron expression utilities for the CronJob schedule editor and "next run" column.
 *
 * Supports the standard 5-field Kubernetes CronJob grammar (minute hour day-of-month
 * month day-of-week): wildcards, lists, ranges, steps, month/day names, `?`, and the
 * `@hourly`/`@daily`/`@weekly`/`@monthly`/`@yearly`/`@annually`/`@midnight` macros.
 * `L`, `W`, `#`, seconds and year fields are intentionally rejected — Kubernetes
 * doesn't accept them either, and silently mis-parsing them would be worse than
 * refusing to show a next run.
 *
 * {@link nextCronRun} evaluates the schedule in the CronJob's own `spec.timeZone`
 * (an IANA zone) when one is set, and in the user's local timezone otherwise —
 * matching kube-controller-manager, which evaluates in `spec.timeZone` or, when
 * unset, the controller's own zone (almost always UTC, but the user's local zone is
 * the more useful display for an unset field and every consumer renders the result
 * through the local-time formatter anyway).
 */

export interface CronField {
    wildcard: boolean;
    /** Allowed values; ignored when `wildcard` is true (all values allowed). */
    values: Set<number>;
}

export interface ParsedCron {
    minute: CronField;
    hour: CronField;
    dayOfMonth: CronField;
    month: CronField;
    dayOfWeek: CronField;
}

interface FieldRange {
    min: number;
    max: number;
    names?: Record<string, number>;
}

const MINUTE: FieldRange = { min: 0, max: 59 };
const HOUR: FieldRange = { min: 0, max: 23 };
const DAY_OF_MONTH: FieldRange = { min: 1, max: 31 };
const MONTH: FieldRange = {
    min: 1,
    max: 12,
    names: {
        jan: 1,
        feb: 2,
        mar: 3,
        apr: 4,
        may: 5,
        jun: 6,
        jul: 7,
        aug: 8,
        sep: 9,
        oct: 10,
        nov: 11,
        dec: 12,
    },
};
const DAY_OF_WEEK: FieldRange = {
    min: 0,
    max: 7, // 7 is normalized to 0 after parsing
    names: { sun: 0, mon: 1, tue: 2, wed: 3, thu: 4, fri: 5, sat: 6 },
};

const FIELD_RANGES = [MINUTE, HOUR, DAY_OF_MONTH, MONTH, DAY_OF_WEEK];

const MACROS: Record<string, string> = {
    "@yearly": "0 0 1 1 *",
    "@annually": "0 0 1 1 *",
    "@monthly": "0 0 1 * *",
    "@weekly": "0 0 * * 0",
    "@daily": "0 0 * * *",
    "@midnight": "0 0 * * *",
    "@hourly": "0 * * * *",
};

const WEEKDAY_NAMES = [
    "Sunday",
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
];

const MONTH_NAMES = [
    "January",
    "February",
    "March",
    "April",
    "May",
    "June",
    "July",
    "August",
    "September",
    "October",
    "November",
    "December",
];

function parseValue(token: string, range: FieldRange): number | null {
    const lower = token.toLowerCase();
    if (range.names && lower in range.names) return range.names[lower];
    if (!/^\d+$/.test(token)) return null;
    const value = parseInt(token, 10);
    return value >= range.min && value <= range.max ? value : null;
}

function parseField(field: string, range: FieldRange): CronField | null {
    if (field === "*" || field === "?")
        return { wildcard: true, values: new Set() };
    if (!field) return null;

    const values = new Set<number>();
    for (const part of field.split(",")) {
        if (!part) return null;

        const slash = part.split("/");
        if (slash.length > 2) return null;
        const [body, stepStr] = slash;
        const step = stepStr !== undefined ? parseInt(stepStr, 10) : 1;
        if (stepStr !== undefined && (!/^\d+$/.test(stepStr) || step < 1))
            return null;

        let lo: number;
        let hi: number;
        if (body === "*" || body === "?" || body === "") {
            if (stepStr === undefined) return null; // lone "*" already handled above
            lo = range.min;
            hi = range.max;
        } else {
            const m = /^([a-zA-Z0-9]+)(?:-([a-zA-Z0-9]+))?$/.exec(body);
            if (!m) return null;
            const a = parseValue(m[1], range);
            if (a === null) return null;
            // `a/n` means "from a to max, every n" in Vixie cron.
            const b =
                m[2] !== undefined
                    ? parseValue(m[2], range)
                    : stepStr !== undefined
                      ? range.max
                      : a;
            if (b === null || b < a) return null;
            lo = a;
            hi = b;
        }
        for (let v = lo; v <= hi; v += step) values.add(v);
    }
    if (values.size === 0) return null;
    return { wildcard: false, values };
}

/** Parses a 5-field cron expression or supported @macro. Null when invalid. */
export function parseCronExpression(expression: string): ParsedCron | null {
    const trimmed = expression.trim();
    if (!trimmed) return null;
    const effective = MACROS[trimmed.toLowerCase()] ?? trimmed;
    const fields = effective.split(/\s+/);
    if (fields.length !== 5) return null;

    const parsed = fields.map((f, i) => parseField(f, FIELD_RANGES[i]));
    if (parsed.some((p) => p === null)) return null;

    const [minute, hour, dayOfMonth, month, dayOfWeek] = parsed as [
        CronField,
        CronField,
        CronField,
        CronField,
        CronField,
    ];
    // Sunday may be written as 0 or 7 — normalize to 0.
    if (dayOfWeek.values.has(7)) {
        dayOfWeek.values.delete(7);
        dayOfWeek.values.add(0);
    }
    return { minute, hour, dayOfMonth, month, dayOfWeek };
}

export function isValidCronExpression(expression: string): boolean {
    // `@reboot` etc. are valid cron *words* but never produce a scheduled run —
    // treat them as invalid here so the editor refuses them rather than saving a
    // schedule Kubernetes would reject anyway.
    if (expression.trim().toLowerCase().startsWith("@")) {
        return expression.trim().toLowerCase() in MACROS;
    }
    return parseCronExpression(expression) !== null;
}

// ── Next-run computation ─────────────────────────────────────────────────────

interface LocalParts {
    year: number;
    month: number;
    day: number;
    hour: number;
    minute: number;
    weekday: number;
}

const PARTS_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = {
    year: "numeric",
    month: "numeric",
    day: "numeric",
    hour: "numeric",
    minute: "numeric",
    hourCycle: "h23",
    weekday: "short",
};

const WEEKDAY_LOOKUP: Record<string, number> = {
    Sun: 0,
    Mon: 1,
    Tue: 2,
    Wed: 3,
    Thu: 4,
    Fri: 5,
    Sat: 6,
};

function makePartsFormat(timeZone?: string): Intl.DateTimeFormat | null {
    try {
        return new Intl.DateTimeFormat("en-US", {
            ...PARTS_FORMAT_OPTIONS,
            ...(timeZone ? { timeZone } : {}),
        });
    } catch {
        return null;
    }
}

function localParts(format: Intl.DateTimeFormat, date: Date): LocalParts {
    const parts: Partial<LocalParts> = {};
    for (const part of format.formatToParts(date)) {
        switch (part.type) {
            case "year":
                parts.year = parseInt(part.value, 10);
                break;
            case "month":
                parts.month = parseInt(part.value, 10);
                break;
            case "day":
                parts.day = parseInt(part.value, 10);
                break;
            case "hour":
                parts.hour = parseInt(part.value, 10) % 24;
                break;
            case "minute":
                parts.minute = parseInt(part.value, 10);
                break;
            case "weekday":
                parts.weekday = WEEKDAY_LOOKUP[part.value] ?? 0;
                break;
        }
    }
    return parts as LocalParts;
}

/** Whole-minute offset (local wall time minus UTC) at the given instant. */
function tzOffsetMinutes(format: Intl.DateTimeFormat, instant: Date): number {
    const p = localParts(format, instant);
    const wallAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute);
    const utcMinute = Math.floor(instant.getTime() / 60_000) * 60_000;
    return (wallAsUtc - utcMinute) / 60_000;
}

/**
 * Converts a wall time in the formatter's zone back to a UTC instant.
 * Returns null when the wall time can't exist (spring-forward gap) or can't be
 * resolved — rare and correct to skip rather than guess.
 */
function wallTimeToUtc(
    format: Intl.DateTimeFormat,
    year: number,
    month: number,
    day: number,
    hour: number,
    minute: number,
): Date | null {
    const guess = Date.UTC(year, month - 1, day, hour, minute);
    let candidate = guess - tzOffsetMinutes(format, new Date(guess)) * 60_000;
    const refined =
        guess - tzOffsetMinutes(format, new Date(candidate)) * 60_000;
    if (refined !== candidate) candidate = refined;

    const back = localParts(format, new Date(candidate));
    return back.year === year &&
        back.month === month &&
        back.day === day &&
        back.hour === hour &&
        back.minute === minute
        ? new Date(candidate)
        : null;
}

function matchesDate(cron: ParsedCron, parts: LocalParts): boolean {
    if (!cron.month.wildcard && !cron.month.values.has(parts.month))
        return false;
    const domMatch =
        cron.dayOfMonth.wildcard || cron.dayOfMonth.values.has(parts.day);
    const dowMatch =
        cron.dayOfWeek.wildcard || cron.dayOfWeek.values.has(parts.weekday);
    // Vixie semantics: when *both* dom and dow are restricted the day matches if
    // *either* matches; a wildcard defers to the other field entirely.
    if (cron.dayOfMonth.wildcard && cron.dayOfWeek.wildcard) return true;
    if (cron.dayOfMonth.wildcard) return dowMatch;
    if (cron.dayOfWeek.wildcard) return domMatch;
    return domMatch || dowMatch;
}

function sortedValues(field: CronField, min: number, max: number): number[] {
    if (field.wildcard) {
        const all: number[] = [];
        for (let v = min; v <= max; v++) all.push(v);
        return all;
    }
    return [...field.values].sort((a, b) => a - b);
}

/**
 * The next instant the schedule fires after `from` (exclusive of the current minute,
 * matching how kube-controller-manager reports `lastScheduleTime` vs upcoming runs).
 * Returns null for unparseable expressions, unsupported timezone names, or schedules
 * with no occurrence inside the search window (~5 years — a schedule that far out is
 * better shown as "—" than computed at unbounded cost).
 */
export function nextCronRun(
    expression: string,
    options?: { timeZone?: string | null; from?: Date },
): Date | null {
    const cron = parseCronExpression(expression);
    if (!cron) return null;

    const format = makePartsFormat(options?.timeZone ?? undefined);
    if (!format) return null;

    const from = options?.from ?? new Date();
    const hours = sortedValues(cron.hour, 0, 23);
    const minutes = sortedValues(cron.minute, 0, 59);

    // The "already past" cutoff belongs to `from`, not to the stepping cursor:
    // cursor starts at from+60s, so using its minute-of-day would skip the very
    // next minute (from=10:30 → 10:31 excluded). Computing it from `from` also
    // stays correct when from+60s rolls into the next day (from=23:59:30).
    const fromParts = localParts(format, from);
    const fromDateKey = `${fromParts.year}-${fromParts.month}-${fromParts.day}`;
    const fromMinutes = fromParts.hour * 60 + fromParts.minute;

    // Step through local calendar days. A local day is always ≥23h long, so a 23h
    // step can never skip one; repeated days (DST fall-back) are deduped.
    const MAX_ITERATIONS = 2200; // ~5.5 years of days plus dedupe skips
    const seenDates = new Set<string>();
    let cursor = new Date(from.getTime() + 60_000);

    for (let i = 0; i < MAX_ITERATIONS; i++) {
        const parts = localParts(format, cursor);
        const dateKey = `${parts.year}-${parts.month}-${parts.day}`;
        if (seenDates.has(dateKey)) {
            cursor = new Date(cursor.getTime() + 3_600_000);
            continue;
        }
        seenDates.add(dateKey);

        if (matchesDate(cron, parts)) {
            const afterMinutes = dateKey === fromDateKey ? fromMinutes : -1;
            for (const hh of hours) {
                for (const mm of minutes) {
                    if (hh * 60 + mm <= afterMinutes) continue;
                    const instant = wallTimeToUtc(
                        format,
                        parts.year,
                        parts.month,
                        parts.day,
                        hh,
                        mm,
                    );
                    if (instant && instant.getTime() > from.getTime())
                        return instant;
                }
            }
        }
        cursor = new Date(cursor.getTime() + 23 * 3_600_000);
    }
    return null;
}

// ── Human-readable descriptions ──────────────────────────────────────────────

function pad2(n: number): string {
    return String(n).padStart(2, "0");
}

function singleValue(field: CronField): number | null {
    return !field.wildcard && field.values.size === 1
        ? [...field.values][0]
        : null;
}

/** Detects "every n" step fields: contiguous range from min, step n. */
function stepValue(field: CronField, min: number, max: number): number | null {
    if (field.wildcard) return 1;
    const sorted = [...field.values].sort((a, b) => a - b);
    if (sorted.length < 2 || sorted[0] !== min) return null;
    const step = sorted[1] - sorted[0];
    if (step < 2) return null;
    for (let i = 0; i < sorted.length; i++) {
        if (sorted[i] !== min + i * step) return null;
    }
    // Must reach the end of the range — `0-10/5` is contiguous-but-bounded, not "every 5".
    return sorted[sorted.length - 1] + step > max ? step : null;
}

/**
 * A short English description for common schedule shapes ("Daily at 02:00"),
 * or null when the expression doesn't fit a friendly pattern — callers show the
 * raw cron string in that case rather than a misleading paraphrase.
 */
export function describeCronExpression(expression: string): string | null {
    const cron = parseCronExpression(expression);
    if (!cron) return null;
    const { minute, hour, dayOfMonth, month, dayOfWeek } = cron;

    if (
        minute.wildcard &&
        hour.wildcard &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return "Every minute";
    }

    const everyMinute = stepValue(minute, 0, 59);
    if (
        everyMinute !== null &&
        everyMinute > 1 &&
        hour.wildcard &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return `Every ${everyMinute} minutes`;
    }

    const everyHour = stepValue(hour, 0, 23);
    const mm = singleValue(minute);
    if (
        mm !== null &&
        everyHour !== null &&
        everyHour > 1 &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return `Every ${everyHour} hours at :${pad2(mm)}`;
    }

    const hh = singleValue(hour);
    const time = mm !== null && hh !== null ? `${pad2(hh)}:${pad2(mm)}` : null;

    if (
        mm !== null &&
        hour.wildcard &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return `Every hour at :${pad2(mm)}`;
    }

    if (time && dayOfMonth.wildcard && month.wildcard && dayOfWeek.wildcard) {
        return `Daily at ${time}`;
    }

    if (time && dayOfMonth.wildcard && month.wildcard && !dayOfWeek.wildcard) {
        const days = [...dayOfWeek.values]
            .sort((a, b) => a - b)
            .map((d) => WEEKDAY_NAMES[d]);
        if (days.length > 0) {
            return `${days.length === 7 ? "Daily" : `Weekly on ${days.join(", ")}`} at ${time}`;
        }
    }

    const dom = singleValue(dayOfMonth);
    if (time && dom !== null && month.wildcard && dayOfWeek.wildcard) {
        return `Monthly on day ${dom} at ${time}`;
    }

    const mo = singleValue(month);
    if (time && dom !== null && mo !== null && dayOfWeek.wildcard) {
        return `Yearly on ${MONTH_NAMES[mo - 1]} ${dom} at ${time}`;
    }

    return null;
}

// ── Schedule presets for the editor ──────────────────────────────────────────

export type CronPreset =
    | "everyNMinutes"
    | "hourly"
    | "daily"
    | "weekly"
    | "monthly"
    | "yearly"
    | "custom";

export interface CronPresetValues {
    preset: CronPreset;
    everyN?: number;
    minute?: number;
    hour?: number;
    weekday?: number;
    dayOfMonth?: number;
    month?: number;
}

/** Maps a cron expression onto the closest editor preset for pre-filling the form. */
export function detectCronPreset(expression: string): CronPresetValues {
    const cron = parseCronExpression(expression);
    if (!cron) return { preset: "custom" };
    const { minute, hour, dayOfMonth, month, dayOfWeek } = cron;

    const everyN = stepValue(minute, 0, 59);
    if (
        everyN !== null &&
        everyN > 1 &&
        hour.wildcard &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return { preset: "everyNMinutes", everyN };
    }

    const mm = singleValue(minute) ?? 0;
    const hh = singleValue(hour) ?? 0;

    if (
        singleValue(minute) !== null &&
        hour.wildcard &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return { preset: "hourly", minute: mm };
    }

    if (
        singleValue(minute) !== null &&
        singleValue(hour) !== null &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return { preset: "daily", minute: mm, hour: hh };
    }

    if (
        singleValue(minute) !== null &&
        singleValue(hour) !== null &&
        dayOfMonth.wildcard &&
        month.wildcard &&
        !dayOfWeek.wildcard &&
        dayOfWeek.values.size === 1
    ) {
        return {
            preset: "weekly",
            minute: mm,
            hour: hh,
            weekday: [...dayOfWeek.values][0],
        };
    }

    if (
        singleValue(minute) !== null &&
        singleValue(hour) !== null &&
        !dayOfMonth.wildcard &&
        dayOfMonth.values.size === 1 &&
        month.wildcard &&
        dayOfWeek.wildcard
    ) {
        return {
            preset: "monthly",
            minute: mm,
            hour: hh,
            dayOfMonth: [...dayOfMonth.values][0],
        };
    }

    if (
        singleValue(minute) !== null &&
        singleValue(hour) !== null &&
        !dayOfMonth.wildcard &&
        dayOfMonth.values.size === 1 &&
        !month.wildcard &&
        month.values.size === 1 &&
        dayOfWeek.wildcard
    ) {
        return {
            preset: "yearly",
            minute: mm,
            hour: hh,
            dayOfMonth: [...dayOfMonth.values][0],
            month: [...month.values][0],
        };
    }

    return { preset: "custom" };
}

/** Builds a cron expression from editor preset values. */
export function buildCronExpression(values: CronPresetValues): string {
    const mm = values.minute ?? 0;
    const hh = values.hour ?? 0;
    switch (values.preset) {
        case "everyNMinutes":
            return `*/${values.everyN ?? 5} * * * *`;
        case "hourly":
            return `${mm} * * * *`;
        case "daily":
            return `${mm} ${hh} * * *`;
        case "weekly":
            return `${mm} ${hh} * * ${values.weekday ?? 1}`;
        case "monthly":
            return `${mm} ${hh} ${values.dayOfMonth ?? 1} * *`;
        case "yearly":
            return `${mm} ${hh} ${values.dayOfMonth ?? 1} ${values.month ?? 1} *`;
        default:
            return "";
    }
}
