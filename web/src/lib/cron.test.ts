import { describe, it, expect } from "vitest";
import {
    parseCronExpression,
    isValidCronExpression,
    nextCronRun,
    describeCronExpression,
    detectCronPreset,
    buildCronExpression,
} from "./cron";

describe("parseCronExpression", () => {
    it("parses a plain 5-field expression", () => {
        const cron = parseCronExpression("30 2 * * *");
        expect(cron).not.toBeNull();
        expect(cron!.minute.values.has(30)).toBe(true);
        expect(cron!.hour.values.has(2)).toBe(true);
        expect(cron!.dayOfMonth.wildcard).toBe(true);
        expect(cron!.dayOfWeek.wildcard).toBe(true);
    });

    it("parses steps, ranges and lists", () => {
        const cron = parseCronExpression("*/15 1-3,5 * * MON-FRI");
        expect(cron).not.toBeNull();
        expect(cron!.minute.values.has(0)).toBe(true);
        expect(cron!.minute.values.has(45)).toBe(true);
        expect(cron!.minute.values.size).toBe(4);
        expect(cron!.hour.values.has(2)).toBe(true);
        expect(cron!.hour.values.has(5)).toBe(true);
        expect(cron!.dayOfWeek.values.has(5)).toBe(true);
        expect(cron!.dayOfWeek.values.has(6)).toBe(false);
    });

    it("accepts day-of-week names and normalizes 7 to Sunday", () => {
        const byName = parseCronExpression("0 9 * * MON");
        expect(byName!.dayOfWeek.values.has(1)).toBe(true);
        const seven = parseCronExpression("0 9 * * 7");
        expect(seven!.dayOfWeek.values.has(0)).toBe(true);
        expect(seven!.dayOfWeek.values.has(7)).toBe(false);
    });

    it("accepts month names case-insensitively", () => {
        const cron = parseCronExpression("0 0 1 Jan *");
        expect(cron!.month.values.has(1)).toBe(true);
    });

    it("supports @macros", () => {
        const daily = parseCronExpression("@daily");
        expect(daily).not.toBeNull();
        expect(daily!.minute.values.has(0)).toBe(true);
        expect(daily!.hour.values.has(0)).toBe(true);
        const hourly = parseCronExpression("@hourly");
        expect(hourly!.minute.values.has(0)).toBe(true);
        expect(hourly!.hour.wildcard).toBe(true);
    });

    it("rejects invalid expressions", () => {
        expect(parseCronExpression("")).toBeNull();
        expect(parseCronExpression("* * *")).toBeNull();
        expect(parseCronExpression("61 * * * *")).toBeNull();
        expect(parseCronExpression("* 25 * * *")).toBeNull();
        expect(parseCronExpression("*/0 * * * *")).toBeNull();
        expect(parseCronExpression("5-2 * * * *")).toBeNull();
        expect(parseCronExpression("a b c d e")).toBeNull();
        expect(parseCronExpression("@reboot")).toBeNull();
        // seconds/year fields and L/W/# extensions are not part of the k8s grammar
        expect(parseCronExpression("0 0 0 * * * *")).toBeNull();
        expect(parseCronExpression("0 0 L * *")).toBeNull();
    });

    it("treats ? as a wildcard", () => {
        const cron = parseCronExpression("0 0 ? * MON");
        expect(cron!.dayOfMonth.wildcard).toBe(true);
    });
});

describe("isValidCronExpression", () => {
    it("accepts valid expressions and macros", () => {
        expect(isValidCronExpression("*/5 * * * *")).toBe(true);
        expect(isValidCronExpression("0 9 1-15 * MON-FRI")).toBe(true);
        expect(isValidCronExpression("@weekly")).toBe(true);
    });

    it("rejects invalid and non-scheduling macros", () => {
        expect(isValidCronExpression("not a cron")).toBe(false);
        expect(isValidCronExpression("@reboot")).toBe(false);
    });
});

describe("nextCronRun", () => {
    // Reference instant: 2026-03-21 is a Saturday.
    const from = new Date(Date.UTC(2026, 2, 21, 10, 30, 0));

    it("returns the next minute for * * * * *", () => {
        const next = nextCronRun("* * * * *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-21T10:31:00.000Z");
    });

    it("returns same-day time when the daily time is still ahead", () => {
        const next = nextCronRun("0 12 * * *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-21T12:00:00.000Z");
    });

    it("rolls to the next day when the daily time has passed", () => {
        const next = nextCronRun("0 9 * * *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-22T09:00:00.000Z");
    });

    it("handles */n steps", () => {
        const next = nextCronRun("*/20 * * * *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-21T10:40:00.000Z");
    });

    it("evaluates day-of-week schedules", () => {
        // Next Monday after Sat 2026-03-21 is Mar 23.
        const next = nextCronRun("30 8 * * MON", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-23T08:30:00.000Z");
    });

    it("evaluates day-of-month schedules across a month boundary", () => {
        const next = nextCronRun("0 0 1 * *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-04-01T00:00:00.000Z");
    });

    it("evaluates yearly schedules across a year boundary", () => {
        const next = nextCronRun("0 0 1 1 *", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2027-01-01T00:00:00.000Z");
    });

    it("applies OR semantics when dom and dow are both restricted", () => {
        // "13th of the month OR any Friday". From Sat Mar 21 the 13th was past,
        // the next Friday is Mar 27.
        const next = nextCronRun("0 9 13 * FRI", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-27T09:00:00.000Z");
    });

    it("evaluates @hourly", () => {
        const next = nextCronRun("@hourly", { from, timeZone: "UTC" });
        expect(next?.toISOString()).toBe("2026-03-21T11:00:00.000Z");
    });

    it("evaluates in a non-UTC timezone when spec.timeZone is set", () => {
        // "Daily at 09:00 Europe/Brussels" — Brussels is UTC+1 in March.
        const next = nextCronRun("0 9 * * *", {
            from,
            timeZone: "Europe/Brussels",
        });
        expect(next?.toISOString()).toBe("2026-03-22T08:00:00.000Z");
    });

    it("returns null for invalid expressions and unknown zones", () => {
        expect(nextCronRun("banana", { from })).toBeNull();
        expect(
            nextCronRun("0 9 * * *", { from, timeZone: "Not/AZone" }),
        ).toBeNull();
    });
});

describe("describeCronExpression", () => {
    it("describes common shapes", () => {
        expect(describeCronExpression("* * * * *")).toBe("Every minute");
        expect(describeCronExpression("*/15 * * * *")).toBe("Every 15 minutes");
        expect(describeCronExpression("30 * * * *")).toBe("Every hour at :30");
        expect(describeCronExpression("0 9 * * *")).toBe("Daily at 09:00");
        expect(describeCronExpression("30 8 * * MON")).toBe(
            "Weekly on Monday at 08:30",
        );
        expect(describeCronExpression("0 9 15 * *")).toBe(
            "Monthly on day 15 at 09:00",
        );
        expect(describeCronExpression("0 9 25 12 *")).toBe(
            "Yearly on December 25 at 09:00",
        );
    });

    it("returns null for shapes without a friendly paraphrase", () => {
        expect(describeCronExpression("0 9 * * MON,WED,FRI")).toBe(
            "Weekly on Monday, Wednesday, Friday at 09:00",
        );
        expect(describeCronExpression("0 9,17 * * *")).toBeNull();
        expect(describeCronExpression("invalid")).toBeNull();
    });
});

describe("detectCronPreset + buildCronExpression", () => {
    it("round-trips every preset", () => {
        const cases: [string, ReturnType<typeof detectCronPreset>["preset"]][] =
            [
                ["*/5 * * * *", "everyNMinutes"],
                ["30 * * * *", "hourly"],
                ["0 9 * * *", "daily"],
                ["30 8 * * FRI", "weekly"],
                ["0 9 15 * *", "monthly"],
                ["0 9 25 12 *", "yearly"],
                ["0 9,17 * * *", "custom"],
            ];
        for (const [expr, expected] of cases) {
            expect(detectCronPreset(expr).preset, expr).toBe(expected);
        }
    });

    it("builds expressions that detect back to the same preset", () => {
        const built = [
            buildCronExpression({ preset: "everyNMinutes", everyN: 10 }),
            buildCronExpression({ preset: "hourly", minute: 45 }),
            buildCronExpression({ preset: "daily", hour: 2, minute: 0 }),
            buildCronExpression({
                preset: "weekly",
                weekday: 3,
                hour: 8,
                minute: 30,
            }),
            buildCronExpression({
                preset: "monthly",
                dayOfMonth: 1,
                hour: 0,
                minute: 0,
            }),
            buildCronExpression({
                preset: "yearly",
                month: 6,
                dayOfMonth: 15,
                hour: 12,
                minute: 0,
            }),
        ];
        for (const expr of built) {
            expect(isValidCronExpression(expr), expr).toBe(true);
        }
        expect(built[0]).toBe("*/10 * * * *");
        expect(built[3]).toBe("30 8 * * 3");
    });
});
