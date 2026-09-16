---
status: In Progress
---

# SQL Database Explorer — Status

- **Current phase:** In Progress — Phases 1–3 implemented and green; Phase 4
  (dogfooding fixes, user-reported 2026-09-16) planned but not implemented.
  Back from Review because new unimplemented scope was added.
- **Requested by:** Sebastien — Jam SQL Studio–style inspection inside SwebKit, scoped to SQL
  Server/Azure SQL + Entra, so the agent and workspace correlation can reach the database.
- **Implementation PR:** not raised — commit/push pending explicit approval.

## Definition of Done

### Phase 1 — Core SQL area

- [x] `SqlConfig`/`SqlConnectionEntry` domain model + `web` types mirror; `AppConfig.SqlConfig`.
- [x] `SwebKit.Sql` project: `ISqlClient`/`SqlDatabaseClient` on `Microsoft.Data.SqlClient`, Entra
      via `AccessTokenCallback` + shared `AzureCredentialFactory` (scope
      `https://database.windows.net/.default`).
- [x] `SqlStatementGuard` (ScriptDom, default-deny) gating `/query` by `AllowWrites` and agent
      reads unconditionally.
- [x] `SidecarSqlConnectionPool` on `ClientCache`; `SqlEndpoints` (test/databases/schema/query/
      table-rows/history/saved-queries) mapped.
- [x] ARM `SqlServerDiscoveryService` + Discover button (Entra, per `AppInsightsDiscoveryService`).
- [x] `/sql` React page (connection/db picker, schema tree, CodeMirror SQL editor, windowed
      results grid, table browse) + `SqlSettings` tab (save-on-blur, test, allow-writes toggle).
- [x] Saved queries + capped history persisted (`sql-queries.json`, atomic + `.bak`).
- [x] `FeatureArea.Sql` + six agent tools (5 reads + `propose_execute_sql` →
      `SqlActionExecutor`); registered in `Program.cs`; ACP bridge picks them up.
- [x] `DemoSqlClient` + demo connection(s) wired into `DemoModeService`.
- [x] Tests per test-plan.md Phase 1; `docs/architecture/functionalities/sql.md` written;
      architecture/codebase-guide updated.

### Phase 2 — Productivity

- [x] Schema-aware autocomplete (name-level) in the editor.
- [x] Context-aware autocomplete via ScriptDom (`completion-context` endpoint).
- [x] Data compare endpoint + `ComparePanel` diff view.
- [x] Schema compare endpoint + report view (no sync scripts — non-goal).
- [x] Results export CSV/JSON.

### Phase 3 — Correlation

- [x] `WorkspaceResourceArea.Sql` + topology candidates + map/settings UI support.
- [x] Relationship suggestions match SQL server FQDNs in env vars/ConfigMaps (+ pod logs).
- [x] `check_sql_health` tool + `investigate_workspace_issue` `area:"Sql"` integration.
- [x] `/sql?connection=&table=` deep links from workspace map + command palette.

### Phase 4 — Dogfooding fixes (added 2026-09-16)

- [ ] Per-database profiles: discover → per-DB add; "browse databases" on a typed
      server (`POST /api/sql/databases`); SQL page picker groups by server
      (`<optgroup>`), shows DBs.
- [ ] Settings layout scales: connections grouped under collapsible server headers +
      filter box for long lists.
- [ ] Test connection fixed — root cause found: `ConfigEndpoints` save invalidates
      storage/SB/redis pools but never `ISqlConnectionPool` (Id-keyed) → stale client
      after edits. Fix = SQL-aware eviction on save + `POST /api/sql/test` ad-hoc
      endpoint that tests the current form values.
- [ ] Save affordance on the Query tab (was only under Saved & History).
- [ ] Compare tab clarity: explainer line, per-side database pickers, explicit
      target required, results echo `server / database.schema.table`.
- [ ] Query builder (`QueryBuilderPanel`, SELECT-only generation) + completion
      snippets + cheat-sheet popover.

### All phases

- [ ] Manual verification (live Entra connect/discover/query, agent flow, >1h token refresh) —
      **owner: Sebastien**.
- [x] Aikido security scan on new code — ran `aikido_scan_paths` over all 81 changed
      first-party files; 9 findings, all assessed false positives (ScriptDom-safe
      `]]`-escaped identifier quoting — values are parameterized; sidecar spawn on a fixed
      resource path; OS-enumerated temp-file cleanup; configured-kubeconfig read).

## Verification results

- `tests/SwebKit.Sql.Tests` — 46 passed (guard + completion resolver).
- `tests/SwebKit.Core.Tests` — full suite green incl. 41 new SQL tests.
- `tests/SwebKit.Sidecar.Tests` — 25 SQL endpoint/pool tests green.
- `tests/SwebKit.Agents.Tests` — SQL tools + workspace-dispatch tests green.
- `web` vitest — 431 passed / 40 files (incl. `sql-csv` coverage).
- `web` lint — clean; production build — clean (`SqlPage` code-split chunk).
- `web/e2e/sql.spec.ts` (Playwright, demo mode) — 7/7 passed.
- Full solution build — all projects compile; Windows MSIX packaging fails with
  `SigningCertificateThumbprintNotInStore` (missing signing cert — environment issue,
  unrelated to this change; the app DLL itself builds).

## Follow-ups

- Repo-linked `.swebkit-sql/` saved-query folders (`LinkedCollectionRootRepository` pattern).
- "Query this entity" drill-through from Service Bus message bodies (heuristic — needs a reliable
  id→table mapping story first).
- Query-plan viewer, sync-script generation, notebooks, DBA dashboards: non-goals unless rescoped.
