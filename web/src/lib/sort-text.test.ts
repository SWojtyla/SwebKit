import { describe, expect, it } from "vitest";
import { createElement, Fragment } from "react";
import { extractSortText } from "./sort-text";

describe("extractSortText", () => {
    it("returns primitives as text", () => {
        expect(extractSortText("hello")).toBe("hello");
        expect(extractSortText(42)).toBe("42");
    });

    it("returns an empty string for invisible nodes", () => {
        expect(extractSortText(null)).toBe("");
        expect(extractSortText(undefined)).toBe("");
        expect(extractSortText(true)).toBe("");
        expect(extractSortText(false)).toBe("");
    });

    it("walks element children recursively", () => {
        const cell = createElement(
            "span",
            { className: "x" },
            createElement("b", null, "3/"),
            "4",
        );
        expect(extractSortText(cell)).toBe("3/4");
    });

    it("flattens arrays and fragments", () => {
        const cell = createElement(
            Fragment,
            null,
            ["a", 1, createElement("i", null, "z")],
            "-end",
        );
        expect(extractSortText(cell)).toBe("a1z-end");
    });

    it("returns empty for elements without text children", () => {
        expect(extractSortText(createElement("hr"))).toBe("");
    });
});

