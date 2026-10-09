export type FilterField =
    | "application-property"
    | "enqueued-time"
    | "delivery-count"
    | "sequence-number";

export type FilterOperator =
    | "contains"
    | "equals"
    | "not-equals"
    | "regex"
    | "before"
    | "on-or-before"
    | "after"
    | "on-or-after"
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

const TEXT_OPERATORS: FilterOperatorOption[] = [
    { value: "contains", label: "Contains" },
    { value: "equals", label: "Equals" },
    { value: "not-equals", label: "Not equals" },
    { value: "regex", label: "Regex" },
];

const NUMERIC_OPERATORS: FilterOperatorOption[] = [
    { value: "equals", label: "Equals" },
    { value: "not-equals", label: "Not equals" },
    { value: "gt", label: ">" },
    { value: "gte", label: ">=" },
    { value: "lt", label: "<" },
    { value: "lte", label: "<=" },
];

const DATE_OPERATORS: FilterOperatorOption[] = [
    { value: "equals", label: "Equals" },
    { value: "before", label: "Before" },
    { value: "on-or-before", label: "On or before" },
    { value: "after", label: "After" },
    { value: "on-or-after", label: "On or after" },
];

/**
 * Application properties are untyped (`Record<string, unknown>` on the wire), so the
 * operator picks the comparison semantics: the text ops compare strings, the `>`
 * family coerces both sides to numbers, and the Before/After family parses both
 * sides as dates (ISO 8601 and the NServiceBus `yyyy-MM-dd HH:mm:ss:ffffff Z`
 * format). `equals`/`not-equals` stay text-only — semantic equality is ambiguous
 * when the property's type isn't declared.
 */
const APPLICATION_PROPERTY_OPERATORS: FilterOperatorOption[] = [
    ...TEXT_OPERATORS,
    { value: "gt", label: "> (number)" },
    { value: "gte", label: ">= (number)" },
    { value: "lt", label: "< (number)" },
    { value: "lte", label: "<= (number)" },
    { value: "before", label: "Before (date)" },
    { value: "on-or-before", label: "On or before (date)" },
    { value: "after", label: "After (date)" },
    { value: "on-or-after", label: "On or after (date)" },
];

export function getOperatorOptions(field: FilterField): FilterOperatorOption[] {
    switch (field) {
        case "application-property":
            return APPLICATION_PROPERTY_OPERATORS;
        case "enqueued-time":
            return DATE_OPERATORS;
        case "delivery-count":
        case "sequence-number":
            return NUMERIC_OPERATORS;
        default:
            return TEXT_OPERATORS;
    }
}

export function defaultOperatorForField(field: FilterField): FilterOperator {
    switch (field) {
        case "enqueued-time":
            return "after";
        case "delivery-count":
        case "sequence-number":
            return "gte";
        default:
            return "contains";
    }
}

export function requiresPropertyName(field: FilterField): boolean {
    return field === "application-property";
}

export function getValuePlaceholder(field: FilterField): string {
    switch (field) {
        case "enqueued-time":
            return "Date/time (e.g. 2026-03-28T12:00:00Z)";
        case "delivery-count":
            return "Number";
        case "sequence-number":
            return "Number";
        default:
            return "Text, number, or date (e.g. 2026-09-30T12:00Z)";
    }
}

export function isRuleConfigured(rule: AdvancedFilterRule): boolean {
    if (!rule.value.trim()) return false;
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
