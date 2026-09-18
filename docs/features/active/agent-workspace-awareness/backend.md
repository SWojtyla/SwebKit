# Backend — Agent Workspace Awareness

All backend work lives in `src-sidecar/` + `src/SwebKit.Agents/` (the live
stack). Nothing in `src/SwebKit.App/` — legacy.

## Module 1 — screen-state snapshot

### Snapshot ingest

- `POST /api/agent/screen-state` added in `src-sidecar/Endpoints/AgentEndpoints.cs`
  (alongside the existing `/api/agent/chat` routes).
- Payload shape (frontend-owned, see `frontend.md`):

  ```json
  {
    "sessionId": "ctx-<mount-id> | global",
    "route": "/aks",
    "featureArea": "Aks",
    "capturedAt": "2026-09-18T10:31:00Z",
    "snapshot": { "...bounded area-specific JSON..." }
  }
  ```

- New `ScreenStateStore` singleton in `src-sidecar/Services/`:
  - Keyed by `sessionId` (contextual panels get a stable per-mount session id via
    `useContextualAgent`; the global page posts under `"global"`).
  - Latest-wins per key; entries expire after a TTL (~5 min) so a closed panel's
    snapshot can't be served as current.
  - Rejects payloads over a hard byte cap (~8 KB) — belt-and-suspenders on top of
    the serializer bounds.
- DI: `builder.Services.AddSingleton<ScreenStateStore>()` in
  `src-sidecar/Program.cs` near the other agent services (~line 135).

### `GetScreenStateTool`

- New file `src/SwebKit.Agents/Tools/GetScreenStateTool.cs`, implementing
  `IAgentTool`:
  - `Name`: `get_screen_state`
  - `Description`: "Returns what the user is currently looking at in the app —
    the visible data on their screen (already fetched by the UI). Call this when
    the user's question refers to what they can see, instead of re-fetching."
  - Parameters: none required; optional `session_id` override is unnecessary —
    the tool resolves the calling session's snapshot via the context already
    threaded through `AgentToolCallOrchestrator.BuildStepTrackingToolExecutor`
    (which receives `context?.Selection` today — extend to pass session id).
  - `FeatureArea`: needs fence-exempt treatment — see D4 in `decisions.md`.
    Simplest correct option: keep `FeatureArea.Workspace` and add
    `get_screen_state` to the same exemption the orchestrator gives
    Observability tools (`AgentToolCallOrchestrator.cs` ~line 73), since it reads
    UI state rather than area data.
- Response on no-snapshot: a plain JSON `{ "available": false, "reason": ... }`
  so the model falls back to normal tools instead of retrying.
- Response includes `capturedAt` and `ageSeconds` so the model can judge
  freshness and say "as of your screen 40s ago…".

### ACP reachability

No extra work: `SwebKitToolsMcpBridge` exposes whatever the per-request
allowlist resolves; once `get_screen_state` is registered + exempt it flows to
ACP agents automatically. Verify in the bridge's `?tools=` allowlist test.

## Module 2 — investigation depth

### `ProactiveInvestigationRunner` (new, `src-sidecar/Services/`)

Headless, bounded agent loop that replaces the single fixed tool call in
`ProactiveInsightService.HandleAlertFiredAsync` (currently ~line 110: one
`investigate_workspace_issue` execution + `SummarizeAsync`).

- Drives `IAgentModelClient` directly (router resolves the active profile —
  capability check stays at the call site).
- Tools resolved through `AgentToolCallOrchestrator` at **workspace scope +
  ask mode** — every read tool across all areas is reachable, `propose_*`
  mutations are filtered out by the existing mode gate. Mutations are
  structurally impossible in an unsupervised run.
- Loop bounds (mirroring `MistralHttpClient`'s existing 5-round pattern):
  max ~5 tool rounds, max ~90 s wall-clock, max ~N tool calls total.
- System prompt: reuse `AgentSystemPromptBuilder` so the run sees the same
  workspace context + user-declared topology map; add a run-specific instruction
  block: "An alert fired: {ruleName} — {message}. Investigate starting from
  {node}. Return JSON: hypothesis, evidence[], severity, suggested_next_steps[]."
- Output: structured JSON → mapped onto the seeded session and insight event.

### `ProactiveInsightService` changes

- `HandleAlertFiredAsync` calls the runner instead of the fixed
  `investigate_workspace_issue` + `SummarizeAsync` pair.
- `investigate_workspace_issue` remains registered — the runner may call it as
  one step among several (e.g. topology walk + `get_pod_logs` + `query_logs`).
- Guardrails unchanged: `_busy` single-flight, capability ≥ ToolCalling,
  rule-must-exist, topology-node-must-exist, fire-and-forget off `AlertFired`.
- `SeedProactiveInsightSession` — extend (or overload) to carry the evidence
  list, not just summary + raw report JSON. `ProactiveInsightReadyEvent` keeps
  its shape; `Summary` becomes the one-line hypothesis.

### Failure modes (must stay non-fatal — never take the alert engine down)

- Runner throws / times out → log warning, emit nothing (today's behavior).
- Model returns unparseable JSON → fall back to today's single-shot summary path
  or emit hypothesis-only. Decide at implementation; record in `decisions.md`
  if it diverges.
- Tool-less profile → existing early return, unchanged.

## Module 3 — alert → AI activation + background notify

### Rule flag

- `src/SwebKit.Core/Models/MonitoringModels.cs` — add
  `public bool AiInvestigationEnabled { get; set; } = true;` to `MonitoringAlertRule`.
  Default `true` preserves today's implicit auto-investigate behavior; old
  persisted rules without the field deserialize to `true` via the initializer.
- `web/src/lib/api.ts` — `MonitoringAlertRule` gains `aiInvestigationEnabled`.
- Rule create/update endpoints already round-trip the whole `MonitoringAlertRule` — no
  endpoint change needed beyond the model field (verify in
  `MonitoringEndpoints.cs` rules handlers).

### Gate in the pipeline

- `ProactiveInsightService.HandleAlertFiredAsync` — after the existing
  `rule is null` check: `if (!rule.AiInvestigationEnabled) return;` with an
  informational log ("AI investigation disabled for rule …").

### Insight-ready OS notification

- `MonitoringPage`'s `onInsightReady` handler — additionally calls
  `showNotification("Investigation ready", $"{ruleName} — {summary}")` via
  `web/src/lib/tauri-bridge.ts` → `src-tauri/src/native.rs show_notification`.
  The sidecar keeps evaluating alerts while the window is minimized (SSE stays
  connected), so the toast reaches the user in the background case. Truncate
  the summary to ~200 chars for the toast body.

## Files touched (expected)

| File | Change |
| ---- | ------ |
| `src-sidecar/Endpoints/AgentEndpoints.cs` | `POST /api/agent/screen-state` |
| `src-sidecar/Services/ScreenStateStore.cs` | new |
| `src/SwebKit.Agents/Tools/GetScreenStateTool.cs` | new |
| `src-sidecar/Services/AgentToolCallOrchestrator.cs` | fence exemption |
| `src-sidecar/Services/ProactiveInvestigationRunner.cs` | new |
| `src-sidecar/Services/ProactiveInsightService.cs` | call runner, structured seed |
| `src-sidecar/Services/SidecarAgentChatService.cs` | seed overload for evidence |
| `src-sidecar/Program.cs` | DI registrations |
| `tests/SwebKit.Sidecar.Tests/` | store, tool, runner, orchestrator tests |
