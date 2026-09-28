import { describe, expect, it } from "vitest";
import {
    formatDurationSeconds,
    suppressionRatio,
    firingsBarHeights,
    severityShares,
} from "./opsSummaryFormat";
import type { AlertHistoryBucket } from "../../lib/api";

const bucket = (fired: number, suppressed = 0): AlertHistoryBucket => ({
    bucketStartUtc: "2026-03-10T00:00:00Z",
    fired,
    suppressed,
});

describe("formatDurationSeconds", () => {
    it("returns an em dash for null/undefined — never fabricates a zero", () => {
        expect(formatDurationSeconds(null)).toBe("—");
        expect(formatDurationSeconds(undefined)).toBe("—");
    });

    it("formats seconds, minutes, hours, and days", () => {
        expect(formatDurationSeconds(45)).toBe("45s");
        expect(formatDurationSeconds(720)).toBe("12m");
        expect(formatDurationSeconds(754)).toBe("12m 34s");
        expect(formatDurationSeconds(7200)).toBe("2h");
        expect(formatDurationSeconds(8400)).toBe("2h 20m");
        expect(formatDurationSeconds(180000)).toBe("2d 2h");
    });

    it("clamps negatives to zero", () => {
        expect(formatDurationSeconds(-5)).toBe("0s");
    });
});

describe("suppressionRatio", () => {
    it("is suppressed / (fired + suppressed)", () => {
        expect(suppressionRatio(6, 2)).toBeCloseTo(0.25);
        expect(suppressionRatio(0, 3)).toBe(1);
    });

    it("returns 0 with no firings at all", () => {
        expect(suppressionRatio(0, 0)).toBe(0);
    });
});

describe("firingsBarHeights", () => {
    it("scales the busiest bucket to 100 and keeps others proportional", () => {
        const heights = firingsBarHeights([
            bucket(2),
            bucket(4, 4), // busiest: 8 total
            bucket(0),
        ]);
        expect(heights).toEqual([25, 100, 0]);
    });

    it("returns zeros for an empty window — never NaN", () => {
        expect(firingsBarHeights([bucket(0), bucket(0)])).toEqual([0, 0]);
        expect(firingsBarHeights([])).toEqual([]);
    });
});

describe("severityShares", () => {
    it("normalizes counts to 0..1 shares", () => {
        expect(severityShares({ Critical: 3, Warning: 1 })).toEqual({
            Critical: 0.75,
            Warning: 0.25,
        });
    });

    it("returns {} when nothing fired", () => {
        expect(severityShares({})).toEqual({});
        expect(severityShares({ Warning: 0 })).toEqual({});
    });
});
