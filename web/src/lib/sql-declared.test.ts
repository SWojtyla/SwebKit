import { describe, expect, it } from "vitest";
import {
    canonicalDeclaredEntry,
    mergeObjectColumns,
    parseDeclaredEntry,
    parseDeclaredObjectsText,
    quoteSqlIdent,
} from "./sql-declared";
import type { SqlSchemaModel } from "./types";

describe("parseDeclaredEntry", () => {
    it("parses a schema-qualified name", () => {
        expect(parseDeclaredEntry("prd.v_orders")).toEqual({
            schema: "prd",
            name: "v_orders",
            isProcedure: false,
        });
    });

    it("parses an exec-prefixed procedure", () => {
        expect(parseDeclaredEntry("exec:prd.p_recalc")).toEqual({
            schema: "prd",
            name: "p_recalc",
            isProcedure: true,
        });
        // Prefix is case-insensitive; names keep their case.
        expect(parseDeclaredEntry("EXEC:Sales.P1")!.isProcedure).toBe(true);
    });

    it.each([
        "",
        "   ",
        "v_orders", // unqualified
        "prd.", // missing object
        ".v_orders", // missing schema
        "a.b.c", // three-part
        "prd.v-audit", // '-' isn't an identifier char
        "prd.9lives", // can't start with a digit
        "[prd].[v_orders]", // brackets aren't part of the grammar
        "exec:", // prefix with nothing
        "exec:prd", // prefix but unqualified
    ])("rejects %j", (raw) => {
        expect(parseDeclaredEntry(raw)).toBeNull();
    });

    it("canonicalizes to lowercase exec: / bare schema.name", () => {
        expect(
            canonicalDeclaredEntry({ schema: "prd", name: "p_recalc", isProcedure: true }),
        ).toBe("exec:prd.p_recalc");
        expect(
            canonicalDeclaredEntry({ schema: "prd", name: "v_orders", isProcedure: false }),
        ).toBe("prd.v_orders");
    });
});

describe("parseDeclaredObjectsText", () => {
    it("splits lines, trims paste whitespace, dedupes case-insensitively", () => {
        const { entries, invalid } = parseDeclaredObjectsText(
            " prd.v_orders \r\nEXEC:prd.p_recalc\nprd.V_ORDERS\n\n",
        );
        expect(entries).toEqual(["prd.v_orders", "exec:prd.p_recalc"]);
        expect(invalid).toEqual([]);
    });

    it("collects invalid lines instead of dropping them silently", () => {
        const { entries, invalid } = parseDeclaredObjectsText(
            "prd.v_orders\nnot an object\na.b.c",
        );
        expect(entries).toEqual(["prd.v_orders"]);
        expect(invalid).toEqual(["not an object", "a.b.c"]);
    });
});

describe("quoteSqlIdent", () => {
    it("bracket-quotes with ] escaping", () => {
        expect(quoteSqlIdent("v_orders")).toBe("[v_orders]");
        expect(quoteSqlIdent("weird]name")).toBe("[weird]]name]");
    });
});

describe("mergeObjectColumns", () => {
    const schema: SqlSchemaModel = {
        database: "orders",
        schemas: [
            {
                name: "prd",
                objects: [
                    {
                        name: "v_orders",
                        kind: "table",
                        isDeclared: true,
                        columns: [],
                        indexes: [],
                        foreignKeys: [],
                    },
                ],
            },
        ],
    };

    it("writes columns into the matching object so autocomplete sees them", () => {
        const merged = mergeObjectColumns(schema, "prd", "v_orders", [
            { name: "id", dataType: "int", isNullable: false, isPrimaryKey: false },
        ]);
        expect(merged!.schemas[0].objects[0].columns).toHaveLength(1);
        // Original model untouched — the cache entry must be a new object.
        expect(schema.schemas[0].objects[0].columns).toHaveLength(0);
    });

    it("matches schema/object case-insensitively and no-ops on misses", () => {
        const hit = mergeObjectColumns(schema, "PRD", "V_ORDERS", [
            { name: "id", dataType: "int", isNullable: false, isPrimaryKey: false },
        ]);
        expect(hit!.schemas[0].objects[0].columns).toHaveLength(1);
        expect(mergeObjectColumns(schema, "nope", "v_orders", [])).toBe(schema);
        expect(mergeObjectColumns(undefined, "prd", "v_orders", [])).toBeUndefined();
    });
});
