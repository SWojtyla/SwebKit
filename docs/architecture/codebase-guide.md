# SwebKit Codebase Guide

## Mandate

**This is the implementation navigation map.** It answers: _where do I start looking in the code?_

Update this file when project/folder structure changes, entry points move, or naming conventions and
cross-cutting patterns are introduced or retired.

## Entry Points by Task Type

| Task                                                        | Starting file                                                                                          |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| App startup, sidecar spawn, Tauri command/plugin wiring     | `src-tauri/src/lib.rs`, `src-tauri/src/sidecar.rs`                                                     |
| Frontend bootstrap, query defaults, router                  | `web/src/main.tsx`, `web/src/App.tsx`                                                                  |
| Sidecar DI composition and global error handling            | `src-sidecar/Program.cs`                                                                               |
| Sidecar endpoints for one feature                           | `src-sidecar/Endpoints/<Feature>Endpoints.cs`                                                          |
| Frontend shell layout, sidebar, top bar, notifications      | `web/src/components/layout/`                                                                           |
| Feature page components                                     | `web/src/components/<feature>/` (page + `*PageContext.tsx` where present)                              |
| Shared UI primitives                                        | `web/src/components/ui/`                                                                               |
| Typed API calls + sidecar base URL                          | `web/src/lib/api.ts`                                                                                   |
| Server-state hooks (TanStack Query)                         | `web/src/lib/hooks/use*.ts`                                                                            |
| Client-state stores (zustand)                               | `web/src/lib/stores/`                                                                                  |
| TS mirror of sidecar models                                 | `web/src/lib/types.ts`                                                                                 |
| Profile/config persistence                                  | `src/SwebKit.Core/Configuration/ProfileRepository.cs`, `ConfigEndpoints.cs`                            |
| UI state persistence (tabs, filters, preferences)           | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                                  |
| User settings (theme, logging, toggles)                     | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`                                             |
| Structured file logging (engine, redaction, retention)      | `src/SwebKit.Core/Diagnostics/`                                                                        |
| Secret storage and retrieval                                | `src-tauri/src/secrets.rs`, `src-sidecar/Services/SidecarCredentialStore.cs`                           |
| Service Bus client behavior                                 | `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`, `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs` |
| AKS operations (logs, YAML, port-forward, shell, apply)     | `src/SwebKit.Core/Abstractions/IAksClient.cs`, `src/SwebKit.Kubernetes/AksClient/`                     |
| Redis operations                                            | `src/SwebKit.Core/Abstractions/IRedisClient.cs`, `src/SwebKit.Redis/RedisClient.cs`                    |
| SQL Server/Azure SQL browsing, queries, discovery           | `src/SwebKit.Core/Abstractions/ISqlClient.cs`, `src/SwebKit.Sql/`, `src-sidecar/Endpoints/SqlEndpoints.cs` |
| Blob/file-share Storage operations                          | `src/SwebKit.Core/Abstractions/IStorageClient.cs`, `src/SwebKit.Azure/Storage/AzureStorageClient.cs`   |
| Observability queries and App Insights discovery            | `src/SwebKit.Observability/AzureAppInsightsProvider.cs`, `AppInsightsDiscoveryService.cs`              |
| Agent chat orchestration and streaming                      | `src-sidecar/Services/SidecarAgentChatService.cs`, `src-sidecar/Services/AgentChat/`                   |
| Agent tools                                                 | `src/SwebKit.Agents/IAgentTool.cs`, `src/SwebKit.Agents/Tools/`                                        |
| ACP external-agent host + MCP bridge                        | `src-sidecar/Services/Acp/`                                                                            |
| Monitoring alert engine + signal sources                    | `src-sidecar/Services/MonitoringAlertEvaluationService.cs`, `IAlertSignalSource` impls per lib         |
| API Client execution pipeline (auth, vars, capture)         | `src/SwebKit.Core/Services/` (`HttpRequestExecutor`, `VariableSubstitutionService`, `AuthInheritanceResolver`), `src-sidecar/Services/SidecarAuthHeaderBuilder.cs` |
| API Client git integration                                  | `src-tauri/src/git.rs`, `web/src/components/api-client/GitPanel.tsx`                                   |
| Pod shell (xterm.js ↔ kubectl exec)                         | `src-tauri/src/pod_shell.rs`, `web/src/components/aks/`                                                |
| Workspace warm-up / resume                                  | `web/src/lib/hooks/useWorkspaceWarmup.ts`, `src-sidecar` startup warm-up in `Program.cs`               |

## Key Folders and Responsibilities

```
src-tauri/src/
├── lib.rs                    # Tauri command + plugin registration
├── sidecar.rs                # sidecar process spawn, port discovery
├── native.rs                 # port-forward, file pickers, clipboard, explorer
├── pod_shell.rs              # kubectl exec PTY sessions for xterm.js
├── git.rs                    # allowlisted git ops for API Client linked repos
└── secrets.rs                # OS keychain commands

web/src/
├── App.tsx                   # lazy routes per feature
├── main.tsx                  # QueryClient, sidecar URL resolution, mount
├── lib/
│   ├── api.ts                # apiFetch/apiSend + typed calls
│   ├── types.ts              # TS mirror of C# domain models
│   ├── hooks/                # useServiceBus, useAks, useRedis, … (TanStack Query)
│   ├── stores/               # zustand stores (selection, prefs, agent conversation)
│   └── tauri-bridge.ts       # invoke() wrappers, no-op in browser dev
└── components/
    ├── layout/               # sidebar, top bar, notification system
    ├── ui/                   # shared primitives (Select, ConfirmBar, SidePanel, …)
    └── <feature>/            # service-bus, aks, api-client, storage, redis, sql,
                              # monitoring, agent, dashboard, settings

src-sidecar/
├── Program.cs                # DI composition + middleware + endpoint mapping
├── Endpoints/                # Map*Endpoints per feature
└── Services/                 # connection pools, agent chat, alert engine, ACP/, DemoModeService

src/
├── SwebKit.Core/             # Abstractions/, Domain/, Models/, Configuration/, Services/, Diagnostics/, Serialization/
├── SwebKit.Azure/            # ServiceBus/, Storage/
├── SwebKit.Kubernetes/       # AksClient/ (partial classes by area)
├── SwebKit.Redis/
├── SwebKit.Sql/
├── SwebKit.Agents/           # IAgentTool, registry, Tools/<feature>/
└── SwebKit.Observability/

tests/                        # xUnit, one project per src project
web/e2e/                      # Playwright against demo mode
```

## Naming Conventions

| Pattern                          | Meaning                                                                          |
| -------------------------------- | -------------------------------------------------------------------------------- |
| `I*Client`                       | Integration interface in `SwebKit.Core/Abstractions/`                             |
| `Azure*`/`Kubernetes*`/`RedisClient`/`Sql*Client` | Concrete SDK implementation                                              |
| `Demo*`                          | Synthetic implementation used when `AppStateService.UseDemoData` is on            |
| `*Repository`                    | JSON-backed persistence in `SwebKit.Core/Configuration/`                          |
| `Sidecar*ConnectionPool`         | Singleton per-resource SDK client cache in `src-sidecar/Services/`                |
| `*Endpoints.cs`                  | `Map*Endpoints` extension module in `src-sidecar/Endpoints/`                      |
| `*PageContext.tsx`               | Per-page React context+provider (god-context smell — see quality program)         |
| `use*.ts` in `lib/hooks/`        | TanStack Query hooks per feature                                                  |
| `*-page`, `*-detail` testids     | Playwright selectors — see `.agents/skills/swebkit-ui-ux-guardrails`              |

## Cross-Cutting Concerns

| Concern                          | Where it lives                                                                             |
| -------------------------------- | ------------------------------------------------------------------------------------------ |
| Dependency injection root        | `src-sidecar/Program.cs`                                                                   |
| Shared app state / demo toggle   | `src/SwebKit.Core/Services/AppStateService.cs`                                             |
| Event bus                        | `src/SwebKit.Core/Services/AppEventBus.cs`                                                 |
| Profile persistence              | `src/SwebKit.Core/Configuration/ProfileRepository.cs`                                      |
| UI state persistence             | `src/SwebKit.Core/Configuration/UiStateRepository.cs`                                      |
| User settings                    | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`                                 |
| Credential storage               | `src-tauri/src/secrets.rs` + `SidecarCredentialStore.cs`                                   |
| Port-forward session lifecycle   | `src-tauri/src/native.rs`                                                                  |
| Monitoring events (SSE)          | `src-sidecar/Endpoints/MonitoringEventStream.cs`                                           |
| Agent streaming (SSE)            | `src-sidecar/Services/SidecarAgentChatService.cs` + `AgentEndpoints.cs`                    |
| Demo mode                        | `DemoModeService` + `SwebKit.Core/Services/Demo*`                                          |
| Styling                          | Tailwind v4 via `web/src/styles/globals.css` + theme presets (`web/src/lib/theme-colors.ts`) |
