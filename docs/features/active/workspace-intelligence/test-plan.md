# Workspace Intelligence — Test Plan

Scope follows `technical-plan.md`'s modules. Same conventions as `ai-augmented-app/test-plan.md`:
sidecar endpoint handlers extracted as named `internal static` methods for direct xUnit testing,
Playwright specs against demo mode for frontend flows, no real-time-based tests for anything that
can instead take an injected/fake clock.

## Module 1 — Topology data model

- Unit: node/relationship CRUD endpoints — add, list, delete; a relationship referencing a
  since-deleted node is handled explicitly (either cascade-delete or a clear validation error, not a
  silent dangling reference — decide which during implementation and test that behavior explicitly).
- Unit: auto-populated node list reflects current profile config (add a Redis cache to the profile,
  it appears as an available node without any manual entry).
- E2E: Map view — add a relationship between two existing nodes, see it in the table, remove it.

## Module 2 — Heuristic suggestions

- Unit: the matching heuristic against crafted fixture data (pod env vars containing a known Service
  Bus host/Redis hostname/Storage account name) — asserts a suggestion is produced with the right
  from/to pair; also assert a non-matching case produces no suggestion (avoid false-positive-prone
  matching going untested).
- E2E: a suggested relationship appears as "Suggested," confirming moves it into the regular table,
  dismissing removes it from the suggestions list without adding it to confirmed relationships.

## Module 3 — Cross-area correlation tool

- Unit: `InvestigateWorkspaceIssueTool` against fake per-area clients — given a starting node with
  two declared relationships, asserts both related areas' investigation tools were called and their
  results merged; asserts the hop-depth bound is respected (a relationship three hops away is not
  included when the bound is two).
- Unit: the tool still functions correctly with zero declared relationships (returns just the
  starting resource's own investigation, not an error) — this is the "topology not configured yet"
  path and must degrade gracefully, not require the graph to exist.
- E2E: the "search across my workspace" toggle changes the `tools` sent in a request (assert via
  network interception, matching `ai-augmented-app test-plan.md`'s pattern for its mode toggle).

## Module 4 — Proactive insights

- Unit: subscribing to `AlertFired` and triggering an investigation — use the existing
  `MonitoringAlertEvaluationServiceTests` fixture patterns (`RunEvaluationOnce_FiresEvent_WhenSourceReturnsFiring`
  already exists as a reference) rather than a real timer-driven background service in the test.
- Unit: the **global** rate limit — firing several different rules in quick succession results in at
  most one in-flight investigation, with the rest queued or dropped (per whichever behavior is
  chosen during implementation) rather than all running concurrently; this is the one piece of new
  dedup logic this module actually adds (per-rule cooldown is already handled upstream).
- Unit: the background investigation never throws in a way that could crash or block the alert
  evaluation loop itself — wrap and log, matching the existing `EvaluateRuleAsync`'s own
  defensive-catch pattern around signal-source errors.
- E2E: a fired alert (demo mode) produces a dismissible insight card; dismissing it removes it and
  it does not reappear for the same firing event on reload within the same session.

## Module 5 — Token-aware context budgeting

- Unit: the token-estimation function against known-length inputs (sanity-check the heuristic is in
  the right ballpark, not exact-token-accurate — document the acceptable margin rather than asserting
  precision the heuristic doesn't claim).
- Unit: tool-result capping — a result exceeding the configured cap is truncated with the expected
  marker text, a result under the cap is untouched.
- Unit: summarization triggers exactly when estimated usage crosses the threshold, not before —
  test at threshold-minus-one-message and threshold-plus-one-message as the boundary case.
- Unit: the pinned "current focus" context and the most recent N turns survive a summarization pass
  verbatim; only the older middle section is replaced by the summary turn.

## Module 6 — Reasoning trace and usage indicator

- Unit: `SidecarAgentReply.Steps` is populated with one entry per tool call in a request that uses
  tools, empty when none are used — assert shape, not exact wording (tool descriptions may change).
- E2E: "Show reasoning" disclosure is collapsed by default, expands to show the trace; the
  usage-indicator percentage updates after a turn that used tools with a sizeable result (demo/mocked
  data is fine — this is a rendering/wiring check, not a real-model check).
- E2E: the inline summarization notice appears in the conversation transcript at the point
  summarization actually happened (can be forced in a test by seeding a conversation already near
  the threshold, rather than sending 50 real messages in the test itself).

## Module 7 — Local-model adaptive behavior

- Unit: summarization threshold scales with a profile's `ContextWindowTokens` — a small-window
  profile summarizes at a lower absolute token count than a large-window one, verified directly
  against the threshold-calculation function.
- E2E: a `ChatOnly`-capability profile hides/disables the workspace-wide escalation toggle with a
  visible reason, matching the existing Ask & do guardrail pattern.

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
