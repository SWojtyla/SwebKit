import { describe, it, expect, vi } from "vitest";
import {
    applyFilters,
    hasActiveFilters,
    isScheduledMessage,
} from "./filterLogic";
import {
    createFilterRule,
    describeRule,
    isRuleConfigured,
    parseDurationMs,
    splitRange,
    type AdvancedFilterRule,
    type FilterField,
    type FilterOperator,
} from "./filterTypes";
import type { SbMessage } from "@/lib/types";

function msg(overrides: Partial<SbMessage> = {}): SbMessage {
    return {
        messageId: "m-1",
        correlationId: null,
        subject: null,
        contentType: null,
        body: "{}",
        applicationProperties: {},
        systemProperties: null,
        deadLetterReason: null,
        deadLetterErrorDescription: null,
        enqueuedAt: "2026-01-15T10:00:00Z",
        deliveryCount: 1,
        lockToken: null,
        sequenceNumber: 42,
        sessionId: null,
        ...overrides,
    };
}

function rule(
    field: FilterField,
    operator: FilterOperator,
    value: string,
    propertyName = "",
): AdvancedFilterRule {
    return { ...createFilterRule(), field, operator, propertyName, value };
}

const filtered = (messages: SbMessage[], rules: AdvancedFilterRule[]) =>
    applyFilters(messages, "", rules, true, null);

describe("hasActiveFilters", () => {
    it("is false when nothing is set", () => {
        expect(hasActiveFilters("", null, [])).toBe(false);
    });

    it("is true when the text filter has non-whitespace content", () => {
        expect(hasActiveFilters("order", null, [])).toBe(true);
    });

    it("is false when the text filter is only whitespace", () => {
        expect(hasActiveFilters("   ", null, [])).toBe(false);
    });

    it("is true when a session is pinned", () => {
        expect(hasActiveFilters("", "session-1", [])).toBe(true);
    });

    it("is true when an advanced rule is configured", () => {
        const rule: AdvancedFilterRule = {
            ...createFilterRule(),
            propertyName: "orderId",
            value: "ORD-1",
        };
        expect(hasActiveFilters("", null, [rule])).toBe(true);
    });

    it("ignores advanced rules that aren't configured yet (e.g. an added-but-empty rule)", () => {
        const rule = createFilterRule();
        expect(hasActiveFilters("", null, [rule])).toBe(false);
    });
});

describe("text field rules", () => {
    it("matches message-id with contains/equals/starts-with", () => {
        const m = msg({ messageId: "ORD-8842-x" });
        expect(
            filtered([m], [rule("message-id", "contains", "8842")]),
        ).toHaveLength(1);
        expect(
            filtered([m], [rule("message-id", "equals", "ord-8842-x")]),
        ).toHaveLength(1);
        expect(
            filtered([m], [rule("message-id", "starts-with", "ORD-")]),
        ).toHaveLength(1);
        expect(
            filtered([m], [rule("message-id", "starts-with", "x-ORD")]),
        ).toHaveLength(0);
        expect(
            filtered([m], [rule("message-id", "not-equals", "other")]),
        ).toHaveLength(1);
    });

    it("subject and correlation-id rules don't match when the field is absent", () => {
        const m = msg();
        expect(filtered([m], [rule("subject", "contains", "x")])).toHaveLength(
            0,
        );
        expect(
            filtered([m], [rule("correlation-id", "equals", "c")]),
        ).toHaveLength(0);
        const withFields = msg({
            subject: "Billing event",
            correlationId: "corr-9",
        });
        expect(
            filtered(
                [withFields],
                [rule("subject", "regex", "billing\\s+event")],
            ),
        ).toHaveLength(1);
        expect(
            filtered(
                [withFields],
                [rule("correlation-id", "contains", "orr-9")],
            ),
        ).toHaveLength(1);
    });

    it("an invalid regex never throws and matches nothing", () => {
        expect(
            filtered([msg()], [rule("message-id", "regex", "([")]),
        ).toHaveLength(0);
    });
});

describe("numeric field rules", () => {
    const m = () => msg({ deliveryCount: 3, sequenceNumber: 100 });

    it("compares delivery-count with every numeric operator", () => {
        const cases: [FilterOperator, string, boolean][] = [
            ["equals", "3", true],
            ["not-equals", "3", false],
            ["gt", "2", true],
            ["gt", "3", false],
            ["gte", "3", true],
            ["lt", "4", true],
            ["lt", "3", false],
            ["lte", "3", true],
        ];
        for (const [op, value, expected] of cases) {
            expect(
                filtered([m()], [rule("delivery-count", op, value)]).length ===
                    1,
                `${op} ${value}`,
            ).toBe(expected);
        }
    });

    it("between is inclusive on both ends", () => {
        expect(
            filtered([m()], [rule("sequence-number", "between", "100,200")]),
        ).toHaveLength(1);
        expect(
            filtered([m()], [rule("sequence-number", "between", "1,100")]),
        ).toHaveLength(1);
        expect(
            filtered([m()], [rule("sequence-number", "between", "101,200")]),
        ).toHaveLength(0);
    });

    it("unparseable numbers and null sequence numbers don't match", () => {
        expect(
            filtered([m()], [rule("delivery-count", "gte", "abc")]),
        ).toHaveLength(0);
        expect(
            filtered(
                [msg({ sequenceNumber: null })],
                [rule("sequence-number", "gte", "1")],
            ),
        ).toHaveLength(0);
        expect(
            filtered([m()], [rule("sequence-number", "between", "1,abc")]),
        ).toHaveLength(0);
    });
});

describe("date field rules", () => {
    const enq = "2026-01-15T10:00:00Z";
    const m = () => msg({ enqueuedAt: enq });

    it("compares enqueued-time against absolute instants", () => {
        const cases: [FilterOperator, string, boolean][] = [
            ["before", "2026-01-15T11:00:00Z", true],
            ["before", "2026-01-15T09:00:00Z", false],
            ["on-or-before", enq, true],
            ["after", "2026-01-15T09:00:00Z", true],
            ["on-or-after", enq, true],
            ["equals", enq, true],
            ["equals", "2026-01-15T10:00:01Z", false],
        ];
        for (const [op, value, expected] of cases) {
            expect(
                filtered([m()], [rule("enqueued-time", op, value)]).length ===
                    1,
                `${op}`,
            ).toBe(expected);
        }
    });

    it("between bounds a range of instants inclusively", () => {
        const inside = rule(
            "enqueued-time",
            "between",
            "2026-01-15T09:00:00Z,2026-01-15T11:00:00Z",
        );
        const outside = rule(
            "enqueued-time",
            "between",
            "2026-01-16T00:00:00Z,2026-01-17T00:00:00Z",
        );
        expect(filtered([m()], [inside])).toHaveLength(1);
        expect(filtered([m()], [outside])).toHaveLength(0);
        expect(
            filtered(
                [m()],
                [rule("enqueued-time", "between", "not-a-date,also-not")],
            ),
        ).toHaveLength(0);
    });

    it("older-than / within-last compare against now minus the duration", () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date("2026-01-15T12:00:00Z"));
        try {
            // 10:00 is 2h before noon.
            expect(
                filtered([m()], [rule("enqueued-time", "older-than", "1h")]),
            ).toHaveLength(1);
            expect(
                filtered([m()], [rule("enqueued-time", "older-than", "3h")]),
            ).toHaveLength(0);
            expect(
                filtered([m()], [rule("enqueued-time", "within-last", "3h")]),
            ).toHaveLength(1);
            expect(
                filtered([m()], [rule("enqueued-time", "within-last", "30m")]),
            ).toHaveLength(0);
            // A bare number reads as minutes: 120m == 2h — boundary is inclusive (at-or-after).
            expect(
                filtered([m()], [rule("enqueued-time", "within-last", "120")]),
            ).toHaveLength(1);
            expect(
                filtered([m()], [rule("enqueued-time", "older-than", "soon")]),
            ).toHaveLength(0);
        } finally {
            vi.useRealTimers();
        }
    });
});

describe("scheduled-time rules and isScheduledMessage", () => {
    const future = "2099-01-01T00:00:00Z";
    const sched = () => msg({ scheduledEnqueueTime: future });

    it("scheduled-time rules skip ordinary messages", () => {
        expect(
            filtered([msg()], [rule("scheduled-time", "after", "2026-01-01")]),
        ).toHaveLength(0);
        expect(
            filtered(
                [sched()],
                [rule("scheduled-time", "after", "2026-01-01")],
            ),
        ).toHaveLength(1);
    });

    it("isScheduledMessage only counts stamps still in the future", () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date("2026-06-01T00:00:00Z"));
        try {
            expect(isScheduledMessage(msg())).toBe(false);
            expect(isScheduledMessage(sched())).toBe(true);
            expect(
                isScheduledMessage(
                    msg({ scheduledEnqueueTime: "2020-01-01T00:00:00Z" }),
                ),
            ).toBe(false);
            expect(
                isScheduledMessage(msg({ scheduledEnqueueTime: "garbage" })),
            ).toBe(false);
        } finally {
            vi.useRealTimers();
        }
    });
});

describe("rule metadata", () => {
    it("parseDurationMs handles suffixes and bare minutes", () => {
        expect(parseDurationMs("30m")).toBe(30 * 60_000);
        expect(parseDurationMs("2h")).toBe(2 * 3_600_000);
        expect(parseDurationMs("7d")).toBe(7 * 86_400_000);
        expect(parseDurationMs("45")).toBe(45 * 60_000);
        expect(parseDurationMs("soon")).toBeNull();
        expect(parseDurationMs("-5m")).toBeNull();
    });

    it("splitRange splits min,max", () => {
        expect(splitRange("1,9")).toEqual({ min: "1", max: "9" });
        expect(splitRange("1")).toEqual({ min: "1", max: "" });
    });

    it("a between rule needs both bounds before it filters", () => {
        expect(isRuleConfigured(rule("delivery-count", "between", "1,"))).toBe(
            false,
        );
        expect(isRuleConfigured(rule("delivery-count", "between", "1,9"))).toBe(
            true,
        );
    });

    it("describeRule renders chip labels", () => {
        expect(describeRule(rule("delivery-count", "gte", "3"))).toBe(
            "Delivery Count ≥ 3",
        );
        expect(describeRule(rule("enqueued-time", "older-than", "2h"))).toBe(
            "Enqueued · older than 2h",
        );
        expect(
            describeRule(
                rule("application-property", "equals", "x", "orderId"),
            ),
        ).toBe("prop:orderId = x");
        expect(describeRule(rule("sequence-number", "between", "1,9"))).toBe(
            "Sequence Number between 1 and 9",
        );
    });
});
