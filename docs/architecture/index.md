# SwebKit Architecture Context Index

## Mandate

**This is the context router.** It answers: _given this task, what architecture docs should I read before coding?_

Update this file when architecture docs are added, renamed, split, or when a common implementation task needs a different preload path.

## Required Preload

For any non-trivial implementation, read:

1. `architecture.md`
2. `codebase-guide.md`
3. The `design.md` section or functionality deep dive routed below
4. Relevant `docs/pitfalls/` files

## Task Routing

| If the task touches                                                                        | Read first                                                                                              | Then read                                                                                      | Validation hints                                                                                    |
| ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| App startup, shell hydration, global layout, navigation, commands, shortcuts               | `architecture.md`, `design.md` App Bootstrap Flow, `codebase-guide.md` Blazor shell entries             | `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`                               | Build the app project; verify startup banners, tab restore, shortcuts, and route navigation.        |
| Dashboard, dashboard tiles, setup readiness, favorites, recents, open tabs                 | `functionalities/dashboard.md`, `design.md` Dashboard Summary Flow, `design.md` Operator Workspace Flow | `functionalities/settings-and-configuration.md`, `docs/pitfalls/blazor-maui.md`                | Component tests for dashboard states; verify UI-state persistence and drill-through behavior.       |
| Service Bus namespaces, entity browsing, message list, DLQ, scheduled messages             | `functionalities/service-bus.md`, `design.md` Service Bus Namespace and Message Browse Flow             | `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`                                 | Run focused Service Bus tests when touched; verify destructive confirmations and refresh behavior.  |
| AKS diagnostics, Kubernetes resources, logs, YAML, shell, port-forward, monitoring         | `functionalities/aks.md`, `design.md` AKS Diagnostics Flow                                              | `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`                               | Verify bootstrap guards, cancellation, auto-refresh pause/resume, and long-running panel behavior.  |
| Redis browsing, key detail, cache health, slowlog, memory insights                         | `functionalities/redis.md`, `codebase-guide.md` Redis entries                                           | `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`                               | Run focused Redis component tests where available; verify selection and detail state.               |
| Blob Storage containers, blob lists, preview, copy/download flows                          | `functionalities/storage.md`, `codebase-guide.md` Storage entries                                       | `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/blazor-maui.md`                                   | Verify account selection, container switching, and blob detail actions.                             |
| Pipelines, releases, approvals, deployment assurance                                       | `functionalities/releases.md`, `codebase-guide.md` Pipelines and releases entries                       | `docs/pitfalls/dotnet-csharp.md`                                                               | Verify DevOps client snapshots, approval counts, tree/detail state, and status badges.              |
| Observability, App Insights discovery, KQL, failures, performance, logs, availability      | `functionalities/observability.md`, `design.md` Observability Resource and Query Flow                   | `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/azure-sdk.md`                                   | Verify resource selection, KQL query flow, Logs first-use JS interop, and tab state.                |
| Incident Timeline evidence, mappings, snapshot export, proposal generation                 | `functionalities/incident-timeline.md`, `design.md` Incident Timeline flows                             | `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`                               | Verify source coverage, cancellation/version guards, and evidence-first wording.                    |
| Monitoring alert rules, alert engine, signal sources, Windows toast notifications          | `functionalities/monitoring.md`, `design.md` Alert Engine flow                                          | `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`                               | Verify rule persistence, cooldown, signal source skipped/firing, and toast notification.            |
| Settings, configuration health, profile persistence, bundle import/export, appearance      | `functionalities/settings-and-configuration.md`, `design.md` Settings Save and Config Propagation Flow  | `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md`                                 | Verify atomic persistence, backup recovery, credential references, and readiness checks.            |
| API Client collections, requests, environments, auth, variables, linked repos, Git actions | `functionalities/api-client.md`, `codebase-guide.md` API Client entries                                 | `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md` | Run focused API client service/component tests; verify linked-root path scoping and secret masking. |
| Tests, build plumbing, or validation strategy                                              | `codebase-guide.md`, relevant functionality deep dive                                                   | Relevant `docs/pitfalls/` files                                                                | Prefer focused tests for touched areas before broader builds.                                       |

## Scale-Out Docs

Existing functionality deep dives:

- `functionalities/dashboard.md`
- `functionalities/service-bus.md`
- `functionalities/aks.md`
- `functionalities/redis.md`
- `functionalities/storage.md`
- `functionalities/releases.md`
- `functionalities/observability.md`
- `functionalities/incident-timeline.md`
- `functionalities/monitoring.md`
- `functionalities/settings-and-configuration.md`
- `functionalities/api-client.md`

Current pitfall docs:

- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/agent-workflow.md`

## Update Rules

| Change type                                                                       | Docs to update                                                                                                                      |
| --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| New top-level project, external integration, runtime boundary, or persisted store | `architecture.md`, `codebase-guide.md`, and this index                                                                              |
| New or changed cross-component flow                                               | `design.md` and the relevant functionality deep dive                                                                                |
| New routed page, shell area, navigation entry, command, or shortcut surface       | `codebase-guide.md`, `design.md` if flow changes, and relevant functionality deep dive                                              |
| Behavior change inside an existing app capability                                 | Matching `functionalities/*.md` file and feature status docs when active                                                            |
| New persisted state, migration behavior, or app-data file                         | `functionalities/settings-and-configuration.md`, `architecture.md` cross-cutting concerns, and tests                                |
| New recurring failure mode or reviewed mistake                                    | Relevant `docs/pitfalls/*.md` file and `docs/pitfalls/index.md` when a new file is added                                            |
| New architecture decision that should survive the feature                         | Add a decision under `docs/architecture/decisions/` when the decision is repo-wide; otherwise use the active feature `decisions.md` |
