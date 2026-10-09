import type { SbMessage } from "@/lib/types";
import type { AdvancedFilterRule, FilterOperator } from "./filterTypes";
import { isRuleConfigured } from "./filterTypes";

function matchesTextFilter(message: SbMessage, text: string): boolean {
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

function evaluateTextOperator(
    actual: string,
    expected: string,
    op: FilterOperator,
): boolean {
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

function evaluateNumericOperator(
    actual: number,
    expected: number,
    op: FilterOperator,
): boolean {
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

function evaluateDateOperator(
    actualMs: number,
    expectedMs: number,
    op: FilterOperator,
): boolean {
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

/**
 * Lenient date parse for filter values and application-property payloads. Covers
 * ISO 8601 plus the NServiceBus shape `2026-09-30 04:25:38:948134 Z` — space
 * separator, colon before the fractional seconds, spaced zone — which
 * `Date.parse` doesn't understand. Returns `NaN` when unparseable.
 */
export function parseFlexibleDate(value: string): number {
    const trimmed = value.trim();
    const match =
        /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}:\d{2})(?:[:.](\d{1,9}))?\s*(Z|[+-]\d{2}:?\d{2})?$/i.exec(
            trimmed,
        );
    if (match) {
        const [, day, time, fraction, zone] = match;
        let suffix = "";
        if (zone) {
            suffix =
                zone.toUpperCase() === "Z"
                    ? "Z"
                    : zone.includes(":")
                      ? zone
                      : `${zone.slice(0, 3)}:${zone.slice(3)}`;
        }
        const ms = Date.parse(
            `${day}T${time}${fraction ? `.${fraction}` : ""}${suffix}`,
        );
        if (!isNaN(ms)) return ms;
    }
    return Date.parse(trimmed);
}

/**
 * Application-property values are untyped strings/objects, so the operator
 * decides the semantics: date operators parse both sides as dates, the `>`/`>=`/`<`/`<=`
 * family coerces both sides to numbers, everything else compares as text.
 * Unparseable operands never match.
 */
function evaluatePropertyOperator(
    actual: string,
    expected: string,
    op: FilterOperator,
): boolean {
    switch (op) {
        case "before":
        case "on-or-before":
        case "after":
        case "on-or-after": {
            const actualMs = parseFlexibleDate(actual);
            const expectedMs = parseFlexibleDate(expected);
            if (isNaN(actualMs) || isNaN(expectedMs)) return false;
            return evaluateDateOperator(actualMs, expectedMs, op);
        }
        case "gt":
        case "gte":
        case "lt":
        case "lte": {
            const actualNum = Number(actual);
            const expectedNum = Number(expected);
            if (isNaN(actualNum) || isNaN(expectedNum)) return false;
            return evaluateNumericOperator(actualNum, expectedNum, op);
        }
        default:
            return evaluateTextOperator(actual, expected, op);
    }
}

function matchesAdvancedRule(
    message: SbMessage,
    rule: AdvancedFilterRule,
): boolean {
    const rawValue = rule.value.trim();
    if (!rawValue) return true;

    switch (rule.field) {
        case "application-property": {
            const propertyValue = tryGetApplicationPropertyValue(
                message,
                rule.propertyName,
            );
            if (propertyValue === null) return false;
            return evaluatePropertyOperator(
                propertyValue,
                rawValue,
                rule.operator,
            );
        }

        case "delivery-count": {
            const expected = Number(rawValue);
            if (isNaN(expected)) return false;
            return evaluateNumericOperator(
                message.deliveryCount,
                expected,
                rule.operator,
            );
        }

        case "sequence-number": {
            if (message.sequenceNumber === null) return false;
            const expected = Number(rawValue);
            if (isNaN(expected)) return false;
            return evaluateNumericOperator(
                message.sequenceNumber,
                expected,
                rule.operator,
            );
        }

        case "enqueued-time": {
            const expectedMs = parseFlexibleDate(rawValue);
            if (isNaN(expectedMs)) return false;
            const actualMs = Date.parse(message.enqueuedAt);
            if (isNaN(actualMs)) return false;
            return evaluateDateOperator(actualMs, expectedMs, rule.operator);
        }

        default:
            return true;
    }
}

/**
 * Whether any filter condition is actually narrowing the list right now — the text search, a
 * pinned session, or a configured advanced rule. Drives both "should the Save filter button be
 * offered" (pre-existing `canSaveFilter` logic in `MessageList`) and the single "Clear all
 * filters" action, which previously didn't exist: the text search had its own inline clear, the
 * session pin had its own, and "Clear all" next to the advanced rules only cleared the rule list,
 * leaving 3-4 scattered controls with no one place to reset everything at once.
 */
export function hasActiveFilters(
    textFilter: string,
    pinnedSessionId: string | null,
    advancedRules: AdvancedFilterRule[],
): boolean {
    return Boolean(
        textFilter.trim() ||
        pinnedSessionId ||
        advancedRules.some(isRuleConfigured),
    );
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
        const enabledRules = advancedRules.filter(
            (r) => r.enabled && isRuleConfigured(r),
        );
        if (enabledRules.length > 0) {
            query = query.filter((m) =>
                enabledRules.every((rule) => matchesAdvancedRule(m, rule)),
            );
        }
    }

    return query;
}
