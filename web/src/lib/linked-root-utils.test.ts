import { describe, it, expect } from "vitest";
import { isWithinPath } from "./linked-root-utils";

describe("isWithinPath", () => {
    it("covers an equal path regardless of separators or case", () => {
        expect(isWithinPath("C:/Repos/api", "C:\\repos\\api")).toBe(true);
    });

    it("covers a descendant", () => {
        expect(
            isWithinPath("C:/Repos/api/collections", "C:\\Repos"),
        ).toBe(true);
    });

    it("does not cover an ancestor", () => {
        // Granting a subfolder must not unlock the repo root above it.
        expect(isWithinPath("C:/Repos", "C:/Repos/api")).toBe(false);
    });

    it("does not cover a sibling sharing a prefix", () => {
        expect(isWithinPath("C:/Repos/api2", "C:/Repos/api")).toBe(false);
    });

    it("rejects empty inputs", () => {
        expect(isWithinPath(null, "C:/Repos")).toBe(false);
        expect(isWithinPath("C:/Repos", undefined)).toBe(false);
    });
});
