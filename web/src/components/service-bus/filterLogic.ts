import type { SbMessage } from "@/lib/types";
import type { AdvancedFilterRule, FilterOperator } from "./filterTypes";
import {
    isRuleConfigured,
    isRelativeOperator,
    isRangeOperator,
    parseDurationMs,
    splitRange,
} from "./filterTypes";

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
        case "starts-with":
            return actual.toLowerCase().startsWith(expected.toLowerCase());
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

function matchesNumeric(actual: number, rule: AdvancedFilterRule): boolean {
    const rawValue = rule.value.trim();
    if (isRangeOperator(rule.operator)) {
        const { min, max } = splitRange(rawValue);
        const lo = Number(min);
        const hi = Number(max);
        if (isNaN(lo) || isNaN(hi)) return false;
        return actual >= lo && actual <= hi;
    }
    const expected = Number(rawValue);
    if (isNaN(expected)) return false;
    return evaluateNumericOperator(actual, expected, rule.operator);
}

/** A scheduled message is one whose broker-side fire time is still in the future — past
 * stamps have already fired and the message is a normal active one. */
export function isScheduledMessage(m: SbMessage, now = Date.now()): boolean {
    if (!m.scheduledEnqueueTime) return false;
    const t = Date.parse(m.scheduledEnqueueTime);
    return !isNaN(t) && t > now;
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
            return evaluateTextOperator(propertyValue, rawValue, rule.operator);
        }

        case "message-id":
            return evaluateTextOperator(
                message.messageId,
                rawValue,
                rule.operator,
            );
        case "subject":
            return (
                !!message.subject &&
                evaluateTextOperator(message.subject, rawValue, rule.operator)
            );
        case "correlation-id":
            return (
                !!message.correlationId &&
                evaluateTextOperator(
                    message.correlationId,
                    rawValue,
                    rule.operator,
                )
            );

        case "delivery-count":
            return matchesNumeric(message.deliveryCount, rule);
        case "sequence-number": {
            if (message.sequenceNumber === null) return false;
            return matchesNumeric(message.sequenceNumber, rule);
        }

        case "enqueued-time":
        case "scheduled-time": {
            const actual =
                rule.field === "enqueued-time"
                    ? message.enqueuedAt
                    : message.scheduledEnqueueTime;
            if (!actual) return false;
            const actualMs = Date.parse(actual);
            if (isNaN(actualMs)) return false;
            // Relative operators: the comparison point is "now minus the duration" —
            // `older than 2h` means the timestamp sits before now-2h, `within last 30m`
            // means at or after now-30m.
            if (isRelativeOperator(rule.operator)) {
                const durationMs = parseDurationMs(rawValue);
                if (durationMs === null) return false;
                const cutoffMs = Date.now() - durationMs;
                return rule.operator === "older-than"
                    ? actualMs < cutoffMs
                    : actualMs >= cutoffMs;
            }
            if (isRangeOperator(rule.operator)) {
                const { min, max } = splitRange(rawValue);
                const lo = Date.parse(min);
                const hi = Date.parse(max);
                if (isNaN(lo) || isNaN(hi)) return false;
                return actualMs >= lo && actualMs <= hi;
            }
            const expectedMs = Date.parse(rawValue);
            if (isNaN(expectedMs)) return false;
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
