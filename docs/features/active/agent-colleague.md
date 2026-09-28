# Agent — Colleague, not Chatbot

State: Done

## Goal

Seven capabilities that turn the agent's output into navigable, actionable objects:
deep-linked evidence, structured access gaps, one-click alert-rule drafts, addressable
screen state, feedback capture, "what changed" timelines, and cross-environment compare.

## Scope

### 1. Structured evidence + bookmarks (build first — items 2–3 share the model change)
- `ProactiveInsightReport` gains `EvidenceItems: List<EvidenceItem>` — `{ Text, Tool,
  CapturedAt, View? }`. Keep string `Evidence` for back-compat; coerce old reports.
- `EvidenceView { Kind, Params }` — backend emits `(kind, params)` only, never URLs;
  `web/src/lib/evidence-links.ts` whitelists kinds → existing search-param conventions
  (`/service-bus?ns=&entity=&view=&msg=&seq=`, `/sql?connection=&table=`, `/aks?...`,
  `/monitoring?tab=reports&report=`). Invalid model output degrades to plain text.
- `EvidenceViewValidator` (Core) + permissive parser accepting plain strings.
- Redis needs a `?cache=` search param (cacheId is `location.state`-only today).

### 2. Access gaps → actionable UI
- `ProactiveInsightReport.AccessGaps: List<AccessGap>` (mirrors `AccessDenial` +
  `Resource` best-effort from tool args).
- Orchestrator collects `{"status":"access_denied"}` results → `AgentChatResult.
  AccessDenials` → report (chat-level gaps become available too).
- `AccessGapsCard` on the report + badge count on insight cards + "Copy access request"
  (role + resource text — **no invented scopes/principals**; per access-denial doc those
  are phase-3 non-goals until the artifact work lands).
- Sanitize `Detail` (SDK messages can echo URIs).

### 3. "Watch this" → alert-rule draft
- **Path A (chosen)**: `EvidenceItem.Watch` hint (`{source, params}` for the supported
  `MonitoringAlertRule` sources only) → "Watch this" button opens `AlertRuleDialog`
  prefilled → normal save path. Zero backend approval plumbing; richer editor.
- Path B (later): pending-action proposal without a model round-trip.
- Non-goal: arbitrary KQL/App Insights rules (no generic query field exists — flag).

### 4. Entity-indexed screen state (`get_screen_detail`)
- Snapshot payload gains `entities: {"<area>.<kind>.<id>": bounded-detail}` — per-entity
  ~1–2 KB, ≤20 entities, total ≤8 KB enforced in publish path.
- `ScreenStateStore.GetEntity(id)`; new read tool `get_screen_detail`; overview lists
  entity ids so the model discovers names. Frozen id convention: `<area>.<kind>.<id>`,
  last-write-wins, namespace-separated.
- Whitelist discipline extends to entities (no bodies/tokens/secrets) + a SQL provider
  (none today).

### 5. Thumbs-down → regression cases
- `exchangeId` on terminal `Done` stream event; server-side ring buffer keyed by it
  retains the exchange (message, assistant text, step summaries, tools, screen state).
- Thumbs-down button on assistant messages → `POST /api/agent/feedback` tags+flushes to
  `AgentFeedbackRepository` (`agent-feedback.json`, cap ~200); Settings list + JSON
  export for prompt tuning. Redaction: truncate 4 KB, denylist keys, never persist
  pending-action payloads.

### 6. "What changed since 14:32?" timeline
- New read tool `get_change_timeline {since_iso, area?}` merging: alert ring buffer,
  k8s events, pod restarts, `DeploymentInfo.LastUpdateTime` (new field), Helm revision
  timestamps (already readable via `owner=helm` secrets).
- Requires `coverage` section listing unchecked/denied sources (reuses `AccessGap`);
  prompt instructs "correlation, not causality" + confidence labels.
- Phase B later: bucketed `get_metrics` for before/after deltas. App Insights deploy
  markers = speculative (per-workspace marker query).

### 7. Cross-environment compare (biggest design risk)
- **No environment model exists** — `LegacyProfileData.Environments` collapsed to one
  `AppConfig`; "stg/prd" is naming convention today. Do NOT resurrect environments.
- Chosen model: `WorkspaceResourceNode.LogicalName` ("orders-api") — nodes sharing it
  across maps are the same logical service; map name acts as the env tag. Settings →
  Map gets a `LogicalName` field (datalist suggest, never auto-add — repo convention).
- `compare_environments {logical_name, env_a, env_b}` tool → per-area comparators:
  AKS deployment (image/replicas via `KubeconfigContext` on nodes), SB queue
  settings+stats, SQL schema (`SqlSchemaComparer` endpoint already exists — wrap),
  Redis INFO. Missing/denied sides are first-class `status` outcomes.
- Demo: `orders-dev-sql`/`orders-prod-sql` pair + demo storage env tags already
  simulate two environments.

## Implementation order

`1 → 2 → 3` one vertical slice (shared report model) → `4` → `5` → `6` → `7`.

## Test plan

- xUnit: view validator, evidence coercion, denial collection, dedupe, timeline merge/
  coverage, logical-name lookup, feedback capture/redaction.
- vitest: evidence-link resolution (param whitelist + encoding), dedupe merge.
- e2e (demo): report evidence click-through to DLQ view; watch-this → dialog; compare
  demo envs; `get_screen_detail` from contextual assistant.

## Decisions / open questions

- Watch-this = dialog prefill (A), not pending-action (B).
- Cross-env requires `LogicalName` field — needs product sign-off before building.
- Feedback retention = Settings list + JSON export, no triage workflow.
