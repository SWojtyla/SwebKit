# SQL Database Explorer — Technical Plan

Phased plan — Phase 1 is the usable core; Phases 2 and 3 are follow-on modules in the same
feature. Each phase should land green (build + tests) before the next starts.

## Phase 1 — Core SQL area

### Module 1.1 — Domain model

New `src/SwebKit.Core/Domain/SqlConfig.cs`:

```csharp
public class SqlConfig
{
    public List<SqlConnectionEntry> Connections { get; set; } = [];
    public string? ActiveConnectionId { get; set; }

    [JsonIgnore]
    public SqlConnectionEntry? ActiveConnection =>
        Connections.FirstOrDefault(c => c.Id == ActiveConnectionId) ?? Connections.FirstOrDefault();

    public void Validate() { /* requires ≥1 entry; each entry requires non-empty Server */ }
}

public class SqlConnectionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Database";
    /// <summary>Server FQDN, e.g. myserver.database.windows.net. No protocol, no port required
    /// (1433 default).</summary>
    public string Server { get; set; } = string.Empty;
    /// <summary>Initial database. Empty = connect to the server and let the user pick
    /// per-session (endpoints accept a ?database= override).</summary>
    public string Database { get; set; } = string.Empty;
    /// <summary>Gate for mutating statements from the query editor. Default false. Mirrors
    /// StorageConfig.AllowMutations.</summary>
    public bool AllowWrites { get; set; }
    public bool Active { get; set; } = true;
}
```

`AppConfig` gains `public SqlConfig? SqlConfig { get; set; }` (nullable like `RedisConfig`,
defaulted — no migration needed, nothing existed before). Auth is Entra-only: no credential fields
exist on the entry by design (see index.md Decisions).

Mirror the shape in `web/src/lib/types.ts` (`SqlConfig`, `SqlConnectionEntry`) next to
`RedisConfig`.

### Module 1.2 — `SwebKit.Sql` project + `ISqlClient`

New project `src/SwebKit.Sql/SwebKit.Sql.csproj` (net10.0, matching siblings; add to
`SwebKit.slnx`). Referenced by `src-sidecar` and `tests`.

Abstractions in `SwebKit.Core/Abstractions/ISqlClient.cs` (interface + models in
`SwebKit.Core/Models/SqlModels.cs`):

```csharp
public interface ISqlClient : IAsyncDisposable
{
    Task<bool> TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct);
    Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct);
    Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, CancellationToken ct);
    Task<SqlQueryResult> GetTableRowsAsync(string schema, string table, string? database,
        string? filter, string? orderBy, int skip, int take, CancellationToken ct);
    // Phase 2:
    Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schema, string table,
        IReadOnlyList<string> keyColumns, int maxDiffRows, CancellationToken ct);
    Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? database, CancellationToken ct);
}

public interface ISqlClientFactory
{
    Task<ISqlClient> CreateAsync(SqlConnectionEntry connection, CancellationToken ct);
}
```

`SqlQueryResult` = `{ columns: [{name, typeName, nullable}], rows: object[][], truncated, elapsedMs }`.
`SqlSchemaModel` = grouped `{ schema, objects: [{ name, kind(table|view), columns: [{name, dataType,
isNullable, isPrimaryKey, maxLength?}] }] }` built from `INFORMATION_SCHEMA`/`sys.*` queries
(`sys.tables`, `sys.views`, `sys.columns`, `sys.indexes`/`sys.index_columns` for PK flags).

Implementation `SqlDatabaseClient` on `Microsoft.Data.SqlClient`:

- Connection string built server-side only: `Server=tcp:{entry.Server},1433;Initial Catalog={db};
  Encrypt=True;TrustServerCertificate=False;Persist Security Info=False;` — never returned to the
  frontend. (Never put `ex.Message` in the test-connection response verbatim where it could embed
  the server path — same caution as `RedisEndpoints`'s comment; reuse `ConnectionTestError`.)
- Entra token via `SqlConnection.AccessTokenCallback` (MDS ≥ 5.2):
  `AzureCredentialFactory.CreateDefault().GetTokenAsync(new TokenRequestContext(["https://database.windows.net/.default"]))`
  → `SqlAuthenticationToken`. Do NOT use `Authentication=Active Directory Default` — it runs its
  own MSAL chain, bypassing the factory's EnvironmentCredential exclusion (AZ-4).
- ADO.NET connection pooling left on (default; pools per connection string). `MaxRows` enforced by
  reading only N+1 rows from the `SqlDataReader` and setting `truncated`.
- Schema introspection queries parameterized on `@database` via `USE`-free three-part naming is not
  possible cross-database; instead open the connection against the requested `database` (or
  `master`/entry default when browsing) — catalog queries are then trivially per-connection.

### Module 1.3 — Write guard (`SqlStatementGuard` in `SwebKit.Sql`)

Classification via `Microsoft.SqlServer.TransactSql.ScriptDom` (`TSql160Parser`):

- `Parse` the batch; on parse errors → treat as not-allowed-when-readonly (fail closed) and return
  the parser errors to the caller for display.
- Walk each `TSqlStatement` in each batch: allow `SelectStatement`, `WithCtesAndXmlNamespaces`
  (but recurse — a CTE batch can still wrap a mutating statement; check the inner statement type),
  `ExecuteStatement` is allowed ONLY when `AllowWrites` (an EXEC can do anything — no way to prove
  read-only), `PrintStatement`, `Set*Statement` (allow-list read-harmless SETs), `UseStatement`,
  `BeginTransaction`/`CommitTransaction`/`RollbackTransaction` only when `AllowWrites`.
- Reject `InsertStatement`, `UpdateStatement`, `DeleteStatement`, `MergeStatement`, all
  `Create/Alter/Drop*Statement`, `TruncateTableStatement`, `BulkInsertStatement`,
  `Grant/Revoke/Deny`, etc. — default-deny: any statement class not on the allow-list is mutating.
- Guard returns `{ allowed, reason, offendingStatement? }` — endpoints surface `reason` as the
  friendly "writes are disabled on this connection" / "statement type X is not supported" error.

This guard is the single security boundary for read-only mode: called by the `/query` endpoint and
by the `query_sql` agent tool (which always applies it regardless of `AllowWrites` — agent reads
stay read-only; writes go through `propose_execute_sql` + user confirmation).

### Module 1.4 — Sidecar endpoints + pooling

`ISqlConnectionPool` in Core; `SidecarSqlConnectionPool` in `src-sidecar/Services/` built on
`ClientCache<ISqlClient>` keyed by `entry.Id` — same shape as `SidecarRedisConnectionPool`
including the demo-bypass inside `GetOrCreateAsync` and `Evict`/`InvalidateAll` for settings edits.

New `src-sidecar/Endpoints/SqlEndpoints.cs` + `app.MapSqlEndpoints()` in `Program.cs` (registered
alongside `MapRedisEndpoints`):

| Endpoint | Purpose |
| --- | --- |
| `GET /api/sql/{connectionId}/test` | Test connection (Entra token + connect); `ConnectionTestError.Describe` |
| `GET /api/sql/{connectionId}/databases` | List databases on the server |
| `GET /api/sql/{connectionId}/schema?database=` | Full schema tree model for the browser + autocomplete cache |
| `POST /api/sql/{connectionId}/query` | `{sql, database?, maxRows?}` → guarded, windowed `SqlQueryResult` (default cap e.g. 500, hard cap e.g. 5000) |
| `GET /api/sql/{connectionId}/tables/{schema}/{table}/rows` | Browse: `?database&filter&orderBy&skip&take` — server-side `WHERE`/`ORDER BY`/`OFFSET FETCH`; identifiers bracket-quoted, filter validated (column names + operators only — no raw SQL concatenation of untrusted identifiers) |
| `GET /api/sql/{connectionId}/history` + `DELETE` | Query history (M1.7) |
| `GET/POST/DELETE /api/sql/{connectionId}/queries` | Saved queries CRUD (M1.7) |
| `GET /api/sql/discover` | ARM discovery stream/list (M1.5) |
| `POST /api/sql/compare/data` | Phase 2 (M2.3) |
| `POST /api/sql/compare/schema` | Phase 2 (M2.4) |
| `POST /api/sql/{connectionId}/completion-context` | Phase 2 (M2.2) |

`ResolveConnection(connectionId, profile, demo)` mirrors `ResolveCache`: demo-mode returns the demo
entry; `"demo-sql"` is a reserved id that must never resolve to a real profile entry.

DI in `Program.cs` next to the Redis pool registrations: `ISqlClientFactory → SqlClientFactory`,
`ISqlConnectionPool → SidecarSqlConnectionPool`, `SqlServerDiscoveryService` (+ demo selector),
`SqlQueryRepository`.

### Module 1.5 — ARM discovery

`SqlServerDiscoveryService` in `SwebKit.Sql`, mirroring `AppInsightsDiscoveryService`: shared
credential → `ArmClient` → `subscription.GetSqlServersAsync()` (package `Azure.ResourceManager.Sql`,
pin stable ≥7 days old) → per-server `GetSqlDatabases()`. Yield `{ serverFqdn, serverName,
resourceGroup, subscriptionId, subscriptionName, databases[] }`; in-memory cache + `InvalidateCache`.
A demo-aware selector (pattern: `ObservabilityResourceDiscoverySelector`) returns canned servers in
demo mode. Frontend settings "Discover" button lists them → one click prefills a
`SqlConnectionEntry` (DisplayName = server name, Server = FQDN).

### Module 1.6 — Frontend

- Route `/sql` in `web/src/App.tsx` (lazy, ErrorBoundary — copy an existing entry); nav item in
  `web/src/components/layout/AppLayout.tsx` `navItems` (e.g. label "SQL", a suitable lucide icon).
- `web/src/components/sql/`:
  - `SqlPage.tsx` — top bar: connection picker (active connection default), database picker
    (populated from `/databases`), test-connection status. Layout: left `SchemaTree`, right editor
    tabs + `ResultsGrid`; bottom panel tabs for History / Saved queries.
  - `SchemaTree.tsx` — server → schemas → tables/views → columns (type + PK icon), refresh button,
    filter box. "Browse rows" + "SELECT top 100" context actions per ux-interaction-consistency
    affordance rules (click affordance, not right-click-only).
  - `QueryEditor.tsx` — CodeMirror with `@codemirror/lang-sql` (add dep), reusing
    `codemirror-theme.ts`; Ctrl+Enter runs; editor honors `allowWrites` (read-only badge + guard
    error banner).
  - `ResultsGrid.tsx` — virtualized/windowed grid (reuse `log-window.ts` model — never ship
    unbounded rows to the DOM); shows `truncated` indicator + elapsed ms; export buttons (M2.5).
  - `TableBrowsePanel.tsx` — table mode of the results area (filter/sort/paging controls →
    `/tables/…/rows`).
  - `SavedQueriesPanel.tsx`, `HistoryPanel.tsx` — list, run, save-as, delete; saved queries grouped
    by folder.
  - `ComparePanel.tsx` — mounted empty in Phase 1, filled by M2.3/M2.4.
- `web/src/lib/api.ts` fetchers — `apiFetch` with TanStack `queryFn` `signal` pass-through per the
  documented cancellation convention.
- `web/src/components/settings/SqlSettings.tsx` + tab in `SettingsPage.tsx`: connections list
  (add/remove/duplicate), fields Server/Database/DisplayName/Active, Entra-note helper text ("uses
  your signed-in Azure identity — `docs/pitfalls/azure-sdk.md`"), Allow-writes checkbox with warning
  copy, Test Connection, Discover (M1.5). `DraftInput` commit-on-blur per
  `settings-save-performance`.
- `docs/architecture/functionalities/sql.md` (new) + rows in `architecture.md` and
  `codebase-guide.md` tables.

### Module 1.7 — Saved queries + history

`SqlQueryRepository` in `SwebKit.Core/Configuration/` — `sql-queries.json` under
`%APPDATA%/SwebKit` with atomic write + `.bak` recovery (same helper as other repositories).
Models in `SwebKit.Core/Domain/SqlQueryModels.cs`: `SavedSqlQuery { id, name, folder?, sql,
connectionId?, createdAt, updatedAt }`, `SqlHistoryEntry { sql, connectionId, database, executedAt,
elapsedMs, rowCount, succeeded, error? }` — history capped (e.g. 200 per profile, FIFO). Written by
the `/query` endpoint on every execution. Repo-linked `.swebkit-sql/` folders are a follow-up (the
`LinkedCollectionRootRepository`/`LinkedCollectionFileService` pattern) — explicitly out of Phase 1.

### Module 1.8 — Agent tools

`FeatureArea.Sql` added to the `FeatureArea` enum (additive — check web `types.ts` and any
area-label mappings need the new member). New folder `src/SwebKit.Agents/Tools/Sql/`:

| Tool | Kind/Risk | Purpose |
| --- | --- | --- |
| `list_sql_connections` | Read | Configured connections (ids, names, server) — lets the agent pick |
| `list_sql_databases` | Read | Databases on a connection |
| `list_sql_tables` | Read | Tables/views (schema-qualified) |
| `describe_sql_table` | Read | Columns, types, PK, nullability |
| `query_sql` | Read | Guarded SELECT only (M1.3 always enforced), hard row cap ~100, returns compact JSON |
| `propose_execute_sql` | Mutate/High | Non-SELECT via `PendingAgentAction` → `IAgentActionCoordinator`; executed by new `SqlActionExecutor : IAgentActionExecutor` on confirm; additionally gated by the connection's `AllowWrites` |

`SqlToolContext` mirrors `RedisToolContext`: demo branch (`new DemoSqlClient()`), requested-id →
active → first-configured fallback, "SQL is not configured. Add a connection in settings." error.
Tools registered as `AddSingleton<IAgentTool, …>` in `Program.cs` alongside the Redis block;
`SqlActionExecutor` registered with the other `IAgentActionExecutor`s. ACP `SwebKitToolsMcpBridge`
exposes them to external agents automatically — verify the `FeatureArea` allowlist mapping covers
the new enum member.

### Module 1.9 — Demo mode

`DemoSqlClient : ISqlClient` in `SwebKit.Core/Services/` (with the other `Demo*` clients): fake
"orders" database — `dbo.customers`, `dbo.orders`, `dbo.products`, `sales.invoices` schemas with
realistic columns; `ExecuteQueryAsync` returns canned result sets for recognizable queries and an
echo/`SELECT`-shape generic result otherwise, honoring `maxRows`/`truncated`. `DemoModeService`:
`DemoSqlConnectionId = "demo-sql"`, `GetDemoSqlConnection(id)` (`orders-dev-sql` /
`payments-dev-sql` style entries aligning with the demo namespaces), `GetSqlClient`. Guided-tour /
demo-scenario data extended if the tour touches SQL (`web/src/lib/demo-scenarios.ts`).

## Phase 2 — Productivity

### Module 2.1 — Schema-aware autocomplete (name-level)

`GET /api/sql/{connectionId}/completion-model?database=` — compact payload: T-SQL keyword list
(static), schema/table/view names, per-table column names (from the same catalog queries as the
schema tree, cached in the pool's client). Frontend `web/src/lib/sql-completion.ts`: a CodeMirror
completion source wired into `QueryEditor`. Name-level only — no context resolution. Unit-testable
pure function over the model.

### Module 2.2 — Context-aware autocomplete (ScriptDom)

`POST /api/sql/{connectionId}/completion-context` `{sql, cursorOffset}` → server-side ScriptDom
parse of the partial text: resolve `FROM`/`JOIN` table references and aliases in scope at the
cursor → ranked suggestion list (alias columns first, then tables, then keywords). Deliberately
separate from M2.1 — it needs the AST walk, and a best-effort fallback to M2.1 behavior on parse
errors (mid-typing text rarely parses cleanly; use `TSql160Parser` tolerant parse + nearest-node
heuristics).

### Module 2.3 — Data compare

`POST /api/sql/compare/data` `{sourceConnectionId, targetConnectionId, schema, table, keyColumns[],
database?, maxDiffRows?}`:

- Both sides through their pooled clients. Fetch keyset pages (`ORDER BY` keys, `OFFSET FETCH`) from
  each, merge-join in memory on the key tuple → `onlyInSource`, `onlyInTarget`, `key` sets; for
  shared keys fetch full rows in key-batches and compare column values → `changed` (with
  `{column, sourceValue, targetValue}` diffs).
- Caps: `maxDiffRows` (default 500), page size, hard timeout via `ct`. Returns counts even when the
  row payload is capped.
- UI in `ComparePanel.tsx`: source/target pickers, table picker, key-column multiselect (PK
  columns preselected from schema), three-section diff result.

### Module 2.4 — Schema compare (report only)

`POST /api/sql/compare/schema` `{sourceConnectionId, targetConnectionId, database}` → catalog
snapshot on both (tables, columns w/ type+nullability, PK/FK constraints, indexes) → diff:
`onlyInSource` / `onlyInTarget` / `differing` objects with per-property diffs. Report view in
`ComparePanel.tsx` (grouped tree). **No sync-script generation** — non-goal per index.md.

### Module 2.5 — Export

Results grid "Export" → CSV/JSON file download reusing `web/src/lib/download.ts`. JSON shape = array
of row objects keyed by column name.

## Phase 3 — Correlation

### Module 3.1 — Topology resource kind

`WorkspaceResourceArea.Sql` (additive enum member — `WorkspaceTopology.cs` doc comment lists the
covered areas; update it). `WorkspaceTopologyEndpoints.GetCandidatesAsync` gains SQL candidates from
`SqlConfig.Connections` — resource key `server` (or `server/database`), label = DisplayName.
`WorkspaceMapSettings.tsx` picker + map rendering learn the new area (icon, label). Web `types.ts`
mirror.

### Module 3.2 — Relationship detection

`WorkspaceRelationshipSuggestionService`: SQL nodes join `otherNodes` automatically once the enum
exists — server FQDNs already appear verbatim in env-var/ConfigMap connection strings. Extend
`ResourceKeyMatchFragment`/`CollectHaystackAsync` so a `server/database` key matches on the server
part, and add pod-log scanning as a haystack source per the `agent-correlation` direction
(reasonable-string extraction — capped). Reason strings name the matched fragment.

### Module 3.3 — `investigate_workspace_issue` + `check_sql_health`

- `"Sql"` added to the tool's `area` enum and node matching.
- New `check_sql_health` agent tool (`FeatureArea.Sql`, Read): connectivity, database state
  (`sys.databases`), blocking-session count + top blockers (`sys.dm_exec_requests` /
  `sys.dm_os_waiting_tasks`), dead/basic server info — shallow on purpose (non-goal: full DBA
  dashboard).
- The workspace walker (`InvestigateWorkspaceIssueTool`) maps `WorkspaceResourceArea.Sql` →
  `check_sql_health` by tool name, same dispatch as the other areas.

### Module 3.4 — Drill-through

Deep-link route params `/sql?connection={id}&database={db}&table={schema.table}`: `SqlPage` consumes
them on mount (select connection, expand tree, open a browse tab). Entry points: workspace-map node
context action "Open in SQL", command-palette `paletteSearch` provider, and a "query this entity"
action where a Service Bus message body clearly carries a table row id is a documented follow-up
(too heuristic for this phase — record it in status.md Follow-ups, don't implement).

## Packages

| Package | Where | Notes |
| --- | --- | --- |
| `Microsoft.Data.SqlClient` | `SwebKit.Sql` | Pin current stable ≥7 days old in `Directory.Packages.props` |
| `Microsoft.SqlServer.TransactSql.ScriptDom` | `SwebKit.Sql` | Write-guard (P1) + autocomplete (P2.2) |
| `Azure.ResourceManager.Sql` | `SwebKit.Sql` | Discovery (P1) |
| `@codemirror/lang-sql` | `web` | Editor SQL mode |

## Docs to update during implementation

- `docs/architecture/functionalities/sql.md` (new) — Credential/auth model, endpoints, tools,
  write-guard boundary.
- `docs/architecture/architecture.md` — `SwebKit.Sql` runtime component + flow-diagram node +
  cross-cutting rows.
- `docs/architecture/codebase-guide.md` — folder map + "where to start" rows.
- `docs/features/README.md` — catalog entry when the feature lands (this folder's own entry is
  added now).
- `docs/pitfalls/` — only if a real pitfall emerges (candidate: ScriptDom parse-error handling for
  mid-typing text; SQL identifier quoting rules for the browse endpoint).

## Known risks / constraints

- **Write-guard is a security boundary.** Default-deny classification, fail closed on parse errors,
  EXEC allowed only under `AllowWrites`. Edge cases to test hard: multi-batch scripts, comments,
  `WITH … DELETE`, `INSERT … OUTPUT`, `SELECT INTO` (mutating!), `MERGE`, synonyms.
- **`SELECT INTO` is a mutating select** — `SelectStatement` with an `Into` clause must be treated
  as a write.
- **Token refresh**: `AccessTokenCallback` over static `AccessToken`, or pooled connections die
  after token expiry (~1h).
- **Private endpoints**: databases reachable only in-network — same constraint as every other area;
  say so in `sql.md`.
- **Cross-database queries**: catalog/browse endpoints always open a connection per target
  database; no `USE` switching mid-session (pool safety).
- **Identifier safety**: table browse builds `WHERE`/`ORDER BY` from validated column names and
  whitelisted operators only; never raw-concatenate untrusted identifiers (schema/table names
  validated against the catalog first).

## Phase 4 — Dogfooding fixes (user-reported, 2026-09-16)

First real use surfaced five issues plus one rework request. Each is independently
shippable; 4.2 is a correctness bug and lands first.

### Module 4.1 — Per-database profiles + settings that scale

**Files:** `SqlSettings.tsx`, `SqlPage.tsx`, `web/src/lib/types.ts` (no schema change —
`SqlConnectionEntry.Database` already exists).

User flow: select a server, see which databases are in it *before* committing, then add
each database as its own entry. The SQL page then shows databases — not servers.

- **Discovery panel**: replace the single per-server "Add" (which today silently takes
  `databases[0]`) with per-database add — each discovered DB gets its own Add that
  creates `{ displayName: db, server, database: db }`. Whole-server add stays possible.
- **Browse-before-add for typed servers**: a "Browse databases" action that lists a
  server's DBs without a saved profile. New `POST /api/sql/databases { server, database? }`
  builds an unpooled throwaway client via `ISqlClientFactory` (ad-hoc enumeration can't
  go through the Id-keyed pool). Same endpoint backs Module 4.2's test fix.
- **Settings layout**: group `ConnectionRow`s under collapsible server headers
  (server FQDN + entry count + "add database" action); a filter box over
  displayName/server/database once the list exceeds ~5 entries.
- **SQL page**: picker groups options by server (`<optgroup>`). Entries with a
  `Database` show as that DB; legacy db-less entries keep today's per-session database
  select — no migration, no broken profiles.

### Module 4.2 — Fix "Test connection"

**Files:** `ConfigEndpoints.cs`, `SqlEndpoints.cs`, `SqlSettings.tsx`, `useSql.ts`.

Root cause verified in code: the profile-save handler calls `InvalidateAll()` on the
storage and service-bus pools and targeted `Evict` on redis — **but never touches
`ISqlConnectionPool`**, which caches clients keyed by `connection.Id` alone. Editing
`Server`/`Database` on an existing entry leaves a pooled `ISqlClient` aimed at the old
server, so Test (and every other SQL endpoint) silently exercises the stale connection.

Two-part fix:

1. `ConfigEndpoints` evicts stale SQL connections on save — mirror
   `StaleRedisCacheIds`/`SameConnection` with a SQL variant comparing the
   connection-affecting fields (`Server`, `Database`; `DisplayName`/`Active`/
   `AllowWrites` don't affect the pooled client).
2. New `POST /api/sql/test { server, database }` — an ad-hoc, unpooled client via
   `ISqlClientFactory` so the settings row tests **the current form values**, not the
   last-saved state. Also removes the `DraftInput` commit race (Test clicked before
   blur/save) and enables test-before-first-save. Keep `GET /api/sql/{id}/test` for
   the saved-config path.

### Module 4.3 — Save from the Query tab

**Files:** `SqlPage.tsx` (+ a small `SaveQueryPopover`); no backend change.

"Unable to save queries on the query tab" — literally true: the only save form lives in
`SavedQueriesPanel`. Add a Save button beside Run → popover (name + optional folder) →
`useSaveSqlQuery` → `notify` + invalidate the saved-queries key. Follows the existing
saved-query contract (`SaveSqlQueryRequest`).

### Module 4.4 — Compare tab clarity

**Files:** `ComparePanel.tsx`, `SqlEndpoints.cs` compare request records.

"From where comes the data?" — compare runs **live reads against the two selected
connections**; the UI never says so, silently defaults the target to the first other
connection, and always uses each connection's default database (the source schema list
is `useSqlSchema(sourceId, null)`).

- Explainer line: "Compares live data between the two connections — nothing is copied
  or stored."
- Explicit database picker per side (from `GET /api/sql/{id}/databases`), threaded
  through `SqlDataCompareRequest`/`SqlSchemaCompareRequest`
  (`SourceDatabase`/`TargetDatabase`); schema/object pickers load for the chosen
  source database.
- Require an explicit target pick — drop the silent `first other connection` default.
- Results header echoes `server / database.schema.table` for both sides.

### Module 4.5 — Query builder + syntax help

**Files:** new `QueryBuilderPanel.tsx` on the Query tab; `SqlCompletionResolver`
snippet suggestions + a static cheat-sheet popover in `SqlEditor`'s toolbar.

"SQL for dummies": a guided builder that writes the SELECT — table picker (from the
loaded schema) → column multi-select (default all) → filter rows (column / whitelisted
operator / value) → ORDER BY + TOP N → "Insert into editor". Output is SELECT-only text
generated by a pure helper (`sqlLiteral` for safe value quoting); the write guard still
applies downstream, and the user edits freely before running. Syntax help = keyword
snippets in the existing completion path plus a "?" popover of common patterns — static
web content, no new backend surface.
