# AI-Augmented App — Test Plan

Scope follows `technical-plan.md`'s modules. Existing patterns from `tauri-react-primary-tool` carry
over directly: sidecar endpoint handlers extracted as named `internal static` methods for direct
xUnit testing without `WebApplicationFactory` (see e.g. `AksEndpointsTests.cs`,
`ServiceBusEndpointsMutationTests.cs` for the pattern to follow), and Playwright specs against demo
mode for frontend flows.

## Module 1 — Capability testing — done

- [x] Unit (`AgentEndpointsTests.cs`): `TestProfileAsync` against an unknown profile id returns
  `NotFound`; against a known profile, returns the tester's `CapabilityTestResult` (verified against
  a fake `HttpMessageHandler`+`HttpClient`, reusing the existing `FakeHttpMessageHandler`/
  `FakeCredentialStore` doubles from `SidecarAuthHeaderBuilderTests.cs` rather than writing new
  ones) and — per the stateless design decision recorded in `technical-plan.md` — does **not**
  mutate the profile in `UserSettingsRepository`; asserted directly rather than assumed.
- [x] `AgentCapabilityTester` itself was confirmed to have **no** existing coverage in
  `tests/SwebKit.Agents.Tests` (checked directly, not assumed) — out of scope for this module, which
  only wires up already-existing, unmodified logic; its own unit tests remain a gap, not one this
  module introduced.
- [x] E2E (`settings.spec.ts`): "Test connection" button shows the mocked capability result
  (`test connection button reports capability from the sidecar`), and a base-URL edit survives a
  reload (`agent profile base URL persists across reload` — a regression test for the `endpointUrl`
  field-name bug found and fixed alongside this module).

## Module 2 — Per-session conversations — done

- [x] Unit (`AgentEndpointsTests.cs`): `SendAsync_DifferentSessionIds_HaveIndependentHistory` — two
  sessions' histories don't leak into each other, and clearing one doesn't touch the other.
  `SendAsync_OmittedSessionId_UsesTheSameGlobalSessionAsBeforePerSessionSupport` — the no-arg
  overload and an explicit `null` land in the same session, matching pre-Module-2 behavior exactly
  (a real regression check, not an assumption).
- [x] Idle eviction ended up lazy-on-access rather than a scheduled sweep (see technical-plan.md),
  which sidesteps the need for a fake-clock unit test entirely — there's no timer to fake. Not
  separately unit-tested as a result; if a scheduled-sweep design is ever adopted instead, add the
  fake-clock test this bullet originally called for at that point.
- [x] E2E (`agent.spec.ts`, pre-existing, unchanged): all 6 tests still pass against the refactored
  service — confirms the global `/agent` page's behavior wasn't disturbed by the session-scoping
  change underneath it.

## Module 3 — Confirm-before-execute — done

- [x] Unit (`AgentPendingApprovalsEndpointsTests.cs`): full propose(direct `RegisterAction`) →
  confirm → apply round trip through `AgentEndpoints.ConfirmActionAsync` (not just the underlying
  coordinator/applier classes directly); confirming an unknown id 404s; rejecting then confirming
  the same id fails cleanly; confirming when no executor handles the type fails with a clear
  message rather than a crash; `GetPendingApprovals` excludes rejected/expired actions and never
  exposes `Payload`.
- [x] Unit (`AgentActionApplierTests.cs`): every validation gate tested in isolation — unconfirmed,
  rejected, expired, already-applied all fail *without* reaching the executor (asserted by checking
  the fake executor's `LastApplied` stays null); dispatch picks the executor whose `CanHandle`
  matches; no matching executor fails with a distinguishable message; an executor throwing is
  caught and returned as a failure result, not left unhandled or marked as applied.
- [x] Unit (`ApiClientActionExecutorTests.cs`): `Create`/`Update`/`Move` call through to
  `IApiClientAgentService` with exactly the fields present in `Payload` (verified against a fake
  service recording its last call, not just checking `IsSuccess`); missing payload fails cleanly
  without calling the client; `Delete`/`Duplicate` extract the request id from `Target`;
  `ExecuteHttpRequest` fails with a clear not-implemented message *and* still enforces the
  fingerprint check first (a stale fingerprint fails with the freshness error, not the
  not-implemented one — order matters and is tested).
- [x] E2E (`agent.spec.ts`): `PendingActionCard` renders summary/risk/preview and confirm/reject
  both produce the expected outcome, mocked via `page.route` (no real tool can propose an action
  in the sidecar yet — that's Module 4 — so this exercises the UI/API contract, not a live
  end-to-end proposal). The confirm test is also what caught the invalidation-timing bug recorded
  in technical-plan.md — it failed first, then got fixed, then passed; not written after the fact.

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
