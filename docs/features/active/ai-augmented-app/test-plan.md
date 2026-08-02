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

## Module 4 — Redis and Storage tools — done

- [x] Unit (`RedisToolsTests.cs`, 19 tests): each read tool against a mocked `IRedisClient` — happy
  path, not-configured error, missing-required-param error (asserted to never touch the client),
  requested-cache-id-not-found error, demo-mode branch. `AnalyzeCacheHealthTool`'s health-summary
  logic tested as a 5-case boundary table (healthy / warning-from-memory / warning-from-slow-log /
  critical-from-memory / critical-from-slow-log), plus a client-throws-returns-Critical case.
  Both propose tools verified to register the right `AgentActionType`/`AgentActionRisk` and to
  carry the proposed fields in `Payload`.
- [x] Unit (`RedisActionExecutorTests.cs`, 5 tests): `CanHandle` scoped to exactly the two Redis
  action types; delete/set-ttl/remove-ttl each call the exact expected `IRedisClient` method with
  the exact expected arguments (verified via `Mock.Verify`, not just checking `IsSuccess`); missing
  payload fails without ever resolving a client (`factory.Verify(..., Times.Never)`).
- [x] Unit (`StorageToolsTests.cs`, 7 tests; `StorageActionExecutorTests.cs`, 4 tests): same rigor
  for the Storage side — `ProposeCopyBlobTool`/`StorageActionExecutor` verified against
  `BlobCopyOptions`' exact fields, including a client-reports-failure case that propagates the
  real `ErrorMessage` rather than a generic one.
- [x] All 16 pre-existing tools' `FeatureArea` retrofit verified indirectly: the full
  `SwebKit.Agents.Tests` suite (166/166) and `SwebKit.Sidecar.Tests` suite (182/182) both had to
  keep passing after the interface change, including two local test-fake `IAgentTool`
  implementations (`ToolMetadataTests.cs`, `SidecarAgentChatServiceToolsTests.cs`) that needed the
  same one-line fix as the real tools — caught by the compiler, not missed.
- [x] Verified both hosts build clean with every new tool/executor wired: `dotnet build` on
  `SwebKit.Sidecar.csproj`, `SwebKit.Agents.csproj`, and `SwebKit.App.csproj` (the MAUI target,
  since Module 4 also updated `SwebKitServiceCollectionExtensions.Agents.cs` for parity).

## Module 5 — Contextual system prompt + mode filtering — done

- [x] Unit (`SidecarAgentChatServiceFilteringTests.cs`, 11 tests, against a real
  `SidecarAgentChatService` + fake tools of mixed `Kind`/`FeatureArea`, capturing the actual
  `AgentModelRequest.Tools` sent to a `FakeAgentModelClient` — not just the filtering logic in
  isolation): `ChatOnly` capability sends zero tools regardless of mode/context (capability always
  wins); mode alone (no context) — "ask" keeps only Read tools *from every area*, "ask_and_do" keeps
  everything; context alone (`ask_and_do` mode) — an `"Aks"` context keeps only Aks tools, including
  the mutate one; both together — an `"Aks"` context *and* "ask" mode keeps only the one tool that's
  both Read and Aks. Capability × mode is exercised as an actual 2×2 via these cases, not just two
  happy paths.
- [x] Unit: omitted mode, empty-string mode, and a made-up mode string (`"not-a-real-mode"`) all
  verified to never include the mutate tool — the three ways "no valid mode was given" can actually
  arrive over JSON, not just the `null` case.
- [x] Unit: an unparseable `featureArea` string falls through to no area filtering (asserted against
  the full expected tool list, not just "didn't throw") — confirms it's a fail-safe, not a
  fail-to-empty that would look like a bug.
- [x] Unit: `BuildCurrentFocusSection` — the system prompt contains `"## Current focus"`, the area
  name, and every selection key/value when context is provided; contains none of that when it isn't
  (both directions asserted, not just the positive case).
- [x] `IAgentTool.FeatureArea`'s retrofit onto all 16 pre-existing tools was already exercised by the
  full test suites staying green through Module 4 (the compiler enforces the assignment exists;
  these tests exercise that the *right* tools are scoped Read/Aks/etc. via the fake-tool fixtures
  above, which is the part the compiler can't check).

## Module 6 — Contextual entry points / mode UI — done

- [x] E2E (`contextual-assistant.spec.ts`, 9 tests, all via `page.route` interception of
  `/api/agent/chat` capturing the real request body — not just "the panel opened"): AKS, Redis,
  Storage, and Service Bus entry points each open the panel and send the correct
  `context.featureArea` (and, for Redis/Storage, the right `selection` key/value); Monitoring's
  entry point derives its area from a freshly-created rule's default source (`AksPodHealth` →
  `"Aks"`) rather than asserting a hardcoded literal, so the test would actually fail if that
  derivation regressed; the mode toggle switching to Ask & do is reflected in the next request's
  `mode` field; closing the panel removes it from the DOM.
- [x] E2E: API Client's `GenerateApiRequestPanel` — submitting a description sends `mode:
  "ask_and_do"`, `context.featureArea: "ApiClient"`, and a message referencing
  `propose_api_request_change` (asserting the actual nudge text landed, not just that *a* message
  was sent); a mocked proposal response surfaces as the shared `PendingActionCard`, not a silent
  mutation.
- [x] E2E (`agent.spec.ts`): a mocked reply containing bold text, a list, and a fenced code block
  renders as real `<strong>`/`<li>`/`<code>` elements, and the raw `**pod-a**` markdown syntax never
  appears as literal text — confirms it's actually parsed, not just dumped as a monospace string
  like before this module (this is the one test in the file exercising a non-trivial reply; every
  other mocked reply in the suite happens to contain no markdown syntax, so this was the gap worth
  closing rather than leaving implicit).

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
