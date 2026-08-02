# AI-Augmented App — Test Plan

Scope follows `technical-plan.md`'s modules. Existing patterns from `tauri-react-primary-tool` carry
over directly: sidecar endpoint handlers extracted as named `internal static` methods for direct
xUnit testing without `WebApplicationFactory` (see e.g. `AksEndpointsTests.cs`,
`ServiceBusEndpointsMutationTests.cs` for the pattern to follow), and Playwright specs against demo
mode for frontend flows.

## Module 1 — Capability testing

- Unit: the new `/api/agent/profiles/{id}/test` handler — success sets `Capability`/
  `LastTestDiagnostic` and persists via `UserSettingsRepository`; unreachable endpoint / model not
  in `/models` list / tool-call probe failure each map to the right `Capability` value and a
  non-empty diagnostic message.
- Unit: `AgentCapabilityTester` itself likely already has coverage in `tests/SwebKit.Agents.Tests`
  — verify existing coverage still applies unchanged, since this module only wires it up, it
  doesn't change its logic.
- E2E: "Test connection" button in `AgentSettings.tsx` shows a result (demo/mocked profile is fine
  here — this is a UI-wiring check, not a real-model check, which belongs in Module 7).

## Module 2 — Per-session conversations

- Unit: two different `sessionId`s produce independent histories (message sent on session A isn't
  visible in session B's next request's history); omitted `sessionId` behaves exactly as today's
  single global session (regression check — existing `/agent` page behavior must not change).
  `ClearHistory`/`GetStatus` scoped correctly per session.
- Unit: idle session eviction (if implemented as a scheduled sweep, test the sweep function
  directly with an injected/fake clock rather than a real sleep — do not write a real-time-based
  test here, per the sleep/hang lesson learned earlier on this branch's pty work).

## Module 3 — Confirm-before-execute

- Unit: full propose → confirm → apply round trip for at least one existing tool
  (`ProposeApiRequestDeleteTool`, since its `ApplyDeleteAsync` branch already works) via the new
  endpoints, not just the underlying coordinator/applier classes directly (the whole point of this
  module is the wiring, so test through the HTTP handler).
- Unit: reject flow — a rejected `PendingAgentAction` cannot later be confirmed.
- Unit: expired action — confirming after `ExpiresAt` fails with a clear, distinguishable error
  (not the same generic failure as "not found" or "already applied" — these are different user-facing
  situations and should be distinguishable in the response).
- Unit: fingerprint mismatch (the target changed between propose and confirm) is rejected rather
  than silently applied against stale state.
- Unit: each new `IAgentActionExecutor` (once that refactor lands) has its own focused tests,
  matching the existing per-endpoint-file test organization (e.g. a `RedisAgentActionExecutorTests`
  alongside `RedisEndpointsMutationTests`).
- E2E: `PendingActionCard` renders summary/risk/preview correctly for at least one mutate action
  end-to-end in demo mode, and Confirm/Reject both produce the expected outcome in the UI.

## Module 4 — Redis and Storage tools

- Unit: each new tool's `ExecuteAsync` against a demo/fake `IRedisClient`/`IStorageClient` —
  correct data returned for the happy path, and (matching the existing pattern in
  `RedisEndpointsMutationTests`/`StorageEndpointsMutationTests`) exceptions from the underlying
  client propagate rather than being swallowed into a false "success".
  correct `Kind`/`Risk`/`RequiredCapability` metadata on each tool (a mutate tool that accidentally
  reports `Kind = Read` would bypass the Module 5 mode filter — this is worth a direct assertion,
  not just behavioral testing).
- Unit: composite tools (`AnalyzeCacheHealthTool` etc.) — the derived health-summary logic
  (Healthy/Warning/Critical thresholds) tested directly with crafted inputs at each boundary,
  mirroring `AnalyzeQueueHealthTool`'s existing test coverage if any exists as a reference.

## Module 5 — Contextual system prompt + mode filtering

- Unit: `BuildSystemPrompt()` (or its successor) includes the "current focus" section only when
  context is provided, and the right fields render into it — test with and without a `selection`.
- Unit: mode filtering — `mode: "ask"` strips every `Kind == Mutate` tool even when the profile's
  capability is `ToolCalling`; `mode: "ask_and_do"` includes them (still gated by capability as
  today). Test both orthogonally: capability × mode is a 2×2 that should be tested as such, not
  just the two "happy path" combinations.
- Unit: feature-area scoping — a request with `context.featureArea: "aks"` receives only tools whose
  `FeatureArea == Aks` (plus whatever mode/capability already allow), even when other areas' tools
  would otherwise pass those gates; a request with no `featureArea` at all (the global `/agent`
  page's existing behavior) is unaffected and receives every area's tools as it does today — this is
  a regression check, not just a new-behavior check, since it's easy to accidentally scope the
  global page too.
- Unit: `IAgentTool.FeatureArea` is set correctly on every existing tool after the retrofit (a tool
  silently left with a wrong/default area would be scoped out of every conversation that should see
  it, or into ones that shouldn't — assert the full registry's area assignments directly rather than
  relying on it coming up in some other test's incidental coverage).

## Module 6 — Contextual entry points / mode UI

- E2E per feature area: opening the "Ask AI" entry point from the relevant detail panel opens a
  contextual panel; the mode toggle is visible and switching it is reflected in a subsequent
  request's `mode` field (can be asserted via a network-request interception in Playwright rather
  than needing a real model reply).
- E2E: API Client's "generate a request" affordance opens its focused prompt (not the generic panel)
  and a successful generation surfaces as a confirm card, not a silent mutation of the open request.
- E2E: markdown rendering — a reply containing a fenced code block or a list renders as such, not
  as literal `` ``` `` / `-` characters (demo/mocked model response is fine here).

## Module 7 — Local-model verification (manual, not automated)

This module is explicitly a **manual verification pass against a real LM Studio instance**, not
something to script into CI — matching the honest approach already taken for pod-shell-exec earlier
on this repo (manual verification recorded in `status.md`, not a fabricated automated-test claim).
Record in `status.md`: model name/size used, capability-test result, and the outcome of one full
Ask conversation and one full Ask & do propose→confirm→apply round trip against it.

## Module 8 — Streaming (only if implemented)

- Unit: SSE chunk parsing/reassembly in the streaming client variant.
- E2E: partial content appears progressively rather than all at once (can be asserted by checking
  intermediate DOM state during a deliberately slow/chunked mock response).

## Regression coverage to re-run, not just add to

- Full existing `SwebKit.Agents.Tests` and `SwebKit.Sidecar.Tests` suites — Module 2's refactor of
  `SidecarAgentChatService`'s internal state is the highest-risk change to existing behavior in this
  plan; the existing global `/agent` page's tests (if any e2e coverage exists for it today) must
  keep passing unchanged.
- `AgentSettings.tsx`'s existing profile CRUD e2e coverage, once the "Test connection" button is
  added alongside it.
