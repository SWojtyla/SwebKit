---
status: Planned
---

# SQL Database Explorer

## Scope

A simplified Jam SQL Studio–style database inspection area inside SwebKit, scoped to SQL Server
and Azure SQL with Microsoft Entra authentication only. The goal is not SQL-IDE parity — the
differentiated value is making SQL a first-class area next to AKS, Service Bus, Redis and Storage
so the agent and the workspace-correlation machinery (`workspace-intelligence`) can see the
database too. "Did this queue back up because a deployment changed a table?" is the class of
question only this tool can answer.

The feature is delivered in three phases, each independently shippable:

- **Phase 1 — Core SQL area.** Connection profiles (Entra-only), ARM discovery of servers and
  databases, schema browser tree, query editor with windowed results grid, table data browser,
  saved queries and history, per-connection write toggle (read-only by default, writes classified
  via ScriptDom and gated), six agent tools (reads plus a propose→confirm write), demo-mode
  provider.
- **Phase 2 — Productivity.** Schema-aware autocomplete (keywords + cached object names),
  context-aware autocomplete via `Microsoft.SqlServer.TransactSql.ScriptDom` (alias resolution),
  data compare (row-level diff by key columns), schema compare (report only — no sync scripts),
  results export to CSV/JSON.
- **Phase 3 — Correlation.** SQL as a `WorkspaceResourceArea` kind, topology candidates from
  configured connections, connection-string relationship detection against AKS env
  vars/ConfigMaps/pod logs, a `check_sql_health` tool wired into `investigate_workspace_issue`,
  and drill-through deep links (`/sql?connection=…&table=…`) from the workspace map and command
  palette.
- **Phase 4 — Dogfooding fixes.** Added 2026-09-16 after the first real use surfaced friction:
  per-database connection profiles (pick a server, see its databases, add each DB as an entry —
  the SQL page then shows databases, not servers), a settings layout that scales to many servers,
  a broken "Test connection" (profile save never evicts the pooled client — it keeps testing the
  pre-edit server), a Save affordance on the Query tab (today it only exists under
  Saved & History), a clearer Compare tab (explicit databases, labelled live source/target, no
  silent target default), and a guided query builder plus richer syntax help.

## Decisions

- **Entra-only auth.** Connections authenticate through the shared
  `AzureCredentialFactory.CreateDefault()` credential — no SQL auth, no connection-string mode.
  Matches what the job requires and keeps the AZ-4 pitfall (`docs/pitfalls/azure-sdk.md`)
  guarantees: `SqlConnection.AccessTokenCallback` is wired to a
  `TokenRequestContext(["https://database.windows.net/.default"])` token from the shared factory,
  rather than `Authentication=Active Directory Default`, which would run MSAL's own chain and
  reintroduce the EnvironmentCredential problem. `AccessTokenCallback` (not a static
  `AccessToken`) so pooled connections get fresh tokens on expiry.
- **Per-connection write toggle.** `AllowWrites` (default false) mirrors
  `StorageConfig.AllowMutations`. When false, the ScriptDom-based statement classifier rejects any
  batch containing a mutating statement — enforced server-side in `SwebKit.Sql` so both the
  endpoints and the agent tools share one boundary. Agent write requests are additionally
  propose→confirm (`propose_execute_sql`, Mutate/High) regardless of the toggle.
- **Compare = report, not sync.** Data compare returns only-in-A / only-in-B / changed row sets;
  schema compare returns object/column/index/constraint diffs. No generated migration scripts —
  that is SSDT's problem, not ours.
- **Web/Tauri only.** No MAUI/Blazor surface, per the primary-stack notice in
  `docs/architecture/architecture.md` and the convention set by `redis-entra-auth`.

## Non-goals

- No execution-plan visualization (showplan graph rendering is a rabbit hole).
- No schema-compare sync-script generation.
- No SQL notebooks, DBA dashboards (sessions/waits/perf counters), backup/restore, cloning.
- No non–SQL Server engines (PostgreSQL/MySQL/Oracle/SQLite) — the abstractions stay
  `ISqlClient`-shaped but nothing else is implemented.
- No standalone MCP server — the existing ACP `SwebKitToolsMcpBridge` already exposes the new
  `IAgentTool`s to external agents for free.
- No repo-linked `.swebkit-sql/` query folders in Phase 1 (documented follow-up using the
  `LinkedCollectionRootRepository` pattern).

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Patterns mirrored: `src/SwebKit.Core/Services/AzureCredentialFactory.cs` (AZ-4),
  `src-sidecar/Services/SidecarRedisConnectionPool.cs`, `src-sidecar/Endpoints/RedisEndpoints.cs`,
  `src/SwebKit.Observability/AppInsightsDiscoveryService.cs`,
  `src/SwebKit.Agents/Tools/Redis/` (tool + ToolContext + propose/executor pattern),
  `src/SwebKit.Core/Domain/WorkspaceTopology.cs`,
  `src-sidecar/Services/WorkspaceRelationshipSuggestionService.cs`,
  `src/SwebKit.Agents/Tools/InvestigateWorkspaceIssueTool.cs`
