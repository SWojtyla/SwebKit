import type { SbMessage } from "@/lib/types";
import type { AdvancedFilterRule, FilterOperator } from "./filterTypes";
import { isRuleConfigured, requiresPropertyName } from "./filterTypes";

export function matchesTextFilter(message: SbMessage, text: string): boolean {
  const q = text.toLowerCase();
  return (
    message.messageId.toLowerCase().includes(q) ||
    (message.correlationId?.toLowerCase().includes(q) ?? false) ||
    (message.subject?.toLowerCase().includes(q) ?? false) ||
    message.body.toLowerCase().includes(q)
  );
}

function tryGetApplicationPropertyValue(
  message: SbMessage,
  propertyName: string,
): string | null {
  if (!propertyName.trim() || !message.applicationProperties) return null;

  const props = message.applicationProperties;
  // Direct match
  if (props[propertyName] !== undefined) {
    return String(props[propertyName]);
  }
  // Case-insensitive match
  for (const [key, value] of Object.entries(props)) {
    if (key.toLowerCase() === propertyName.toLowerCase()) {
      return String(value);
    }
  }
  return null;
}

function safeRegexMatch(value: string, pattern: string): boolean {
  try {
    const re = new RegExp(pattern, "i");
    return re.test(value);
  } catch {
    return false;
  }
}

function evaluateTextOperator(actual: string, expected: string, op: FilterOperator): boolean {
  switch (op) {
    case "equals":
      return actual.toLowerCase() === expected.toLowerCase();
    case "not-equals":
      return actual.toLowerCase() !== expected.toLowerCase();
    case "regex":
      return safeRegexMatch(actual, expected);
    default: // contains
      return actual.toLowerCase().includes(expected.toLowerCase());
  }
}

function evaluateNumericOperator(actual: number, expected: number, op: FilterOperator): boolean {
  switch (op) {
    case "equals":
      return actual === expected;
    case "not-equals":
      return actual !== expected;
    case "gt":
      return actual > expected;
    case "gte":
      return actual >= expected;
    case "lt":
      return actual < expected;
    case "lte":
      return actual <= expected;
    default:
      return false;
  }
}

function evaluateDateOperator(actualMs: number, expectedMs: number, op: FilterOperator): boolean {
  switch (op) {
    case "equals":
      return actualMs === expectedMs;
    case "before":
      return actualMs < expectedMs;
    case "on-or-before":
      return actualMs <= expectedMs;
    case "after":
      return actualMs > expectedMs;
    case "on-or-after":
      return actualMs >= expectedMs;
    default:
      return false;
  }
}

function matchesAdvancedRule(message: SbMessage, rule: AdvancedFilterRule): boolean {
  const rawValue = rule.value.trim();
  if (!rawValue) return true;

  switch (rule.field) {
    case "application-property": {
      const propertyValue = tryGetApplicationPropertyValue(message, rule.propertyName);
      if (propertyValue === null) return false;
      return evaluateTextOperator(propertyValue, rawValue, rule.operator);
    }

    case "delivery-count": {
      const expected = Number(rawValue);
      if (isNaN(expected)) return false;
      return evaluateNumericOperator(message.deliveryCount, expected, rule.operator);
    }

    case "sequence-number": {
      if (message.sequenceNumber === null) return false;
      const expected = Number(rawValue);
      if (isNaN(expected)) return false;
      return evaluateNumericOperator(message.sequenceNumber, expected, rule.operator);
    }

    case "enqueued-time": {
      const expectedMs = Date.parse(rawValue);
      if (isNaN(expectedMs)) return false;
      const actualMs = Date.parse(message.enqueuedAt);
      if (isNaN(actualMs)) return false;
      return evaluateDateOperator(actualMs, expectedMs, rule.operator);
    }

    default:
      return true;
  }
}

export function applyFilters(
  messages: SbMessage[],
  textFilter: string,
  advancedRules: AdvancedFilterRule[],
  advancedEnabled: boolean,
  pinnedSessionId: string | null,
): SbMessage[] {
  let query = messages;

  if (pinnedSessionId) {
    query = query.filter((m) => m.sessionId === pinnedSessionId);
  }

  if (textFilter.trim()) {
    query = query.filter((m) => matchesTextFilter(m, textFilter));
  }

  if (advancedEnabled) {
    const enabledRules = advancedRules.filter((r) => r.enabled && isRuleConfigured(r));
    if (enabledRules.length > 0) {
      query = query.filter((m) => enabledRules.every((rule) => matchesAdvancedRule(m, rule)));
    }
  }

  return query;
}

export { requiresPropertyName };
