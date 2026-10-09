import { describe, it, expect } from "vitest";
import {
    applyFilters,
    hasActiveFilters,
    parseFlexibleDate,
} from "./filterLogic";
import {
    createFilterRule,
    type AdvancedFilterRule,
    type FilterOperator,
} from "./filterTypes";
import type { SbMessage } from "@/lib/types";

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

function msg(props: Record<string, unknown>): SbMessage {
    return {
        messageId: "m-1",
        correlationId: null,
        subject: null,
        contentType: null,
        body: "",
        applicationProperties: props,
        systemProperties: null,
        deadLetterReason: null,
        deadLetterErrorDescription: null,
        enqueuedAt: "2026-09-30T04:00:00Z",
        deliveryCount: 1,
        lockToken: null,
        sequenceNumber: 1,
        sessionId: null,
    };
}

function propRule(
    propertyName: string,
    operator: FilterOperator,
    value: string,
): AdvancedFilterRule {
    return {
        ...createFilterRule(),
        field: "application-property",
        propertyName,
        operator,
        value,
    };
}

function filtered(
    messages: SbMessage[],
    rules: AdvancedFilterRule[],
): SbMessage[] {
    return applyFilters(messages, "", rules, true, null);
}

describe("parseFlexibleDate", () => {
    it("parses ISO 8601", () => {
        expect(parseFlexibleDate("2026-09-30T04:25:38.948Z")).toBe(
            Date.parse("2026-09-30T04:25:38.948Z"),
        );
    });

    it("parses the NServiceBus 'yyyy-MM-dd HH:mm:ss:ffffff Z' format", () => {
        expect(parseFlexibleDate("2026-09-30 04:25:38:948134 Z")).toBe(
            Date.parse("2026-09-30T04:25:38.948134Z"),
        );
    });

    it("parses space-separated datetimes without a zone", () => {
        expect(parseFlexibleDate("2026-09-30 04:25:38")).toBe(
            Date.parse("2026-09-30T04:25:38"),
        );
    });

    it("returns NaN for garbage", () => {
        expect(Number.isNaN(parseFlexibleDate("not a date"))).toBe(true);
    });
});

describe("application-property operators", () => {
    const sent = msg({
        "NServiceBus.TimeSent": "2026-09-30 04:25:38:948134 Z",
        attempt: "3",
        source: "web-checkout",
    });

    it("'after' compares a date-typed property against an ISO value", () => {
        const rule = propRule(
            "NServiceBus.TimeSent",
            "after",
            "2026-09-30T04:00:00Z",
        );
        expect(filtered([sent], [rule])).toHaveLength(1);
    });

    it("'before' excludes a property later than the bound", () => {
        const rule = propRule(
            "NServiceBus.TimeSent",
            "before",
            "2026-09-30T04:00:00Z",
        );
        expect(filtered([sent], [rule])).toHaveLength(0);
    });

    it("date operators accept the NServiceBus format on the filter side too", () => {
        const rule = propRule(
            "NServiceBus.TimeSent",
            "after",
            "2026-09-30 04:00:00:000000 Z",
        );
        expect(filtered([sent], [rule])).toHaveLength(1);
    });

    it("'on-or-after' is inclusive", () => {
        const rule = propRule(
            "NServiceBus.TimeSent",
            "on-or-after",
            "2026-09-30 04:25:38:948134 Z",
        );
        expect(filtered([sent], [rule])).toHaveLength(1);
    });

    it("numeric operators compare a numeric-typed property", () => {
        expect(filtered([sent], [propRule("attempt", "gt", "2")])).toHaveLength(
            1,
        );
        expect(filtered([sent], [propRule("attempt", "lt", "3")])).toHaveLength(
            0,
        );
        expect(
            filtered([sent], [propRule("attempt", "lte", "3")]),
        ).toHaveLength(1);
    });

    it("date operators never match a non-date property", () => {
        const rule = propRule("source", "after", "2020-01-01T00:00:00Z");
        expect(filtered([sent], [rule])).toHaveLength(0);
    });

    it("numeric operators never match a non-numeric property", () => {
        const rule = propRule("source", "gt", "0");
        expect(filtered([sent], [rule])).toHaveLength(0);
    });

    it("equals stays a case-insensitive text comparison", () => {
        const rule = propRule("source", "equals", "Web-Checkout");
        expect(filtered([sent], [rule])).toHaveLength(1);
    });

    it("never matches when the property is absent", () => {
        const rule = propRule("missing", "after", "2020-01-01T00:00:00Z");
        expect(filtered([sent], [rule])).toHaveLength(0);
    });
});
