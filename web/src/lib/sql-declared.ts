import type { SqlColumnInfo, SqlSchemaModel } from "./types";

// Declared-object helpers — the client-side mirror of
// `SwebKit.Core.Domain.SqlDeclaredObject`. The grammar is deliberately narrower than
// T-SQL's (bare two-part identifiers, no bracket quoting) so the canonical persisted
// form is unambiguous and every name is safe to bracket-quote server-side.

const PROCEDURE_PREFIX = "exec:";
const MAX_IDENTIFIER_LENGTH = 128;
// T-SQL regular identifiers: letter/underscore start, then letters, digits, _ @ $ #.
// \p{L}/\p{N} match char.IsLetter/IsLetterOrDigit on the C# side.
const IDENTIFIER = /^[\p{L}_][\p{L}\p{N}_@$#]*$/u;

export interface ParsedDeclaredObject {
    schema: string;
    name: string;
    /** True for `exec:` entries — procedures are listed as runnable but never
     * introspected with SELECT TOP 0. */
    isProcedure: boolean;
}

function isValidIdentifier(part: string): boolean {
    return part.length > 0 && part.length <= MAX_IDENTIFIER_LENGTH && IDENTIFIER.test(part);
}

/** Canonical persisted form: `schema.name` or `exec:schema.name`. */
export function canonicalDeclaredEntry(parsed: ParsedDeclaredObject): string {
    return parsed.isProcedure
        ? `${PROCEDURE_PREFIX}${parsed.schema}.${parsed.name}`
        : `${parsed.schema}.${parsed.name}`;
}

/** Parses `schema.name` / `exec:schema.name`; null for anything else. */
export function parseDeclaredEntry(raw: string): ParsedDeclaredObject | null {
    let text = raw.trim();
    if (!text) return null;

    let isProcedure = false;
    if (text.toLowerCase().startsWith(PROCEDURE_PREFIX)) {
        isProcedure = true;
        text = text.slice(PROCEDURE_PREFIX.length).trim();
    }

    const dot = text.indexOf(".");
    if (dot <= 0 || dot !== text.lastIndexOf(".") || dot === text.length - 1) return null;

    const schema = text.slice(0, dot);
    const name = text.slice(dot + 1);
    if (!isValidIdentifier(schema) || !isValidIdentifier(name)) return null;
    return { schema, name, isProcedure };
}

/**
 * Splits textarea content into canonical declared entries (one per line, trimmed —
 * pasting a list with stray whitespace is the normal way this fills in). Invalid lines
 * come back in `invalid` so the UI can flag them rather than silently dropping text the
 * user typed.
 */
export function parseDeclaredObjectsText(text: string): {
    entries: string[];
    invalid: string[];
} {
    const entries: string[] = [];
    const invalid: string[] = [];
    const seen = new Set<string>();
    for (const line of text.split("\n")) {
        const parsed = parseDeclaredEntry(line);
        if (parsed === null) {
            if (line.trim()) invalid.push(line.trim());
            continue;
        }
        const canonical = canonicalDeclaredEntry(parsed);
        if (!seen.has(canonical.toLowerCase())) {
            seen.add(canonical.toLowerCase());
            entries.push(canonical);
        }
    }
    return { entries, invalid };
}

/** Injection-safe bracket quoting for a single identifier part — the same
 * `]` → `]]` rule SqlDatabaseClient.Quote applies server-side. */
export function quoteSqlIdent(part: string): string {
    return `[${part.replaceAll("]", "]]")}]`;
}

/**
 * Merges lazily-fetched columns into a cached schema model so the editor's autocomplete
 * and the query builder see a declared object's columns after its first expand — they
 * only ever read the schema query's cached model, not the introspection query.
 * Returns `prev` unchanged when the target object isn't in the model.
 */
export function mergeObjectColumns(
    prev: SqlSchemaModel | undefined,
    schemaName: string,
    objectName: string,
    columns: SqlColumnInfo[],
): SqlSchemaModel | undefined {
    const group = prev?.schemas.find(
        (s) => s.name.toLowerCase() === schemaName.toLowerCase(),
    );
    const obj = group?.objects.find(
        (o) => o.name.toLowerCase() === objectName.toLowerCase(),
    );
    if (!prev || !group || !obj) return prev;
    return {
        ...prev,
        schemas: prev.schemas.map((s) =>
            s === group
                ? {
                      ...s,
                      objects: s.objects.map((o) =>
                          o === obj ? { ...o, columns } : o,
                      ),
                  }
                : s,
        ),
    };
}
