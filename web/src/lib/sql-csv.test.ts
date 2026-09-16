import { describe, expect, it } from "vitest";
import { cellText, toCsv } from "./sql-csv";
import type { SqlQueryResult } from "@/lib/types";

function result(rows: Record<string, unknown>[]): SqlQueryResult {
    return {
        columns: ["id", "name", "note"].map((name) => ({
            name,
            typeName: "nvarchar",
        })),
        rows,
        truncated: false,
        elapsedMs: 1,
        rowsAffected: -1,
    };
}

describe("cellText", () => {
    it("renders null and undefined as NULL", () => {
        expect(cellText(null)).toBe("NULL");
        expect(cellText(undefined)).toBe("NULL");
    });

    it("stringifies objects", () => {
        expect(cellText({ a: 1 })).toBe('{"a":1}');
    });

    it("passes through primitives", () => {
        expect(cellText(42)).toBe("42");
        expect(cellText("abc")).toBe("abc");
    });
});

describe("toCsv", () => {
    it("emits a header row and one line per row", () => {
        const csv = toCsv(result([{ id: 1, name: "a", note: "x" }]));
        expect(csv).toBe("id,name,note\n1,a,x");
    });

    it("escapes values containing commas, quotes and newlines", () => {
        const csv = toCsv(
            result([{ id: 1, name: 'a,"b"', note: "line1\nline2" }]),
        );
        expect(csv).toBe('id,name,note\n1,"a,""b""","line1\nline2"');
    });

    it("renders NULL for missing values", () => {
        const csv = toCsv(result([{ id: 1, name: "a" }]));
        expect(csv).toBe("id,name,note\n1,a,NULL");
    });
});

