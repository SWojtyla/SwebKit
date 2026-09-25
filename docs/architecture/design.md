# SwebKit Design

## Mandate

**This is the component blueprint.** It answers: _how is each component internally structured, and
what are the key flows through it?_

Update this file when control flow, runtime responsibilities, or integration boundaries change
inside a component.

## Scope

Key flows of the active stack (Tauri + React + sidecar):

- App bootstrap and sidecar lifecycle
- Feature data flow (page → hook → `api.ts` → endpoint → pooled client)
- Demo mode
- Agent chat and confirm-before-execute actions
- Monitoring alert evaluation and SSE fan-out
- Settings save and config propagation
- High-volume streams (logs, monitoring events)

Folder maps and broad navigation conventions live in `codebase-guide.md`.

## App Bootstrap Flow

### Intent

Render UI fast while the Rust shell spawns the sidecar on an OS-assigned port; never fire an API
call before the port is known.

### High-Level Sequence

```mermaid
sequenceDiagram
    participant User
    participant Tauri as src-tauri/lib.rs
    participant Sidecar as sidecar.rs
    participant Net as .NET sidecar (Program.cs)
    participant Web as web/src/main.tsx
    participant App as App.tsx

    User->>Tauri: Launch swebkit.exe
    Tauri->>Sidecar: spawn SwebKit.Sidecar (OS-assigned port)
    Sidecar->>Net: dotnet process starts, binds localhost port
    Tauri->>Web: load webview (bundled assets)
    Web->>Web: createRoot — QueryClient defaults (staleTime 30s, no focus refetch)
    Web->>Sidecar: get_sidecar_port command
    Sidecar-->>Web: port
    Web->>Web: initSidecarBaseUrl() resolves http://127.0.0.1:{port}
    Web->>App: render → lazy route → Suspense
    App->>Net: apiFetch("/api/...") via resolved base URL
```

### Design Notes

- Dev (`npm run dev` / Playwright) uses a **fixed** port `5199`; production asks Tauri for the
  OS-assigned one — `initSidecarBaseUrl()` must resolve before any fetch fires.
- `refetchOnWindowFocus: false` is deliberate: alt-tab back must not replay a full
  namespace/cluster fan-out; volatile queries set their own short `staleTime`.
- Program.cs wires `AppBootstrap.ConfigureCrashHandlers` first so even a startup throw lands in
  the file log, then registers all DI, then `Map*Endpoints` per feature.

## Feature Data Flow

### Intent

One predictable pipeline per feature: component → TanStack hook → typed api call → minimal-API
endpoint → pooled SDK client → external service.

### High-Level Sequence

```mermaid
sequenceDiagram
    participant Page as *Page.tsx / *PageContext
    participant Hook as lib/hooks/use*.ts
    participant Api as lib/api.ts
    participant Ep as Endpoints/*Endpoints.cs
    participant Pool as Sidecar*ConnectionPool
    participant Client as SwebKit.<lib> client
    participant Ext as External service

    Page->>Hook: useQuery(queryKey, …)
    Hook->>Api: apiFetch("/api/<feature>/…")
    Api->>Ep: HTTP request
    Ep->>Pool: resolve client for config id
    Pool->>Client: get-or-create SDK client
    Client->>Ext: SDK call
    Ext-->>Client: result
    Client-->>Ep: typed model
    Ep-->>Api: JSON
    Api-->>Hook: parsed data
    Hook-->>Page: { data, isLoading, error }
```

### Design Notes

- `Sidecar*ConnectionPool` exists per integration (Service Bus, Redis, Storage, SQL, Monitoring) —
  endpoints never construct SDK clients directly; that caused the original per-request leak.
- Secrets travel by logical key; `SidecarCredentialStore` resolves them from the OS keychain via
  Tauri commands — raw secrets never sit in `profiles.json`.
- Deep-linkable state lives in URL search params (`useSearchParams`), not in component state — a
  reload or shared link restores the same selection.
- Mutations go through `apiSend`; destructive ones surface a `ConfirmBar`/dialog per the
  UI-guardrails skill.

## Demo Mode Flow

`AppStateService.UseDemoData` swaps every client factory for a `Demo*` implementation at the
connection-pool/selector seam — endpoints and UI are unaware. `DemoModeService` seeds the demo
namespace/cache/storage/SQL IDs the UI lists. The entire Playwright suite runs in demo mode, so a
demo regression breaks e2e immediately.

## Agent Chat + Confirm-Before-Execute Flow

### Intent

Stream LLM replies with tool steps, keep "propose → user confirms → apply" for any mutation, and
let the built-in client or an external ACP agent serve the same UI.

### High-Level Sequence

```mermaid
sequenceDiagram
    participant UI as Agent panel / ContextualAssistant
    participant Chat as SidecarAgentChatService
    participant Router as AgentModelClientRouter
    participant LLM as OpenAiCompatible / Acp client
    participant Reg as AgentToolRegistry
    participant Coord as IAgentActionCoordinator
    participant Exec as IAgentActionExecutor

    UI->>Chat: POST /api/agent/chat (SSE)
    Chat->>Router: resolve client for active profile
    Chat->>Reg: tools allowed for this scope/profile
    Router->>LLM: prompt + tool schemas
    LLM-->>Chat: streamed tokens / tool calls
    Chat->>Reg: execute read-only tool
    Reg-->>Chat: result → next LLM turn
    LLM-->>Chat: propose action (e.g. delete key)
    Chat-->>UI: pending-action SSE event
    UI->>Chat: POST confirm (approve)
    Chat->>Coord: apply action
    Coord->>Exec: feature executor
    Exec-->>UI: applied result event
```

### Design Notes

- `AgentModelClientRouter` resolves the model client **per call** — switching agent profiles needs
  no restart.
- Every `IAgentTool` is a singleton in `Program.cs`; `InvestigateWorkspaceIssueTool` resolves the
  registry lazily via `IServiceProvider` to avoid the circular dependency.
- External ACP agents reach the same tool registry through `SwebKitToolsMcpBridge` (stateless MCP
  over streamable HTTP; per-session allowlist in the `?tools=` URL param).
- `ProactiveInsightService` must be force-instantiated at startup — its constructor subscribes to
  `MonitoringAlertEvaluationService.AlertFired`.

## Monitoring Alert Flow

```mermaid
sequenceDiagram
    participant Engine as MonitoringAlertEvaluationService (hosted)
    participant Src as IAlertSignalSource (AKS/SB/Redis/…)
    participant Store as AlertRuleRepository
    participant SSE as MonitoringEventStream
    participant Insight as ProactiveInsightService

    loop Evaluation interval
        Engine->>Store: load rules
        Engine->>Src: Evaluate(rule)
        Src-->>Engine: signal sample
        Engine->>Engine: threshold + cooldown check
        Engine-->>SSE: AlertFired event
        SSE-->>UI: live badge/toast
        Engine-->>Insight: feed insight pipeline
    end
```

- One engine singleton serves both the hosted-service lifetime and endpoint `ReloadRulesAsync`
  calls after rule CRUD.
- Signal sources register twice in DI: concrete type + `IAlertSignalSource`, so the engine
  resolves `IEnumerable<IAlertSignalSource>`.

## Settings Save and Config Propagation Flow

```mermaid
sequenceDiagram
    participant UI as Settings page
    participant Ep as ConfigEndpoints
    participant Repo as ProfileRepository / UserSettingsRepository
    participant File as %APPDATA%/SwebKit/*.json
    participant Pools as Connection pools

    UI->>Ep: PUT /api/config/…
    Ep->>Repo: mutate + save
    Repo->>File: temp write → atomic replace → refresh .bak
    Ep-->>UI: saved
    Pools->>Pools: invalidate cached clients for changed resource
```

- Repos write atomically (temp + replace) and keep a sibling `.bak` for recovery.
- Some settings are read live rather than snapshotted — e.g. the API-client "verify SSL" toggle is
  consulted per request inside the named `HttpClient` handler, so it applies immediately.

## High-Volume Stream Flow

- **Logs (AKS/agent):** SSE/`EventSource` from the sidecar; frontend uses
  `useLogBuffer`/`log-window.ts` to buffer lines and flush on a timer — never render per line.
  Every `EventSource` is closed in effect cleanup.
- **Monitoring events:** `MonitoringEventStream` broadcasts engine events over SSE.
- See `docs/pitfalls/react-frontend.md` for the full rules.

## Key Reference Points

| File                                        | Responsibility                                              |
| ------------------------------------------- | ----------------------------------------------------------- |
| `src-sidecar/Program.cs`                    | DI root, CORS, global exception handler, endpoint mapping   |
| `web/src/main.tsx`                          | QueryClient defaults, sidecar URL resolution, mount         |
| `web/src/lib/api.ts`                        | `apiFetch`/`apiSend`, error-message extraction              |
| `src-sidecar/Services/DemoModeService.cs`   | Demo-mode resource seeds                                    |
| `src/SwebKit.Core/Configuration/*Repository` | Atomic JSON persistence                                    |
