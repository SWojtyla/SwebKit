---
status: Planned
---

# SQL Database Explorer — Status

- **Current phase:** Planned — design commit only (feature docs; no code).
- **Requested by:** Sebastien — Jam SQL Studio–style inspection inside SwebKit, scoped to SQL
  Server/Azure SQL + Entra, so the agent and workspace correlation can reach the database.
- **Implementation PR:** not raised.

## Definition of Done

### Phase 1 — Core SQL area

- [ ] `SqlConfig`/`SqlConnectionEntry` domain model + `web` types mirror; `AppConfig.SqlConfig`.
- [ ] `SwebKit.Sql` project: `ISqlClient`/`SqlDatabaseClient` on `Microsoft.Data.SqlClient`, Entra
      via `AccessTokenCallback` + shared `AzureCredentialFactory` (scope
      `https://database.windows.net/.default`).
- [ ] `SqlStatementGuard` (ScriptDom, default-deny) gating `/query` by `AllowWrites` and agent
      reads unconditionally.
- [ ] `SidecarSqlConnectionPool` on `ClientCache`; `SqlEndpoints` (test/databases/schema/query/
      table-rows/history/saved-queries) mapped.
- [ ] ARM `SqlServerDiscoveryService` + Discover button (Entra, per `AppInsightsDiscoveryService`).
- [ ] `/sql` React page (connection/db picker, schema tree, CodeMirror SQL editor, windowed
      results grid, table browse) + `SqlSettings` tab (save-on-blur, test, allow-writes toggle).
- [ ] Saved queries + capped history persisted (`sql-queries.json`, atomic + `.bak`).
- [ ] `FeatureArea.Sql` + six agent tools (5 reads + `propose_execute_sql` →
      `SqlActionExecutor`); registered in `Program.cs`; ACP bridge picks them up.
- [ ] `DemoSqlClient` + demo connection(s) wired into `DemoModeService`.
- [ ] Tests per test-plan.md Phase 1; `docs/architecture/functionalities/sql.md` written;
      architecture/codebase-guide updated.

### Phase 2 — Productivity

- [ ] Schema-aware autocomplete (name-level) in the editor.
- [ ] Context-aware autocomplete via ScriptDom (`completion-context` endpoint).
- [ ] Data compare endpoint + `ComparePanel` diff view.
- [ ] Schema compare endpoint + report view (no sync scripts — non-goal).
- [ ] Results export CSV/JSON.

### Phase 3 — Correlation

- [ ] `WorkspaceResourceArea.Sql` + topology candidates + map/settings UI support.
- [ ] Relationship suggestions match SQL server FQDNs in env vars/ConfigMaps (+ pod logs).
- [ ] `check_sql_health` tool + `investigate_workspace_issue` `area:"Sql"` integration.
- [ ] `/sql?connection=&table=` deep links from workspace map + command palette.

### All phases

- [ ] Manual verification (live Entra connect/discover/query, agent flow, >1h token refresh) —
      **owner: Sebastien**.
- [ ] Aikido security scan on new code.

## Follow-ups

- Repo-linked `.swebkit-sql/` saved-query folders (`LinkedCollectionRootRepository` pattern).
- "Query this entity" drill-through from Service Bus message bodies (heuristic — needs a reliable
  id→table mapping story first).
- Query-plan viewer, sync-script generation, notebooks, DBA dashboards: non-goals unless rescoped.
