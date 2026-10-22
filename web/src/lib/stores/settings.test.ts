import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
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

describe("boot theme mirror", () => {
    let storage: Map<string, string>;
    let classes: Set<string>;

    beforeEach(() => {
        storage = new Map();
        classes = new Set();
        vi.stubGlobal("localStorage", {
            getItem: (k: string) => storage.get(k) ?? null,
            setItem: (k: string, v: string) => void storage.set(k, v),
        });
        vi.stubGlobal("document", {
            documentElement: {
                classList: {
                    add: (...c: string[]) => c.forEach((x) => classes.add(x)),
                    remove: (...c: string[]) =>
                        c.forEach((x) => classes.delete(x)),
                },
            },
        });
        vi.resetModules();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it("starts the store on the mirrored theme so first paint matches", async () => {
        storage.set("view-pref:boot-theme", JSON.stringify("clay"));
        const { useSettingsStore } = await import("./settings");
        expect(useSettingsStore.getState().theme).toBe("clay");
    });

    it("falls back to dark when nothing (or a junk id) is mirrored", async () => {
        storage.set("view-pref:boot-theme", JSON.stringify("solarized"));
        const { useSettingsStore } = await import("./settings");
        expect(useSettingsStore.getState().theme).toBe("dark");
    });

    it("writes the mirror whenever the theme changes", async () => {
        const { useSettingsStore } = await import("./settings");
        useSettingsStore.getState().setTheme("clay-dark");
        expect(storage.get("view-pref:boot-theme")).toBe(
            JSON.stringify("clay-dark"),
        );
        expect([...classes].sort()).toEqual(["clay", "dark"]);
    });
});
