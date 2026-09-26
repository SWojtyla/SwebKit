# SQL Database Explorer

## What Is Supported

- Configure multiple SQL connection entries per environment profile (server FQDN + database),
  Entra-only — no password or credential fields are stored anywhere.
- ARM-based discovery of Azure SQL servers and databases using the signed-in Entra identity,
  mirroring `AppInsightsDiscoveryService` (`/api/sql/discover`).
- Schema browser tree (database → schema → table/view → columns with types and PK indicators).
- Query editor (CodeMirror, T-SQL mode) with windowed result grid and row-cap truncation.
- Table data browse without writing SQL: server-side filter (`LIKE` on a chosen column),
  order-by, and OFFSET/FETCH paging.
- Saved queries and capped query history (200 entries) persisted to `sql-queries.json`.
- Schema-aware autocomplete (cached object names) plus alias-aware completion via ScriptDom
  (`/api/sql/{id}/completion-context`).
- Data compare (two tables, chosen key columns → only-in-A / only-in-B / changed rows) and
  schema compare (report only — no sync scripts, which are a deliberate non-goal).
- CSV/JSON export of result grids.
- Read-only by default; writes require the per-connection `AllowWrites` flag and go through the
  ScriptDom write-guard a second time.
- Agent tools (`list_sql_connections`, `list_sql_databases`, `list_sql_tables`,
  `describe_sql_table`, `query_sql`, `check_sql_health`, `propose_execute_sql`) with a 50-row
  hard cap and propose→confirm flow for mutations.
- Workspace integration: `WorkspaceResourceArea.Sql` topology nodes, relationship suggestions
  matching SQL server FQDNs in pod env vars/ConfigMaps/logs, `investigate_workspace_issue`
  health dispatch, `/sql?connection=&table=` deep links.
- Hidden-metadata awareness: the schema endpoint also queries `sys.fn_my_permissions` — when
  the catalog reads empty but the identity holds SELECT/EXECUTE without VIEW DEFINITION,
  `MetadataHidden` is set and the UI shows a "schema hidden by permissions" state (with the
  grant to request) instead of the misleading empty-database state.
- Demo mode: three demo connections — `demo-sql`/`demo-sql-2` with intentional schema/data
  drift for compare demonstrations, plus `demo-sql-prd` (restricted: SELECT/EXECUTE, no
  VIEW DEFINITION) to exercise the hidden-schema state.

## Credential / Auth Model

Entra-only. `SqlDatabaseClient` uses `Microsoft.Data.SqlClient` with `AccessTokenCallback`
(not a static `AccessToken`) so pooled connections survive ~1h token expiry. Tokens are
acquired for scope `https://database.windows.net/.default` via the shared
`SwebKit.Core.Services.AzureCredentialFactory` — the same credential chain Service Bus,
Storage, Redis, and AKS use. No SQL auth, no connection strings with secrets, no stored
credentials.

Databases reachable only through private endpoints work only when the tool runs in-network —
same constraint as every other area.

## Write-Guard Boundary

`SqlStatementGuard` (`SwebKit.Sql`, Microsoft ScriptDom `TSql160Parser`) is the security
boundary and is default-deny:

- `SELECT`-family statements only in read-only mode; `SELECT … INTO` is classified as a write.
- Mutating statements (DML, DDL, `EXEC`, transactions, `SET`, `MERGE`, batches, unknown/future
  statement types) are rejected unless `AllowWrites` is enabled on the connection.
- Parse errors fail closed.
- Nested blocks (`IF`, `WHILE`, `BEGIN/END`, `TRY/CATCH`) are walked recursively.

The same guard runs in three places: the `/query` endpoint, `query_sql` (always read-only),
and `SqlActionExecutor` (re-validates `AllowWrites` + the statement at apply time after the
user confirms a `propose_execute_sql` action).

## Core Runtime Flow

1. `SqlPage.tsx` loads connections from the profile (demo overlay adds `demo-sql*` entries;
   they are stripped again before persistence).
2. `SidecarSqlConnectionPool` caches `ISqlClient` instances per connection ID (demo clients
   are never cached); `SqlClientFactory` builds `SqlDatabaseClient` for real entries.
3. Schema/database/table calls go through `src-sidecar/Endpoints/SqlEndpoints.cs` → pooled
   client → `sys.*`/`INFORMATION_SCHEMA` catalog queries, one connection per target database
   (no `USE` switching mid-session — pool safety).
4. Table browse builds `WHERE`/`ORDER BY` from bracket-quoted (`]]`-escaped) identifiers only;
   all values go through parameters. Identifiers can never be parameterized in T-SQL.
5. `SqlQueryRepository` persists saved queries + history with the standard atomic-write +
   `.bak` recovery pattern.
6. Agent tools resolve connections through `SqlToolContext` (profile + demo overlay) and return
   structured JSON (columns, rows, truncation, elapsed, error).

## Main Code Locations

- `src/SwebKit.Sql/SqlDatabaseClient.cs` — `Microsoft.Data.SqlClient` implementation of `ISqlClient`.
- `src/SwebKit.Sql/SqlStatementGuard.cs` — ScriptDom default-deny read/write classifier.
- `src/SwebKit.Sql/SqlServerDiscoveryService.cs` — ARM enumeration (`Azure.ResourceManager.Sql`).
- `src/SwebKit.Sql/SqlCompletionResolver.cs` — alias-aware completion context resolution.
- `src/SwebKit.Core/Abstractions/ISqlClient.cs` — contract (test/databases/schema/query/rows/
  compare/health).
- `src/SwebKit.Core/Domain/SqlConfig.cs` — `SqlConfig`/`SqlConnectionEntry` (no credential fields).
- `src/SwebKit.Core/Domain/SqlQueryModels.cs` — saved query + history models.
- `src/SwebKit.Core/Configuration/SqlQueryRepository.cs` — `sql-queries.json` persistence.
- `src/SwebKit.Core/Services/DemoSqlClient.cs`, `DemoSqlResourceDiscovery.cs`,
  `SqlDataComparer.cs`, `SqlSchemaComparer.cs` — demo client + pure compare helpers.
- `src-sidecar/Endpoints/SqlEndpoints.cs` — `/api/sql/*` surface.
- `src-sidecar/Services/SidecarSqlConnectionPool.cs`, `SqlResourceDiscoverySelector.cs`.
- `src/SwebKit.Agents/Tools/Sql/` — seven tools + `SqlToolContext` + `SqlActionExecutor`.
- `web/src/components/sql/` — `SqlPage`, `SqlEditor`, `SchemaTree`, `ResultsGrid`,
  `BrowsePanel`, `SavedQueriesPanel`, `ComparePanel`.
- `web/src/lib/hooks/useSql.ts`, `web/src/lib/sql-csv.ts`,
  `web/src/components/settings/SqlSettings.tsx`.
