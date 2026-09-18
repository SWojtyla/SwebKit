# Test Plan — Agent Workspace Awareness

Existing baselines that must stay green: `SwebKit.Sidecar.Tests` 466,
`vitest` 473, Playwright 345, `tsc --noEmit`, `dotnet build` 0 warnings.

## Module 1 — screen-state snapshot

### Unit — sidecar (`tests/SwebKit.Sidecar.Tests/`)

- `ScreenStateStore`: latest-wins per session key; TTL expiry returns no
  snapshot; oversized payload rejected; independent keys don't collide
  (contextual session vs `"global"`).
- `GetScreenStateTool`: no snapshot → `{ available: false }` graceful JSON;
  valid snapshot → bounded JSON with `capturedAt`/`ageSeconds`; description
  contains the "call when user refers to visible data" cue.
- `AgentToolCallOrchestrator`: `get_screen_state` survives the feature-area
  fence on a contextual (e.g. `"Aks"`) scope; present in workspace scope;
  absent when tool calling is off.
- `POST /api/agent/screen-state`: accepts valid payload → store updated;
  oversized → 4xx; malformed → 4xx.

### Unit — web (`vitest`)

- `screen-state.ts` registry: register/unregister lifecycle; debounced publish;
  only the route-matching provider contributes; payload includes
  `capturedAt` + `route`.
- Each serializer: bounded output (row cap + `+N more`, string truncation);
  **secret hygiene test** — no emitted key matches
  /token|secret|password|authorization|connectionstring/i (D5).
- Global fallback: navigating routes publishes a route+title snapshot.

### E2E — Playwright (demo mode)

- Contextual panel on AKS page: ask "why is this pod failing?" →
  `get_screen_state` tool call appears in the execution status; the answer
  references on-screen data; no redundant `list_pods`-style refetch for data
  already in the snapshot.
- ACP-profile variant (if demo-able): same question via ACP agent → tool call
  flows through the MCP bridge allowlist.
- Navigate away mid-conversation → next question gets the *new* screen's
  snapshot (TTL/route correctness), not the stale one.

## Module 2 — investigation depth

### Unit — sidecar

- `ProactiveInvestigationRunner`:
  - loop terminates at the tool-round cap with partial evidence;
  - wall-clock budget exceeded → graceful stop;
  - only read tools are ever offered (assert `propose_*` absent from the
    resolved tool list — ask-mode gate);
  - model error / unparseable output → fallback path, no throw.
- `ProactiveInsightService`: runner invoked instead of the fixed
  `investigate_workspace_issue` call; `_busy` single-flight still drops
  concurrent firings; rule deleted mid-flight → silent return; resource not on
  map → silent return (non-goal unchanged).
- `SeedProactiveInsightSession` overload: seeded transcript contains hypothesis
  + evidence entries in order.

### E2E — Playwright (demo mode)

- Fire a demo alert whose resource is on the map → insight card appears with
  hypothesis + evidence; "open" → chat session shows the seeded investigation.
- Alert on unmapped resource → no card, no error toast (silent drop).
- Two alerts in quick succession → only one investigation runs (single-flight).

## Module 3 — alert → AI activation + background notify

### Unit

- `AlertRule` serialization: missing `aiInvestigationEnabled` on an old
  persisted rule deserializes to `true`; explicit `false` round-trips.
- `ProactiveInsightService`: fired rule with `AiInvestigationEnabled=false` →
  no runner call, logged skip, no insight event.
- Frontend: dialog checkbox defaults checked; row indicator reflects the flag.

### E2E — Playwright (demo mode)

- Disable AI on a rule → fire that alert → alert toast/history entry appears
  but no insight card ever arrives.
- Insight-ready path → `showNotification` invoked (mock `invoke` and assert
  `show_notification` called with "Investigation ready").

## Regression watch

- `AgentSystemPromptBuilder` untouched in scope — the `## Current focus` +
  `## Workspace map` sections must be unchanged (module adds the tool, not
  prompt weight).
- `useContextualAgent` signature stays backward compatible — existing
  `featureArea`/`selection` call sites must not break.
- Monitoring SSE stream: `alertFired` events unchanged; only
  `proactiveInsightReady` gains optional fields.
