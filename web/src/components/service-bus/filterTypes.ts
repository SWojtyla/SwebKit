export type FilterField =
    | "application-property"
    | "message-id"
    | "subject"
    | "correlation-id"
    | "enqueued-time"
    | "scheduled-time"
    | "delivery-count"
    | "sequence-number";

export type FilterOperator =
    | "contains"
    | "equals"
    | "not-equals"
    | "starts-with"
    | "regex"
    | "before"
    | "on-or-before"
    | "after"
    | "on-or-after"
    | "older-than"
    | "within-last"
    | "between"
    | "gt"
    | "gte"
    | "lt"
    | "lte";

export interface AdvancedFilterRule {
    id: string;
    enabled: boolean;
    field: FilterField;
    operator: FilterOperator;
    propertyName: string;
    value: string;
}

export interface FilterOperatorOption {
    value: FilterOperator;
    label: string;
}

export type FilterFieldKind = "text" | "numeric" | "date";

/** Which operator family a field uses — drives both the operator select and the value input type. */
export function fieldKind(field: FilterField): FilterFieldKind {
    switch (field) {
        case "enqueued-time":
        case "scheduled-time":
            return "date";
        case "delivery-count":
        case "sequence-number":
            return "numeric";
        default:
            return "text";
    }
}

const TEXT_OPERATORS: FilterOperatorOption[] = [
    { value: "contains", label: "Contains" },
    { value: "equals", label: "Equals" },
    { value: "not-equals", label: "Not equals" },
    { value: "starts-with", label: "Starts with" },
    { value: "regex", label: "Regex" },
];

const NUMERIC_OPERATORS: FilterOperatorOption[] = [
    { value: "equals", label: "Equals" },
    { value: "not-equals", label: "Not equals" },
    { value: "gt", label: ">" },
    { value: "gte", label: ">=" },
    { value: "lt", label: "<" },
    { value: "lte", label: "<=" },
    { value: "between", label: "Between" },
];

const DATE_OPERATORS: FilterOperatorOption[] = [
    { value: "older-than", label: "Older than" },
    { value: "within-last", label: "Within last" },
    { value: "before", label: "Before" },
    { value: "on-or-before", label: "On or before" },
    { value: "after", label: "After" },
    { value: "on-or-after", label: "On or after" },
    { value: "between", label: "Between" },
    { value: "equals", label: "Exactly at" },
];

/**
 * Application properties are untyped (`Record<string, unknown>` on the wire), so the
 * operator picks the comparison semantics: the text ops compare strings, the `>`
 * family coerces both sides to numbers, and the Before/After family parses both
 * sides as dates (ISO 8601 and the NServiceBus `yyyy-MM-dd HH:mm:ss:ffffff Z`
 * format). `between` auto-detects — numeric when both bounds parse as numbers,
 * dates otherwise. `equals`/`not-equals` stay text-only — semantic equality is
 * ambiguous when the property's type isn't declared.
 */
const APPLICATION_PROPERTY_OPERATORS: FilterOperatorOption[] = [
    ...TEXT_OPERATORS,
    { value: "gt", label: "> (number)" },
    { value: "gte", label: ">= (number)" },
    { value: "lt", label: "< (number)" },
    { value: "lte", label: "<= (number)" },
    { value: "between", label: "Between (num or date)" },
    { value: "before", label: "Before (date)" },
    { value: "on-or-before", label: "On or before (date)" },
    { value: "after", label: "After (date)" },
    { value: "on-or-after", label: "On or after (date)" },
    { value: "older-than", label: "Older than (date)" },
    { value: "within-last", label: "Within last (date)" },
];

export function getOperatorOptions(field: FilterField): FilterOperatorOption[] {
    if (field === "application-property") return APPLICATION_PROPERTY_OPERATORS;
    switch (fieldKind(field)) {
        case "date":
            return DATE_OPERATORS;
        case "numeric":
            return NUMERIC_OPERATORS;
        default:
            return TEXT_OPERATORS;
    }
}

export function defaultOperatorForField(field: FilterField): FilterOperator {
    switch (fieldKind(field)) {
        case "date":
            return "older-than";
        case "numeric":
            return "gte";
        default:
            return "contains";
    }
}

/** Relative operators compare against "now minus a duration" — the value is a duration, not a date. */
export function isRelativeOperator(operator: FilterOperator): boolean {
    return operator === "older-than" || operator === "within-last";
}

/** `between` takes two bounds — stored as `min,max` in `value` so saved filters stay flat. */
export function isRangeOperator(operator: FilterOperator): boolean {
    return operator === "between";
}

export function requiresPropertyName(field: FilterField): boolean {
    return field === "application-property";
}

export const FIELD_LABELS: Record<FilterField, string> = {
    "application-property": "Property",
    "message-id": "Message ID",
    subject: "Subject",
    "correlation-id": "Correlation ID",
    "enqueued-time": "Enqueued",
    "scheduled-time": "Scheduled for",
    "delivery-count": "Delivery Count",
    "sequence-number": "Sequence Number",
};

export const FIELD_OPTIONS: FilterField[] = [
    "application-property",
    "message-id",
    "subject",
    "correlation-id",
    "enqueued-time",
    "scheduled-time",
    "delivery-count",
    "sequence-number",
];

export function getValuePlaceholder(field: FilterField): string {
    switch (fieldKind(field)) {
        case "date":
            return "Date/time (e.g. 2026-03-28T12:00:00Z)";
        case "numeric":
            return "Number";
        default:
            return "Value";
    }
}

export const RELATIVE_UNITS = [
    { suffix: "m", label: "minutes", ms: 60_000 },
    { suffix: "h", label: "hours", ms: 3_600_000 },
    { suffix: "d", label: "days", ms: 86_400_000 },
] as const;

export type RelativeUnitSuffix = (typeof RELATIVE_UNITS)[number]["suffix"];

/**
 * Duration shorthand used by relative operators — `30m`, `2h`, `7d` (a bare number reads as
 * minutes). Kept as a single string so the value survives saved-filter round-trips without a
 * schema change.
 */
export function parseDurationMs(value: string): number | null {
    const m = /^(\d+(?:\.\d+)?)\s*(m|h|d)?$/i.exec(value.trim());
    if (!m) return null;
    const amount = Number(m[1]);
    if (!Number.isFinite(amount) || amount < 0) return null;
    const unit = RELATIVE_UNITS.find(
        (u) => u.suffix === (m[2]?.toLowerCase() ?? "m"),
    );
    return unit ? amount * unit.ms : null;
}

/** Splits a stored duration string into the number+unit the rule builder edits separately. */
export function splitDuration(value: string): {
    amount: string;
    unit: RelativeUnitSuffix;
} {
    const m = /^(\d+(?:\.\d+)?)\s*(m|h|d)?$/i.exec(value.trim());
    if (!m) return { amount: "", unit: "h" };
    const unit = (m[2]?.toLowerCase() ?? "h") as RelativeUnitSuffix;
    return { amount: m[1], unit };
}

/**
 * Splits a `between` value (`min,max`) into the two inputs the rule builder edits.
 * Missing halves come back as empty strings so an in-progress rule re-renders what was typed.
 */
export function splitRange(value: string): { min: string; max: string } {
    const [min = "", max = ""] = value.split(",", 2);
    return { min: min.trim(), max: max.trim() };
}

const OPERATOR_GLYPH: Partial<Record<FilterOperator, string>> = {
    contains: "contains",
    equals: "=",
    "not-equals": "≠",
    "starts-with": "starts with",
    regex: "matches",
    before: "<",
    "on-or-before": "≤",
    after: ">",
    "on-or-after": "≥",
    gt: ">",
    gte: "≥",
    lt: "<",
    lte: "≤",
};

/** One-line rule description for the filter chips — e.g. `deliveryCount ≥ 3`, `enqueued · older than 2h`. */
export function describeRule(rule: AdvancedFilterRule): string {
    const field =
        rule.field === "application-property"
            ? `prop:${rule.propertyName || "?"}`
            : FIELD_LABELS[rule.field];
    if (rule.operator === "older-than")
        return `${field} · older than ${rule.value}`;
    if (rule.operator === "within-last")
        return `${field} · within ${rule.value}`;
    if (rule.operator === "between") {
        const { min, max } = splitRange(rule.value);
        return `${field} between ${min || "?"} and ${max || "?"}`;
    }
    const glyph = OPERATOR_GLYPH[rule.operator] ?? rule.operator;
    return `${field} ${glyph} ${rule.value}`;
}

export function isRuleConfigured(rule: AdvancedFilterRule): boolean {
    if (isRangeOperator(rule.operator)) {
        const { min, max } = splitRange(rule.value);
        if (!min || !max) return false;
    } else if (!rule.value.trim()) return false;
    if (requiresPropertyName(rule.field) && !rule.propertyName.trim())
        return false;
    return true;
}

export function createFilterRule(): AdvancedFilterRule {
    return {
        id: crypto.randomUUID(),
        enabled: true,
        field: "application-property",
        operator: "contains",
        propertyName: "",
        value: "",
    };
}
