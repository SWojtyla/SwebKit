# SwebKit Architecture Context Index

## Mandate

**This is the context router.** It answers: _given this task, what architecture docs should I read before coding?_

Update this file when architecture docs are added, renamed, split, or when a common implementation task needs a different preload path.

## Required Preload

For any non-trivial implementation, read:

1. `docs/context.md` (global orientation)
2. `architecture.md`
3. `codebase-guide.md`
4. The `design.md` section or functionality deep dive routed below
5. Relevant `docs/pitfalls/` files

## Task Routing

| If the task touches                                                              | Read first                                                                            | Then read                                                            | Validation hints                                                                            |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| App startup, sidecar lifecycle, shell layout, navigation                         | `architecture.md`, `design.md` App Bootstrap Flow                                     | `docs/pitfalls/react-frontend.md`                                    | `dotnet build src-sidecar`, `npm --prefix web run build`; verify sidecar port + dashboard.   |
| Dashboard tiles, readiness, favorites, pins                                      | `functionalities/dashboard.md`                                                        | `functionalities/settings-and-configuration.md`                      | Dashboard e2e specs; ui-state persistence.                                                  |
| Service Bus namespaces, entities, messages, DLQ, scheduled                       | `functionalities/service-bus.md`, `design.md` Feature Data Flow                        | `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`       | Focused SB tests; destructive confirmations; refresh behavior.                              |
| AKS diagnostics, resources, logs, YAML, shell, port-forward                      | `functionalities/aks.md`                                                              | `docs/pitfalls/react-frontend.md` (log buffering), `dotnet-csharp.md` | AKS tests + e2e; auto-refresh pause/resume; pod shell via `src-tauri/src/pod_shell.rs`.     |
| Redis browsing, key detail, health, slowlog, pub/sub                             | `functionalities/redis.md`                                                            | `docs/pitfalls/dotnet-csharp.md`                                     | Focused Redis tests; selection/detail state.                                                |
| Blob/Storage containers, blobs, preview, copy/download, SAS                      | `functionalities/storage.md`                                                          | `docs/pitfalls/azure-sdk.md`, `react-frontend.md`                    | Account switching, container switching, blob detail actions.                                |
| SQL schema browsing, read-only queries, discovery                                | `functionalities/sql.md`                                                              | `docs/pitfalls/azure-sdk.md`                                         | `SqlStatementGuard` tests; Sql endpoints now in CI filter.                                  |
| Observability (App Insights discovery + KQL via agent tools)                     | `functionalities/observability.md`, `design.md` Feature Data Flow                      | `docs/pitfalls/azure-sdk.md`                                         | `GetMetrics`/`QueryLogs` tool tests; demo provider parity.                                   |
| Monitoring alert rules, engine, signal sources, SSE                              | `functionalities/monitoring.md`, `design.md` Monitoring Alert Flow                     | `docs/pitfalls/dotnet-csharp.md`                                     | Rule persistence, cooldown, signal-source skipped/firing, event stream.                     |
| Agent chat, tools, ACP host, proactive insights                                  | `functionalities/agent.md`, `design.md` Agent Chat Flow                                | `docs/pitfalls/agent-workflow.md`                                    | Tool tests in `SwebKit.Agents.Tests`; ACP session tests in Sidecar tests.                   |
| Settings, profile persistence, bundle import/export, workspace maps              | `functionalities/settings-and-configuration.md`, `design.md` Settings Save Flow        | `docs/pitfalls/dotnet-csharp.md`, `azure-sdk.md`, `react-frontend.md` | Atomic persistence, `.bak` recovery, credential-key references, readiness badges.           |
| API Client collections, requests, environments, auth, variables, Git, WS         | `functionalities/api-client.md`                                                       | `docs/pitfalls/react-frontend.md`, `dotnet-csharp.md`, `azure-sdk.md` | `npm run test:unit`, API Client Playwright specs; git allowlist scoping; secret masking.    |
| Tauri commands (files, git, secrets, pod shell, port-forward)                    | `src-tauri/src/*.rs` + `codebase-guide.md`                                            | `docs/pitfalls/react-frontend.md`                                    | `cargo clippy --all-targets -- -D warnings`, `cargo test`.                                  |
| Tests, build plumbing, validation strategy                                       | `codebase-guide.md`, relevant deep dive                                               | Relevant `docs/pitfalls/` files                                      | Focused tests first, then broader builds.                                                   |

## Scale-Out Docs

Existing functionality deep dives (⚠ still carry MAUI-era references; being refreshed per feature
in the codebase-quality program — trust code over `.razor` pointers):

- `functionalities/dashboard.md`
- `functionalities/service-bus.md`
- `functionalities/aks.md`
- `functionalities/redis.md`
- `functionalities/storage.md`
- `functionalities/sql.md`
- `functionalities/observability.md`
- `functionalities/monitoring.md`
- `functionalities/settings-and-configuration.md`
- `functionalities/api-client.md`
- `functionalities/agent.md`

Current pitfall docs:

- `docs/pitfalls/react-frontend.md`
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/agent-workflow.md`
- `docs/pitfalls/api-client.md`

## Update Rules

| Change type                                                                       | Docs to update                                                                                              |
| --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| New top-level project, external integration, runtime boundary, or persisted store | `architecture.md`, `codebase-guide.md`, and this index                                                      |
| New or changed cross-component flow                                               | `design.md` and the relevant functionality deep dive                                                        |
| New routed page, shell area, navigation entry                                     | `codebase-guide.md`, `design.md` if flow changes, and relevant functionality deep dive                       |
| Behavior change inside an existing app capability                                 | Matching `functionalities/*.md` file and feature plan when active                                           |
| New persisted state, migration behavior, or app-data file                         | `functionalities/settings-and-configuration.md`, `architecture.md` cross-cutting concerns, and tests        |
| New recurring failure mode or reviewed mistake                                    | Relevant `docs/pitfalls/*.md` file and `docs/pitfalls/index.md` when a new file is added                    |
| New architecture decision that should survive the feature                         | Decisions section of the active feature plan, or `architecture.md`/`design.md` when repo-wide               |
