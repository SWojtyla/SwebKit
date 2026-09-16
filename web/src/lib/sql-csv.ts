import type { SqlQueryResult } from "@/lib/types";

export function cellText(value: unknown): string {
  if (value === null || value === undefined) return "NULL";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

export function toCsv(result: SqlQueryResult): string {
  const escape = (v: unknown) => {
    const s = cellText(v);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const header = result.columns.map((c) => escape(c.name)).join(",");
  const lines = result.rows.map((row) => result.columns.map((c) => escape(row[c.name])).join(","));
  return [header, ...lines].join("\n");
}
