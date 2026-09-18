import { describe, it, expect } from "vitest";
import {
    detectNewItemId,
    filterItems,
    groupItems,
    resolveSelectedItem,
} from "./profile-list-utils";

interface Row {
    id: string;
    name: string;
    target: string;
}

const rows: Row[] = [
    { id: "a", name: "Alpha", target: "one.local" },
    { id: "b", name: "Beta", target: "two.local" },
    { id: "c", name: "Gamma", target: "three.local" },
];

const getKey = (r: Row) => r.id;

describe("resolveSelectedItem", () => {
    it("returns the explicitly selected item when it exists", () => {
        expect(resolveSelectedItem(rows, "b", getKey)?.id).toBe("b");
    });

    it("falls back to the active item when the selection is gone", () => {
        const isActive = (r: Row) => r.id === "c";
        expect(resolveSelectedItem(rows, "removed", getKey, isActive)?.id).toBe(
            "c",
        );
    });

    it("falls back to the first item when nothing is selected or active", () => {
        expect(resolveSelectedItem(rows, null, getKey)?.id).toBe("a");
    });

    it("returns undefined for an empty list", () => {
        expect(resolveSelectedItem([], null, getKey)).toBeUndefined();
    });
});

describe("detectNewItemId", () => {
    it("returns null on the first render (null previous set)", () => {
        expect(detectNewItemId(rows, getKey, null)).toBeNull();
    });

    it("detects the key that wasn't there before", () => {
        const prev = new Set(["a", "b"]);
        expect(detectNewItemId(rows, getKey, prev)).toBe("c");
    });

    it("returns null when nothing was added", () => {
        const prev = new Set(["a", "b", "c"]);
        expect(detectNewItemId(rows.slice(0, 2), getKey, prev)).toBeNull();
    });
});

describe("filterItems", () => {
    const fields = (r: Row) => [r.name, r.target];

    it("returns everything for a blank term", () => {
        expect(filterItems(rows, "  ", fields)).toHaveLength(3);
    });

    it("matches case-insensitively across the provided fields", () => {
        expect(filterItems(rows, "TWO", fields).map((r) => r.id)).toEqual(["b"]);
        expect(filterItems(rows, "alpha", fields).map((r) => r.id)).toEqual([
            "a",
        ]);
    });

    it("returns empty when nothing matches", () => {
        expect(filterItems(rows, "zzz", fields)).toEqual([]);
    });
});

describe("groupItems", () => {
    it("buckets by key and sorts groups alphabetically", () => {
        const items = [
            { id: "1", g: "b" },
            { id: "2", g: "a" },
            { id: "3", g: "b" },
        ];
        const groups = groupItems(items, (i) => i.g);
        expect(groups.map(([k]) => k)).toEqual(["a", "b"]);
        expect(groups[1][1].map((i) => i.id)).toEqual(["1", "3"]);
    });
});
