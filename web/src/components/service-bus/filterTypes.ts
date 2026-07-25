export type FilterField = "application-property" | "enqueued-time" | "delivery-count" | "sequence-number";

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

export const TEXT_OPERATORS: FilterOperatorOption[] = [
  { value: "contains", label: "Contains" },
  { value: "equals", label: "Equals" },
  { value: "not-equals", label: "Not equals" },
  { value: "regex", label: "Regex" },
];

export const NUMERIC_OPERATORS: FilterOperatorOption[] = [
  { value: "equals", label: "Equals" },
  { value: "not-equals", label: "Not equals" },
  { value: "gt", label: ">" },
  { value: "gte", label: ">=" },
  { value: "lt", label: "<" },
  { value: "lte", label: "<=" },
];

export const DATE_OPERATORS: FilterOperatorOption[] = [
  { value: "equals", label: "Equals" },
  { value: "before", label: "Before" },
  { value: "on-or-before", label: "On or before" },
  { value: "after", label: "After" },
  { value: "on-or-after", label: "On or after" },
];

export function getOperatorOptions(field: FilterField): FilterOperatorOption[] {
  switch (field) {
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
      return "Value";
  }
}

export function isRuleConfigured(rule: AdvancedFilterRule): boolean {
  if (!rule.value.trim()) return false;
  if (requiresPropertyName(rule.field) && !rule.propertyName.trim()) return false;
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
