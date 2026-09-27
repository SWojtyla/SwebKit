import { describe, it, expect } from "vitest";
import {
    muteUntilFor,
    isRuleMuted,
    isIndefiniteMute,
    muteBadgeLabel,
    silenceEndFor,
    isSilenceCurrent,
    isSilenceActive,
    silenceScopeLabel,
    INDEFINITE_UNTIL,
} from "./silenceWindows";
import type { MonitoringSilence } from "../../lib/api";

const NOW = new Date("2026-08-03T12:00:00Z");

describe("muteUntilFor", () => {
    it("'1h' adds exactly one hour", () => {
        expect(muteUntilFor("1h", NOW)).toBe("2026-08-03T13:00:00.000Z");
    });

    it("'tomorrow' lands on the next local midnight", () => {
        const until = new Date(muteUntilFor("tomorrow", NOW));
        expect(until.getHours()).toBe(0);
        expect(until.getMinutes()).toBe(0);
        expect(until.getTime()).toBeGreaterThan(NOW.getTime());
        expect(until.getDate()).not.toBe(NOW.getDate());
    });

    it("'indefinite' returns the far-future sentinel", () => {
        expect(muteUntilFor("indefinite", NOW)).toBe(INDEFINITE_UNTIL);
    });
});

describe("isRuleMuted", () => {
    it("is false for null, undefined, and empty", () => {
        expect(isRuleMuted(null, NOW)).toBe(false);
        expect(isRuleMuted(undefined, NOW)).toBe(false);
        expect(isRuleMuted("", NOW)).toBe(false);
    });

    it("is true only while the timestamp is strictly in the future", () => {
        expect(isRuleMuted("2026-08-03T13:00:00Z", NOW)).toBe(true);
        expect(isRuleMuted("2026-08-03T11:00:00Z", NOW)).toBe(false);
        expect(isRuleMuted("2026-08-03T12:00:00Z", NOW)).toBe(false); // exactly now = expired
    });

    it("is false for an unparseable timestamp", () => {
        expect(isRuleMuted("not a date", NOW)).toBe(false);
    });
});

describe("isIndefiniteMute / muteBadgeLabel", () => {
    it("treats the far-future sentinel as indefinite", () => {
        expect(isIndefiniteMute(INDEFINITE_UNTIL)).toBe(true);
        expect(isIndefiniteMute("2027-01-01T00:00:00Z")).toBe(false);
        expect(isIndefiniteMute(null)).toBe(false);
    });

    it("renders 'muted' for indefinite, a date label otherwise", () => {
        expect(muteBadgeLabel(INDEFINITE_UNTIL, NOW)).toBe("muted");
        expect(muteBadgeLabel("2026-08-03T13:00:00Z", NOW)).toMatch(
            /^muted until /,
        );
        expect(muteBadgeLabel(null, NOW)).toBe("");
        expect(muteBadgeLabel("2026-08-03T11:00:00Z", NOW)).toBe(""); // expired = no badge
    });
});

describe("silenceEndFor", () => {
    it("adds the requested hours", () => {
        expect(silenceEndFor("1h", NOW)).toBe("2026-08-03T13:00:00.000Z");
        expect(silenceEndFor("4h", NOW)).toBe("2026-08-03T16:00:00.000Z");
        expect(silenceEndFor("8h", NOW)).toBe("2026-08-03T20:00:00.000Z");
    });

    it("'tomorrow' lands on the next local midnight", () => {
        const end = new Date(silenceEndFor("tomorrow", NOW));
        expect(end.getHours()).toBe(0);
        expect(end.getTime()).toBeGreaterThan(NOW.getTime());
    });
});

function silence(overrides: Partial<MonitoringSilence>): MonitoringSilence {
    return {
        id: "s1",
        startUtc: "2026-08-03T11:00:00Z",
        endUtc: "2026-08-03T13:00:00Z",
        ruleIds: null,
        reason: "deploy",
        ...overrides,
    };
}

describe("isSilenceCurrent / isSilenceActive", () => {
    it("an expired window is neither current nor active", () => {
        const past = silence({ endUtc: "2026-08-03T11:59:00Z" });
        expect(isSilenceCurrent(past, NOW)).toBe(false);
        expect(isSilenceActive(past, NOW)).toBe(false);
    });

    it("an in-flight window is current and active", () => {
        const active = silence({});
        expect(isSilenceCurrent(active, NOW)).toBe(true);
        expect(isSilenceActive(active, NOW)).toBe(true);
    });

    it("an upcoming window is current but not yet active", () => {
        const upcoming = silence({
            startUtc: "2026-08-03T13:00:00Z",
            endUtc: "2026-08-03T15:00:00Z",
        });
        expect(isSilenceCurrent(upcoming, NOW)).toBe(true);
        expect(isSilenceActive(upcoming, NOW)).toBe(false);
    });
});

describe("silenceScopeLabel", () => {
    it("'All rules' for null or empty ruleIds", () => {
        expect(silenceScopeLabel(silence({ ruleIds: null }), (id) => id)).toBe(
            "All rules",
        );
        expect(silenceScopeLabel(silence({ ruleIds: [] }), (id) => id)).toBe(
            "All rules",
        );
    });

    it("joins resolved rule names", () => {
        const s = silence({ ruleIds: ["r1", "r2"] });
        expect(
            silenceScopeLabel(s, (id) => `Rule ${id.toUpperCase()}`),
        ).toBe("Rule R1, Rule R2");
    });
});
