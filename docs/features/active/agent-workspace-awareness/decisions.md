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

## D11 — OS notifications use tauri-plugin-notification, never a dialog

**Chosen:** `show_notification` now goes through `tauri-plugin-notification`
(real Windows action-center toasts via `tauri-winrt-notification`).

- The previous implementation was `dialog().blocking_show()` — a modal
  MessageBox that froze the webview until dismissed and looked nothing like a
  notification (the exact complaint that triggered this work).
- The plugin is fire-and-forget: no action callbacks, no click-to-focus. A
  click-through deep link into the app remains a separate enhancement, same
  as noted in D9.

## D12 — Agent-proposed alert rules are a Monitoring-area mutation applied in the sidecar

**Chosen:** new `propose_create_alert_rule` tool (`FeatureArea.Monitoring`,
Mutate/Low) + `MonitoringActionExecutor` in `src-sidecar` handling
`AgentActionType.CreateAlertRule`.

- The tool only registers a `PendingAgentAction` — the existing
  pending-approvals pipeline is the only path to a real rule, per the
  feature's no-autonomous-mutation rule.
- The executor lives in `src-sidecar` (not `SwebKit.Agents`) because applying
  needs `MonitoringAlertEvaluationService.ReloadRulesAsync` — a sidecar
  service — so a confirmed rule starts evaluating immediately, same as the
  REST upsert endpoint does.
- Tool params are flat (`aks_namespace`, `servicebus_entity_path`, …) rather
  than the model's nested param bags: LLMs emit flat objects far more
  reliably; the executor maps them per source and re-validates.
- `FeatureArea.Monitoring` exists now but nothing filters TO it — rule
  contextual panels map to the subject area (AksPodHealth → Aks) — so the
  tool is reachable from global chat and workspace scope, which is where
  "set an alert on this" conversations happen anyway.
- `ai_investigation_enabled` is part of the proposed payload so an agent can
  propose a firing rule with or without the investigation loop attached.

## D13 — Notification center grows read-state inside the existing history

**Chosen:** extend `NotificationSystem`'s history (which already collected
dismissed toasts) with `read` + `link`, an unread badge, mark-all-read, and
clear-all — rather than building a separate notification store.

- Every `notify()` toast already funnels into history on dismiss, so alert
  and insight notifications get center behavior for free once AppLayout
  passes a `link` ("/monitoring").
- Unread-count badge (not total-count) is the meaningful signal; items mark
  themselves read on click, and clicking a linked item navigates and closes
  the panel.
