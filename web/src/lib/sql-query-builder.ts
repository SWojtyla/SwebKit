export type SqlFilterOperator = "=" | "<>" | ">" | ">=" | "<" | "<=" | "LIKE" | "IS NULL" | "IS NOT NULL";

export interface SqlQueryFilter {
  column: string;
  operator: SqlFilterOperator;
  value: string;
}

export interface SqlSelectQuery {
  schema: string;
  table: string;
  columns: string[];
  filters?: SqlQueryFilter[];
  orderBy?: string;
  descending?: boolean;
  top?: number;
}

export function quoteSqlIdentifier(identifier: string): string {
  return `[${identifier.replaceAll("]", "]]").trim()}]`;
}

export function sqlLiteral(value: string): string {
  const trimmed = value.trim();
  if (/^null$/i.test(trimmed)) return "NULL";
  if (/^-?(?:\d+\.?\d*|\.\d+)$/.test(trimmed)) return trimmed;
  if (/^(?:true|false)$/i.test(trimmed)) return /^true$/i.test(trimmed) ? "1" : "0";
  return `'${value.replaceAll("'", "''")}'`;
}

export function buildSelectQuery(query: SqlSelectQuery): string {
  if (!query.schema.trim() || !query.table.trim()) throw new Error("Schema and table are required.");
  const top = Math.max(1, Math.min(5000, Math.trunc(query.top ?? 100)));
  const columns = query.columns.length > 0 ? query.columns.map(quoteSqlIdentifier).join(", ") : "*";
  const lines = [
    `SELECT TOP (${top}) ${columns}`,
    `FROM ${quoteSqlIdentifier(query.schema)}.${quoteSqlIdentifier(query.table)}`,
  ];
  const filters = (query.filters ?? []).filter((f) => f.column.trim());
  if (filters.length > 0) {
    lines.push("WHERE " + filters.map((filter) => {
      const left = quoteSqlIdentifier(filter.column);
      if (filter.operator === "IS NULL" || filter.operator === "IS NOT NULL") return `${left} ${filter.operator}`;
      return `${left} ${filter.operator} ${sqlLiteral(filter.value)}`;
    }).join("\n  AND "));
  }
  if (query.orderBy?.trim()) {
    lines.push(`ORDER BY ${quoteSqlIdentifier(query.orderBy)} ${query.descending ? "DESC" : "ASC"}`);
  }
  return `${lines.join("\n")};`;
}
