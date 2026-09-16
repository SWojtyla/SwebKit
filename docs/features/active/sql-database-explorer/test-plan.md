# SQL Database Explorer — Test Plan

Test scope is per-phase; each module's tests land with that module. New test project
`tests/SwebKit.Sql.Tests` mirrors the `SwebKit.<Area>.Tests` convention.

## Phase 1 — Core

### Unit — `tests/SwebKit.Core.Tests` / `tests/SwebKit.Sql.Tests`

| Scenario                                                                               | Expected                                                                           |
| -------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| `SqlConfig.Validate()` — zero connections                                              | Throws, message names `Connections`                                                |
| `SqlConfig.Validate()` — entry with empty/whitespace `Server`                          | Throws naming `Server`                                                             |
| `SqlConfig.ActiveConnection` — explicit id, missing id, empty list                     | Active → fallback first → null                                                     |
| Connection-string builder                                                              | Contains `Encrypt=True`, `Persist Security Info=False`; never contains credentials |
| `SqlStatementGuard` — plain `SELECT`, `WITH cte AS (SELECT …)`                         | Allowed regardless of `AllowWrites`                                                |
| `SqlStatementGuard` — `SELECT … INTO #t`                                               | Rejected when `AllowWrites=false` (mutating select)                                |
| `SqlStatementGuard` — `INSERT/UPDATE/DELETE/MERGE/TRUNCATE`                            | Rejected when false; allowed when true                                             |
| `SqlStatementGuard` — `WITH cte AS (SELECT …) DELETE …`                                | Rejected when false (mutating statement after CTE)                                 |
| `SqlStatementGuard` — multi-batch `SELECT …; GO; DROP TABLE …`                         | Rejected when false — whole batch classified                                       |
| `SqlStatementGuard` — `EXEC proc`                                                      | Rejected when false; allowed when true                                             |
| `SqlStatementGuard` — comments-only / `-- comment\nSELECT 1`                           | Allowed; comments don't defeat classification                                      |
| `SqlStatementGuard` — parse failure (malformed SQL)                                    | Fail closed: not allowed when `AllowWrites=false`; parser errors returned          |
| `SqlStatementGuard` — `GRANT/ALTER/CREATE` etc. (default-deny)                         | Rejected when false                                                                |
| `SqlToolContext.ResolveAsync` — requested id / active / first / none-configured / demo | Same fallback table as `RedisToolContext` tests                                    |
| `DemoSqlClient` — schema model, `ExecuteQueryAsync` honoring `maxRows`/`truncated`     | Deterministic canned results                                                       |
| `SqlQueryRepository` — round-trip, atomic write, `.bak` recovery, history cap FIFO     | Matches repository conventions                                                     |

### Unit — `tests/SwebKit.Sidecar.Tests`

| Scenario                                                                           | Expected                                             |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------- |
| `SidecarSqlConnectionPool` — same id → same cached client; `Evict`/`InvalidateAll` | Mirrors `SidecarServiceBusConnectionPoolTests` shape |
| Pool demo bypass — `DemoModeService.IsDemoMode` → demo client, cache untouched     | Mirrors Redis pool demo test                         |
| `ResolveConnection` — reserved `demo-sql` id outside demo mode                     | Null → 404                                           |
| `/api/sql/{id}/query` — write statement on `AllowWrites=false` entry               | 400/typed error, no execution                        |
| `/tables/{s}/{t}/rows` — identifier validation (unknown schema/table/column)       | 400, no raw concatenation                            |
| `query` endpoint — `maxRows` default + hard cap                                    | Enforced                                             |

### Unit — `tests/SwebKit.Agents.Tests`

| Scenario                                                                                                                 | Expected                        |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------------- |
| Each `sql_*` tool — schema parses, `FeatureArea.Sql`, `Kind`/`Risk` correct                                              | Matches tool conventions        |
| `query_sql` — rejects non-SELECT even when connection allows writes                                                      | Always guarded                  |
| `propose_execute_sql` — returns `pending_confirmation`, registers `PendingAgentAction`; blocked when `AllowWrites=false` | Matches propose-tool convention |
| Tools unconfigured → `"SQL is not configured. Add a connection in settings."`                                            | Standard error string           |

### Web (vitest) — `web/src/lib` / `web/src/components/sql`

| Scenario                                                             | Expected                     |
| -------------------------------------------------------------------- | ---------------------------- |
| `sql-completion.ts` — suggests keywords + object names from model    | Pure-function tests          |
| Query-result shaping / truncation indicator logic                    | Unit tests                   |
| `SqlSettings.tsx` — save-on-blur (`DraftInput`), allow-writes toggle | Mirrors settings conventions |

### E2E — `tests/SwebKit.E2E.Tests` (Playwright, demo mode)

`/sql` loads in demo mode → demo connection listed → schema tree expands → canned query runs →
results grid renders → write attempt on demo (AllowWrites=false) shows guard error. Follow the
existing demo-mode spec conventions.

## Phase 2 — Productivity

| Area           | Scenarios                                                                                                                    |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Autocomplete   | Context resolution at cursor (alias → columns); fallback to name-level on unparseable text                                   |
| Data compare   | Keyset merge: only-in-source, only-in-target, changed-with-column-diffs; caps honored; NULL vs value handled; composite keys |
| Schema compare | only-in-A / only-in-B / differing objects; column type/nullability diffs; index diffs; identical schemas → empty diff        |
| Export         | CSV/JSON payload correctness from grid model                                                                                 |

## Phase 3 — Correlation

| Area                          | Scenarios                                                                                                    |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Topology                      | `Sql` candidates from config; `server/database` key fragment matching; enum round-trip in profile JSON       |
| Suggestions                   | env var / ConfigMap value containing server FQDN → suggestion with correct Reason; confirmed pair suppressed |
| `investigate_workspace_issue` | `area:"Sql"` resolves node; walker invokes `check_sql_health`; unreachable DB → error section, not a crash   |
| Drill-through                 | `/sql?connection=&table=` deep link selects connection + opens browse tab                                    |

## Phase 4 — Dogfooding fixes

| Area                      | Scenarios                                                                                                                                                                                                     |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pool eviction on save     | `StaleSqlConnectionIds`/`SameSqlConnection`: Server/Database change → evicted; DisplayName/Active/AllowWrites change → kept warm; removed entry → evicted. Regression: edit server → Test hits the new server |
| `POST /api/sql/test`      | Ad-hoc `{server,database}` tests an unsaved entry; never reads the pool; bad server → `connected:false` + described error                                                                                     |
| `POST /api/sql/databases` | Ad-hoc enumeration for a typed server; error shape matches test endpoint                                                                                                                                      |
| Settings grouping/filter  | Connections render under collapsible server groups; filter box matches displayName/server/database; per-DB add in discovery creates one entry per database                                                    |
| Save from Query tab       | Save button → popover → saved query appears in Saved & History for the connection                                                                                                                             |
| Compare                   | Explicit target required (button disabled without it); per-side database pickers feed `SourceDatabase`/`TargetDatabase`; results header shows `server / database.schema.table` both sides                     |
| Query builder             | Generated SELECT is read-only-shaped (passes the write guard); `sqlLiteral` quoting (strings/NULL/numbers/dates/injection-ish input); filters/order/top-N compose correctly                                   |

Manual (live): edit a saved connection's server → Test immediately reflects the new
server (the reported bug); discover a multi-DB server → add two DBs → SQL page shows
both as databases; run a builder-generated query end-to-end.

## Results

_(recorded per phase as modules land)_

## Manual verification — owner: Sebastien

1. Settings → SQL: Discover lists real servers via Entra; add a connection; Test Connection
   succeeds against a live Azure SQL database.
2. `/sql` page: schema tree, run a SELECT, write statement blocked on `AllowWrites=false`; enable
   the toggle and confirm a guarded write path.
3. Agent: "what tables are in <db>" and "query top 10 …" work; a write request produces a
   confirmation card.
4. Long-lived session (>1h) still queries — validates `AccessTokenCallback` refresh.
5. Workspace map: SQL node appears, env-var relationship suggestion fires,
   `investigate_workspace_issue` includes the DB.
6. Aikido scan per `docs/security/aikido-mcp-scan.md` on all new code.
