import { describe, it, expect } from "vitest";
import { isTheme, migrateTheme } from "./settings";

describe("isTheme", () => {
    it("accepts the four SwebOS theme ids", () => {
        expect(isTheme("light")).toBe(true);
        expect(isTheme("dark")).toBe(true);
        expect(isTheme("clay")).toBe(true);
        expect(isTheme("clay-dark")).toBe(true);
    });

    it("rejects retired ids, unknown strings, and empty values", () => {
        expect(isTheme("fancy")).toBe(false);
        expect(isTheme("fathom-dark")).toBe(false);
        expect(isTheme("cascade-light")).toBe(false);
        expect(isTheme("nope")).toBe(false);
        expect(isTheme("")).toBe(false);
        expect(isTheme(null)).toBe(false);
        expect(isTheme(undefined)).toBe(false);
    });
});

describe("migrateTheme", () => {
    it("passes live ids through unchanged", () => {
        expect(migrateTheme("light")).toBe("light");
        expect(migrateTheme("dark")).toBe("dark");
        expect(migrateTheme("clay")).toBe("clay");
        expect(migrateTheme("clay-dark")).toBe("clay-dark");
    });

    it("maps retired dark themes to aurora dark", () => {
        for (const id of [
            "fancy",
            "fathom-dark",
            "letterpress-dark",
            "cascade-dark",
        ]) {
            expect(migrateTheme(id)).toBe("dark");
        }
    });

    it("maps retired light themes to aurora light", () => {
        for (const id of [
            "fathom-light",
            "letterpress-light",
            "cascade-light",
        ]) {
            expect(migrateTheme(id)).toBe("light");
        }
    });

    it("returns null for unknown or missing values so the caller keeps its default", () => {
        expect(migrateTheme("solarized")).toBeNull();
        expect(migrateTheme("")).toBeNull();
        expect(migrateTheme(null)).toBeNull();
        expect(migrateTheme(undefined)).toBeNull();
    });
});
