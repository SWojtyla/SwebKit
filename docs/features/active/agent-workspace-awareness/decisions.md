# Decisions — Agent Workspace Awareness

## D1 — Screen state is pulled via a tool, not pushed into the prompt

**Chosen:** new `get_screen_state` tool; the model calls it when the question
references what's visible.

- Confirmed by user. "Always as context" works for the workspace map (~bounded,
  rarely changes per turn) but screen state is large, changes constantly, and is
  irrelevant on most turns — stuffing it would burn budget on every chat turn.
- Contrast with `archive/workspace-map-overhaul/decisions.md` D3: same "the
  model must know to call it" discovery concern applies, but mitigated
  differently — the tool description explicitly says "call this when the user
  refers to what they can see," and `## Current focus` in the prompt already
  tells the model the user is looking at a specific area/selection, which cues
  the call.
- Push-to-prompt was rejected also because ACP agents would receive screen data
  (potentially sensitive) on every turn with no way to opt out per-request.

## D2 — Snapshot transport: frontend publishes, sidecar stores latest-wins

**Chosen:** `POST /api/agent/screen-state` on change (debounced) into a
per-session `ScreenStateStore`; the tool reads the store.

- The tool executes in the sidecar but screen state lives in the webview — the
  sidecar cannot pull from React. Publish-on-change keeps the store warm so the
  tool call is a local read.
- Rejected alternatives: piggyback the snapshot on each chat request (stale by
  the time tools run; doesn't help ACP agents which bypass `/api/agent/chat`
  context), query-cache serialization wholesale (too big, uncurated, leaks).
- TTL (~5 min) prevents a navigated-away or closed panel's snapshot being served
  as current.

## D3 — One umbrella feature, not two

**Chosen:** `agent-workspace-awareness` with screen-state and
investigation-depth modules.

- User decision. The modules share the "what does the agent know" theme and the
  same tool/orchestrator plumbing; sequencing is Module 1 → Module 2 (the seeded
  proactive session can later include "what the user was viewing when the alert
  fired" — an integration point, not a hard dependency).

## D4 — `get_screen_state` is exempt from the per-area tool fence

**Chosen:** `FeatureArea.Workspace` + added to the orchestrator's exemption
(alongside Observability tools in `AgentToolCallOrchestrator`).

- A feature-scoped contextual panel (e.g. AKS) that can't call the tool would be
  a dead feature. Screen state is UI state, not area data — fencing it by area
  makes no sense.
- In workspace scope it behaves like any other available tool.

## D5 — Serializers whitelist fields; secrets are structurally excluded

**Chosen:** every serializer enumerates the fields it emits; there is no generic
"serialize the query cache" path.

- Snapshots go to external LLM providers, including ACP agents whose transcripts
  SwebKit never sees. A generic serializer would inevitably leak an auth header
  or connection string.
- Whitelisting also produces better prompts: curated field names beat raw DTO
  dumps.
- Enforced by: per-serializer field lists in code review + a unit test asserting
  no snapshot key matches /token|secret|password|authorization|connectionString/i.

## D6 — Investigation depth = headless bounded agent loop, not a bigger tool

**Chosen:** `ProactiveInvestigationRunner` drives `IAgentModelClient` +
workspace-scope ask-mode tools (round cap + wall-clock budget); the existing
`investigate_workspace_issue` stays as one available tool.

- The model choosing evidence beats hardcoding one topology walk: some alerts
  need pod logs, some need KQL, some need queue stats — a fixed tool can't know.
- Ask mode makes `propose_*` mutations structurally unreachable in an
  unsupervised run — non-negotiable for a background pipeline.
- Rejected: extending `InvestigateWorkspaceIssueTool` internally (it would still
  be one deterministic walk, and its report is already available to the loop).

## D7 — Guardrails stay exactly as they are

Single-flight `_busy`, capability ≥ ToolCalling, rule-must-exist,
topology-node-must-exist, fire-and-forget off `AlertFired`, fail-silent logging —
all kept. Coverage beyond mapped nodes and the insight quality loop were
explicitly de-scoped by the user.

## D8 — `AiInvestigationEnabled` defaults to `true`

**Chosen:** the new per-rule flag defaults on for new and existing rules.

- Today's behavior is "every qualifying alert auto-investigates" — default-off
  would silently turn that off for rules the user already has, and the user
  would have to discover why insights stopped.
- The flag exists for *discoverability and control*, not to gate adoption: the
  checkbox + row indicator answer "how do I activate the AI on this alert" —
  which today has no answer at all.
- Users who don't want LLM calls on a noisy rule now have an explicit off
  switch where there previously was none.

## D9 — Insight-ready gets an OS toast; the alert toast stays as-is

**Chosen:** `proactiveInsightReady` → `showNotification` — **revised at
implementation (D10):** the toast site is `AppLayout`, not `MonitoringPage`.

- The alert-fire toast already exists; what's missing is the
  "investigation finished, report ready" signal — the whole point of running
  investigations while the app is minimized.
- Two toasts per incident (fired → ready) is acceptable: they're seconds-to-
  minutes apart and tell different stories ("something broke" vs "here's why").
- Click-to-focus on the toast is out of scope — `native.rs show_notification`
  uses a dialog fallback without action callbacks; deepening that is a
  separate change.

## D10 — Monitoring notifications live in the always-mounted layout, not a page

**Chosen:** one `useMonitoringStream` subscription in `AppLayout` is the single
site for OS toasts (`alertFired` and `proactiveInsightReady`) and the matching
in-app `notify` toasts.

- Each `useMonitoringStream` call opens its own `EventSource` — page-level
  subscriptions in `MonitoringPage` and `DashboardPage` already exist for their
  in-app feeds. Toasting in any of them would (a) only work while that page is
  mounted — the opposite of "notify me while minimized", and (b) double-toast
  whenever two subscribers see the same event.
- Making the layout the only notification site solves both at once: coverage
  becomes page-independent (the layout mounts once for the app's lifetime and
  stays mounted when the window is minimized) and duplication becomes
  structurally impossible — pages feed their feeds and deliberately don't
  toast.
- The seeded-session deep link stays where it is (`MonitoringPage`'s
  Investigate button → `/agent`): the toast announces, the card is where you
  act.
