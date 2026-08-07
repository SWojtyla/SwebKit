# Workspace Intelligence — Test Plan

Scope follows `technical-plan.md`'s modules. Same conventions as `ai-augmented-app/test-plan.md`:
sidecar endpoint handlers extracted as named `internal static` methods for direct xUnit testing,
Playwright specs against demo mode for frontend flows, no real-time-based tests for anything that
can instead take an injected/fake clock.

## Cross-feature visualization workspace follow-up — done (2026-08-07)

- [x] E2E (`dashboard.spec.ts`): launching the cross-feature scenario opens one visualization
      workspace with three labeled tabs, keeps the duplicate Mermaid diagram out of the transcript,
      renders only the selected diagram/topology/timeline, verifies the accessible divider expands the
      visual panel by keyboard and persists its width, and restores the inline diagram after close.
- [x] Unit (`agent-visualization.test.ts`, 4 tests): heading-derived labels, kind detection,
      CRLF fences, duplicate elimination, invalid JSON fallback, and malformed-fence handling.
- [x] Frontend production build passes, including TypeScript validation.

## Module 1 — Topology data model — done

- [x] Node/relationship CRUD ended up as **whole-profile PUT**, not dedicated REST verbs (see
  technical-plan.md for why) — so "CRUD endpoint" tests are instead: `WorkspaceTopologyNormalizationTests.cs`
  (`SwebKit.Core.Tests`) covers a legacy profile JSON with no `topology` key at all normalizing to
  non-null empty `Nodes`/`Relationships`, and a full save-then-reload round trip of 2 nodes + 1
  relationship through the real `ProfileRepository`. The since-deleted-node case is handled in the
  UI layer, not the persistence layer: `WorkspaceMapSettings.tsx`'s `removeNode` filters out any
  relationship referencing the removed node in the same save — cascade-delete, not a dangling
  reference or a validation error, covered by the e2e test below.
- [x] Unit (`WorkspaceTopologyEndpointsTests.cs`, `SwebKit.Sidecar.Tests`, 9 tests): the
  auto-populated candidate list reflects current profile config exactly — AKS crosses
  `MonitoredNamespaces` with `WatchedDeployments` (plus the `DefaultNamespace`-fallback and
  no-namespace-known-at-all-produces-nothing cases), one candidate per real Service Bus
  namespace/Redis cache/Storage account, and demo mode overlays the SB/Redis/Storage candidates
  (2/1/1) while ignoring the real config for those three areas — matching
  `ConfigEndpoints.GetProfilesAsync`'s existing demo-overlay behavior exactly.
- [x] E2E (`settings.spec.ts`, 1 new test): add a custom AKS node and a custom Service Bus node via
  the "Add a custom resource" form (candidates aren't guaranteed to exist for a fresh test profile,
  so this exercises the form path rather than depending on AKS/SB being pre-configured), declare a
  relationship between them, reload and confirm both the nodes and the relationship survive: then
  remove the node and confirm the relationship table goes to zero rows — the actual cascade-delete
  behavior, not just "the node disappeared."

## Module 2 — Heuristic suggestions — done

- [x] Unit (`WorkspaceRelationshipSuggestionServiceTests.cs`, new file, 10 tests, against a
  hand-rolled `FakeAksClientForSuggestions` implementing just the 3 `IAksClient` members this
  service touches — matching the established fake-over-mock convention already used elsewhere,
  e.g. `RuntimeDriftServiceTests`' `FakeAksClient`): no AKS nodes / no non-AKS nodes / no configured
  AKS client all return empty without throwing; a pod env var match and a separate ConfigMap-value
  match each produce the expected from/to suggestion (the exact non-matching case is covered too —
  unrelated env var content produces nothing); an already-confirmed relationship (checked both
  directions) is excluded; an AKS node whose `ResourceKey` doesn't have the `namespace/deployment`
  shape is skipped rather than throwing; one AKS node's `GetPodsAsync` throwing doesn't propagate
  or stop the rest of the scan (best-effort, per the plan).
- [x] Unit (`WorkspaceTopologyEndpointsTests.cs`, 1 new): the suggestions endpoint delegates to the
  service and returns its result via `Results.Ok`.
- [x] E2E (`settings.spec.ts`, 1 new): a mocked suggestion (real node ids, extracted from the DOM
  after adding them via the UI — not hardcoded) renders with its from/to labels and reason text;
  confirming it adds a real relationship that survives a reload; dismissing it removes it from view
  immediately but a reload brings the (still-mocked) suggestion back, proving dismissal really is
  session-only and never reaches the confirmed-relationships list.

## Module 3 — Cross-area correlation tool — done

- [x] Unit (`InvestigateWorkspaceIssueToolTests.cs`, new file, 10 tests, against a
  `FakeToolRegistryForWorkspaceInvestigation` that records every delegated call and a
  `FakeConnectionPoolForWorkspaceInvestigation`/`FakeAksClientForWorkspaceInvestigation` pair for the
  AKS pod-discovery step): an unknown area and a hint matching no node both return a clear error, not
  an exception; a Service Bus related node calls `analyze_queue_health` with the queue name correctly
  extracted from a `namespace/queue`-shaped resource key; a Redis related node calls
  `analyze_cache_health` with the cache id; an AKS related node discovers a real matching pod first
  and only then delegates to `investigate_pod_issue` with that exact pod name (verified against the
  actual recorded call arguments, not just "a call happened"); a Storage related node returns an
  honest "no composite tool exists yet" note and calls nothing; an AKS node with no running pod, or
  with AKS not configured at all, each skip gracefully with their own specific note rather than
  throwing; the 2-hop bound is proven with a real 4-node chain (start → hop1 → hop2 → hop3) — exactly
  2 related reports come back and nothing in the output ever mentions the 3rd-hop node, not just "the
  count was right."
- [x] Unit: zero declared relationships returns `related_resources_investigated: 0` with an
  explanatory note and never calls the registry at all — the "topology not configured yet" path,
  degrading gracefully rather than requiring the graph to exist.
- [x] Unit (`SidecarAgentChatServiceFilteringTests.cs`, 7 new): `scope: "feature"` (the default)
  keeps the existing per-area filter behavior unchanged; `scope: "workspace"` makes every configured
  area's tools visible for the turn despite a narrower `context.FeatureArea`; `scope: "workspace"`
  does NOT bypass the `mode` gate (mutate tools stay excluded in "ask" mode regardless of scope);
  3 unrecognized-scope-value cases (`null`/empty/garbage string) all default to `"feature"`, never
  silently widening tool visibility; a `null` context (the global page) sees every area's tools
  whether scope is workspace or feature, since there was never an area filter to bypass.
- [x] E2E (`contextual-assistant.spec.ts`, 1 new): the "search across my whole workspace" checkbox's
  actual outgoing request body carries `scope: "workspace"` only once checked, and no `scope:
  "workspace"` in the request sent before checking it — via network interception, matching this
  file's existing pattern for the mode toggle.

## Module 4 — Proactive insights — done

- [x] Unit (`ProactiveInsightServiceTests.cs`, new file, 6 tests, using the real
  `MonitoringAlertEvaluationService` + its own `RunEvaluationOnceAsync` — the exact fixture pattern
  `MonitoringAlertEvaluationServiceTests.RunEvaluationOnce_FiresEvent_WhenSourceReturnsFiring`
  already established, reused rather than reinvented, plus a real
  `SidecarAgentChatService`/`ContextBudgetModelClient` pair for observing the seeded session): no
  matching workspace node and `ChatOnly` capability both skip the investigation entirely, never
  touching the tool registry; a match invokes `investigate_workspace_issue` with the exact
  area/hint the rule's own params map to, and raises `InsightReady` with the fired event's own
  identity; a successful run seeds a real session, verified via `SidecarAgentChatService.GetHistoryCount`
  on the emitted `sessionId` — not just "an event was raised"; a summarization failure (the model
  client throws) raises no event and seeds no session — fails silent, not garbled; the background
  task never throws in a way that could propagate — every test that triggers a firing awaits past it
  without the surrounding `RunEvaluationOnceAsync` call itself ever failing, which is the practical
  proof that nothing inside the fire-and-forget path escapes uncaught.
- [x] Unit: the **global** rate limit, proven with real concurrency rather than asserted from
  fixture setup alone — two different rules (different `AlertRuleSource`s, both mapped to distinct
  topology nodes) fire in the same `RunEvaluationOnceAsync` pass; the fake tool registry blocks on a
  `TaskCompletionSource` so the first investigation stays "in flight" long enough for the second's
  `Task.Run` to actually attempt and get rejected by the `Interlocked.CompareExchange` flag —
  `registry.Calls` has exactly one entry, not just "eventually settled to a plausible-looking state."
  This module chose drop-not-queue (a real, documented decision the plan explicitly allowed either
  way), so there's no separate "extras are queued" behavior to test.
- [x] E2E (`monitoring.spec.ts`, 2 new tests, mocking `/api/monitoring/stream`'s new `{kind, event}`
  envelope directly): a `proactiveInsightReady` frame renders a card with the rule name and
  generated summary, and clicking Investigate navigates to `/agent` with that same content visible
  in the transcript (proving the client-side injection path, not just that navigation happened);
  dismissing a card removes it immediately, and a reload against the same (still-mocked, naturally
  reconnecting) stream proves the `sessionStorage`-backed de-dup keeps it from reappearing — the
  real regression this de-dup requirement exists to prevent, not just "the flag exists in code."

## Module 5 — Token-aware context budgeting — done

- [x] Unit (`AgentCapabilityTesterTests.cs`, 3 new): a `/v1/models` entry advertising `context_length`
  for the configured model populates `DetectedContextWindowTokens`; the same field on a *different*
  model in the list detects nothing; no such field anywhere detects nothing — none of the three throw.
- [x] Unit (`OpenAiCompatibleAgentClientToolResultCappingTests.cs`, 5 new): `CapToolResult` under/at/
  over the cap (over-cap asserts the exact truncation marker text and character count, not just "got
  shorter"); null/empty input returns empty string rather than throwing; an end-to-end
  `ChatAsync` 2-round tool loop with a deliberately oversized (20,000-char) tool result asserts the
  *actual outgoing request body* of round 2 contains the truncation marker and never contains the
  full oversized blob — proves the cap applies at the point results re-enter the conversation, not
  just in the unit-level helper.
- [x] Unit (`SidecarAgentChatServiceContextBudgetTests.cs`, new file, 5 tests): a short conversation
  against the default (unset) context window never summarizes across 4 turns, and
  `GetContextUsagePercent` is > 0 but well under 100%; a tiny declared context window plus enough
  history (5 prior turns) makes the summarization flag fire on the exact next turn, and — proven via
  the *next* turn's actual outgoing history, not just the boolean — the summary marker is present,
  the earliest messages are gone, and the most recent turn survives verbatim; a summarization model
  call that throws fails open (turn still succeeds, `Summarized` stays false, history keeps growing
  normally rather than being partially corrupted); `GetContextUsagePercent` on a session that was
  never touched returns 0, not an exception.
- [x] The pinned "current focus" context needed no dedicated test — it's structurally impossible for
  it to be lost, since it's rebuilt fresh into the system prompt every turn and was never part of
  `session.History` (the thing summarization operates on) in the first place. Documented in
  technical-plan.md rather than asserted, since there's no meaningful failure mode to write a test
  against.

## Module 6 — Reasoning trace and usage indicator — done

- [x] Unit (`SidecarAgentChatServiceContextBudgetTests.cs`): a tool-calling turn against a model
  client that actually invokes the executor it's given produces exactly a `tool_call` then
  `tool_result` step pair with the right tool name and a truncated result summary — end-to-end
  through the real step-recording wrapper, not asserted against `AgentChatStep` in isolation.
- [x] Unit (`AgentEndpointsTests.cs`, 2 new): `GetStatus`'s JSON includes `contextUsagePercent`; the
  streamed `Done` event's wire-mapped `result` includes `steps`/`contextUsagePercent`/`summarized` —
  guards `AgentEndpoints.ToWireEvent`'s field mapping specifically, since that's exactly the kind of
  spot a new field silently fails to get copied through.
- [x] E2E (`agent.spec.ts`, 3 new): a mocked reply carrying `steps` shows a collapsed
  "Show reasoning (N steps)" toggle that expands to reveal each step's summary; a reply with zero
  steps shows no disclosure element in the DOM at all (not just "collapsed"); a reply with
  `summarized: true` shows the inline notice under that specific message.
- [x] E2E (`settings.spec.ts`, 1 new): the context-window field round-trips through reload, and the
  capability line shows the "unknown, using a 4,096-token conservative default" copy when unset and
  the actual value once set — both directions asserted, matching the file's existing convention for
  binary-state UI text.
- [x] Full regression sweep (`contextual-assistant`, `global-agent-panel`, `dashboard` — 22 tests)
  re-run since `ContextUsageIndicator`/`AgentReasoningTrace` are now rendered by all three chat
  surfaces, not just the global one.

## Module 7 — Local-model adaptive behavior — done

- [x] Unit (`SidecarAgentChatServiceContextBudgetTests.cs`, 1 new): `ResolveSummarizationThreshold(4096)`
      is 0.50, `ResolveSummarizationThreshold(131072)` is 0.75, and a midpoint (32,000) falls strictly
      between — proving small windows summarize at a lower absolute token count than large ones.
- [x] Unit (`AgentEndpointsTests.cs`, 1 new): `GetStatus` now includes `contextUsageWarningPercent`
      alongside `contextUsagePercent`, matching the new `ContextUsageIndicator` contract.
- [x] E2E (`contextual-assistant.spec.ts`, 2 new): a `ChatOnly`-capability profile disables the
      workspace-wide escalation checkbox and shows the "doesn't support tool calling" reason; an
      `Unknown`-capability profile does the same and shows the "Run Test Connection first" nudge.
      The existing Module 3 scope test was updated to mock a `ToolCalling` profile so the checkbox
      remains operable for that test.
- [x] **Honest test-runner note**: a full `npx playwright test e2e/contextual-assistant.spec.ts` run
      hit the pre-existing Windows `.e2e-appdata` worker-restart lock cascade on unrelated tests
      (`mode toggle` `aks-namespace-select` flake, Redis/Storage `EPERM` cleanup). Targeted reruns of
      the relevant scope tests (`--grep "workspace search|search across"`) passed 3/3.

## Module 7 (of the plan overall) — Manual local-model verification

Same honesty standard as `ai-augmented-app test-plan.md`'s Module 7: verify at least one full
correlation query and one proactive-insight round trip against a real running LM Studio instance,
not just demo-mode/mocked data, and record the model and outcome in `status.md`. Context-budget
behavior (Module 5) specifically needs verification against a small real context window — a large
cloud model may never exercise the trimming/summarization path at all, so testing only against one
would leave this genuinely unverified.

## Regression coverage to re-run, not just add to

- Full `MonitoringAlertEvaluationServiceTests` suite — Module 4 adds a subscriber to an existing
  event; the existing cooldown/backoff/ring-buffer tests must keep passing unchanged.
- `ai-augmented-app`'s own regression suite, particularly anything touching
  `SidecarAgentChatService`'s request construction (Module 5 changes it again, on top of that
  feature's own Module 2 session refactor).
