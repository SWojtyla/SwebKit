/** Mirrors `SwebKit.Core.Domain.SqlConfig`. Entra-only by design — no credential
 * fields; the sidecar acquires tokens through the shared credential factory. */
export interface SqlConfig {
    connections: SqlConnectionEntry[];
    activeConnectionId: string | null;
}

/** Mirrors `SwebKit.Core.Domain.SqlConnectionEntry`. `allowWrites` gates mutating
 * statements everywhere (editor and agent) — default false = read-only. */
export interface SqlConnectionEntry {
    id: string;
    displayName: string;
    server: string;
    database: string;
    allowWrites: boolean;
    active: boolean;
}

export interface SqlDatabaseInfo {
    name: string;
    state: string;
}

export interface SqlColumnInfo {
    name: string;
    dataType: string;
    isNullable: boolean;
    isPrimaryKey: boolean;
}

export interface SqlIndexInfo {
    name: string;
    isUnique: boolean;
    isPrimaryKey: boolean;
    columns: string[];
}

export interface SqlForeignKeyInfo {
    name: string;
    referencedObject: string;
}

export interface SqlObjectInfo {
    name: string;
    kind: string;
    columns: SqlColumnInfo[];
    indexes: SqlIndexInfo[];
    foreignKeys: SqlForeignKeyInfo[];
}

export interface SqlSchemaGroup {
    name: string;
    objects: SqlObjectInfo[];
}

export interface SqlSchemaModel {
    database: string | null;
    schemas: SqlSchemaGroup[];
}

export interface SqlResultColumn {
    name: string;
    typeName: string;
}

export interface SqlQueryResult {
    columns: SqlResultColumn[];
    rows: Record<string, unknown>[];
    truncated: boolean;
    elapsedMs: number;
    rowsAffected: number;
}

export interface SavedSqlQuery {
    id: string;
    name: string;
    folder: string | null;
    sql: string;
    connectionId: string | null;
    createdAt: string;
    updatedAt: string;
}

export interface SqlHistoryEntry {
    id: string;
    sql: string;
    connectionId: string | null;
    database: string | null;
    executedAt: string;
    elapsedMs: number;
    rowCount: number;
    succeeded: boolean;
    error: string | null;
}

export interface SqlDiscoveredServer {
    serverFqdn: string;
    name: string;
    resourceGroup: string;
    subscriptionId: string;
    subscriptionName: string;
    location: string;
    databases: string[];
}

/** What `POST /api/sql/{id}/completion-context` returns — the ScriptDom parse of
 * the editor text around the cursor. `kind` is "any" | "table" | "column". */
export interface SqlCompletionContext {
    tables: { schema: string | null; name: string; alias: string | null }[];
    columnScope: string | null;
    kind: string;
}

export interface SqlColumnDiff {
    column: string;
    sourceValue: unknown;
    targetValue: unknown;
}

export interface SqlChangedRow {
    key: Record<string, unknown>;
    diffs: SqlColumnDiff[];
}

export interface SqlDataCompareResult {
    onlyInSource: Record<string, unknown>[];
    onlyInTarget: Record<string, unknown>[];
    changed: SqlChangedRow[];
    totalOnlyInSource: number;
    totalOnlyInTarget: number;
    totalChanged: number;
    truncated: boolean;
    schemaWarnings: string[];
}

export interface SqlPropertyDiff {
    property: string;
    sourceValue: string | null;
    targetValue: string | null;
}

export interface SqlObjectDiff {
    schema: string;
    name: string;
    kind: string;
    diffs: SqlPropertyDiff[];
}

export interface SqlSchemaCompareResult {
    onlyInSource: SqlObjectDiff[];
    onlyInTarget: SqlObjectDiff[];
    differing: SqlObjectDiff[];
}
