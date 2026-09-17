import { describe, expect, it } from "vitest";
import { buildSelectQuery, quoteSqlIdentifier, sqlLiteral } from "./sql-query-builder";

describe("sql query builder", () => {
  it("quotes identifiers including closing brackets", () => {
    expect(quoteSqlIdentifier("order]detail")).toBe("[order]]detail]");
  });

  it("formats literals without allowing quote injection", () => {
    expect(sqlLiteral("42")).toBe("42");
    expect(sqlLiteral("NULL")).toBe("NULL");
    expect(sqlLiteral("true")).toBe("1");
    expect(sqlLiteral("O'Brien'; DROP TABLE x;--")).toBe("'O''Brien''; DROP TABLE x;--'");
  });

  it("builds a SELECT with filters, ordering, and a bounded TOP", () => {
    expect(buildSelectQuery({
      schema: "sales",
      table: "orders",
      columns: ["id", "created at"],
      filters: [
        { column: "status", operator: "=", value: "open" },
        { column: "deleted_at", operator: "IS NULL", value: "ignored" },
      ],
      orderBy: "created at",
      descending: true,
      top: 50,
    })).toBe([
      "SELECT TOP (50) [id], [created at]",
      "FROM [sales].[orders]",
      "WHERE [status] = 'open'",
      "  AND [deleted_at] IS NULL",
      "ORDER BY [created at] DESC;",
    ].join("\n"));
  });

  it("emits all columns and clamps top", () => {
    expect(buildSelectQuery({ schema: "dbo", table: "users", columns: [], top: 99999 }))
      .toBe("SELECT TOP (5000) *\nFROM [dbo].[users];");
  });
});
