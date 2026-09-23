import { describe, expect, it } from "vitest";
import {
    formatInsightTime,
    insightSeverityBadge,
    insightSeverityLabel,
} from "./aiReportFormat";

describe("insightSeverityLabel", () => {
    it("passes through the three known severities case-insensitively", () => {
        expect(insightSeverityLabel("high")).toBe("high");
        expect(insightSeverityLabel("Medium")).toBe("medium");
        expect(insightSeverityLabel("LOW")).toBe("low");
    });

    it("maps missing or free-form values to unknown", () => {
        expect(insightSeverityLabel(undefined)).toBe("unknown");
        expect(insightSeverityLabel(null)).toBe("unknown");
        expect(insightSeverityLabel("")).toBe("unknown");
        expect(insightSeverityLabel("severe")).toBe("unknown");
    });
});

describe("insightSeverityBadge", () => {
    it("gives the destructive style only to high", () => {
        expect(insightSeverityBadge("high")).toContain("bg-destructive");
        expect(insightSeverityBadge("medium")).not.toContain("bg-destructive");
        expect(insightSeverityBadge("low")).not.toContain("bg-destructive");
        expect(insightSeverityBadge(undefined)).not.toContain("bg-destructive");
    });
});

describe("formatInsightTime", () => {
    it("returns the input untouched when it is not a date", () => {
        expect(formatInsightTime("not-a-date")).toBe("not-a-date");
    });

    it("formats a valid timestamp", () => {
        expect(formatInsightTime("2026-09-21T08:33:34Z")).not.toBe(
            "2026-09-21T08:33:34Z",
        );
    });
});
