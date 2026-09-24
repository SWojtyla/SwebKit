import { describe, it, expect } from "vitest";
import {
    formatLocalDate,
    formatLocalDateTime,
    formatLocalDateTimeSeconds,
    formatLocalTime,
    localTimeZoneAbbrev,
    localTimeZoneName,
    localUtcOffsetLabel,
} from "./datetime";

const SAMPLE = "2026-03-21T14:32:45Z";

describe("datetime helpers", () => {
    it("returns an empty string for missing or invalid input", () => {
        for (const bad of [null, undefined, "", "not-a-date", NaN]) {
            expect(formatLocalDateTime(bad)).toBe("");
            expect(formatLocalDate(bad)).toBe("");
            expect(formatLocalTime(bad)).toBe("");
        }
    });

    it("formats valid input in the local timezone", () => {
        expect(formatLocalDateTime(SAMPLE)).toContain("2026");
        expect(formatLocalDate(SAMPLE)).toContain("2026");
        expect(formatLocalTime(SAMPLE)).toMatch(/\d{2}:\d{2}:\d{2}/);
        expect(formatLocalDateTimeSeconds(SAMPLE)).toContain("2026");
    });

    it("accepts Date and epoch-millis input too", () => {
        const date = new Date(SAMPLE);
        expect(formatLocalDateTime(date)).toBe(formatLocalDateTime(SAMPLE));
        expect(formatLocalDateTime(date.getTime())).toBe(
            formatLocalDateTime(SAMPLE),
        );
    });

    it("reports the local timezone identity", () => {
        expect(localTimeZoneName()).toBeTruthy();
        expect(localTimeZoneAbbrev()).toBeTruthy();
        expect(localUtcOffsetLabel()).toMatch(/^UTC([+-]\d{2}:\d{2})?$/);
    });
});
