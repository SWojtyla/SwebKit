---
status: Review
---

# App-Wide UX & Interaction Consistency

## Scope

Reported directly by the user against two features:

1. **AKS:** clicking a row in a resource table does nothing — you have to right-click to get a
   context menu with actions. No visible affordance, no left-click primary action.
2. **Redis:** panels/trees are not collapsed by default, so the view is cluttered on open.

The user asked for an in-depth, feature-by-feature pass across the whole app — "go really in
depth... make a big plan with proposals for each feature" — with AKS weighted highest since it's
the most-used feature. This is not a request to fix the two examples; it's a request to audit
every feature area (AKS, Redis, Service Bus, API Client, Monitoring, Storage, Agent, Settings,
Dashboard/shared layout) for the same class of problem: illogical interaction models, inconsistent
feedback, and information the user shouldn't have to hunt for.

A direct code audit (2026-09-12) of all nine feature areas confirms both reported problems have a
root cause in shared components (`ResourceTable.tsx`'s clickable-affordance logic; Redis's
namespace-tree auto-expand effect) rather than being isolated to one table or panel, and surfaces
the same *class* of problem — inconsistent click affordances, inconsistent destructive-action
confirmation, inconsistent loading/error/empty states, inconsistent feedback on
success/failure — repeated independently across all nine areas. Full findings are in
`technical-plan.md`.

## Relationship to in-flight work

Four features are already active and near done — `aks-log-parity` (log streaming/toolbar parity,
Review), `redis-entra-auth` (Entra auth + field captions, Review), `api-client-variable-scoping`
(variable highlighting/cURL/global env, Review), `settings-save-performance` (commit-on-blur
saves, Review). This plan does not overlap them: it was scoped by auditing the *current* code
(post those changes, where already merged) and explicitly excludes anything they already cover
(e.g. Redis's Namespace Separator/Database/Active captions, or Settings' per-keystroke save
problem). Where a finding here touches the same file as one of those features, treat that feature
as landing first.

## Outcomes

- **AKS** (highest priority — most-used feature): every resource tab has a real, discoverable
  left-click primary action with correct hover/focus affordances; list errors are visibly distinct
  from "no items"; destructive/mutating actions are confirmed consistently; switching cluster
  context can no longer leave a pod shell silently talking to the wrong cluster; Secrets/ConfigMaps
  behave the same way; tables are sortable/filterable with unhealthy resources surfaced first.
- **Redis:** the namespace tree's expand/collapse state is a deliberate, owned piece of state (not
  an incidental side effect that silently undoes "Collapse All"), with a matching "Expand all," a
  real empty/loading distinction, and drill-through links between tabs.
- **Service Bus:** fetch failures are visibly distinct from "entity is empty"; every mutating action
  (Complete/Resubmit/Purge/Replay) gives success/failure feedback; one consistent confirmation
  pattern for destructive actions app-wide; a working Refresh command.
- **API Client:** closing the active tab focuses a neighbor instead of going blank; "Collapse all"
  collapses collections; search reaches into collapsed folders; confirm dialogs say what they
  actually do (Close/Discard vs. Delete).
- **Monitoring:** alert rows are clickable; delete/disable get confirmation; loading/error states
  are real; a rule can't be saved half-configured.
- **Storage:** the Download button no longer silently corrupts binary blobs; the breadcrumb no
  longer renders blank/mislabeled segments; overwrite/undelete actions are confirmed.
- **Agent:** approve/reject failures are never silent; a long multi-tool turn shows what's actually
  running and can be stopped; high-risk proposed actions look and behave differently from safe
  ones.
- **Settings:** Service Bus connection strings can actually be entered; every section can test its
  connection the way Agent already can; a rejected save always says why, instead of silently
  snapping back (the general form of the historical `SbAuthMode` bug).
- **Dashboard & shared layout:** one confirmation component, one notify-on-mutation pattern, one
  "last updated" indicator, and one consistent connectivity signal are used everywhere instead of
  each feature re-solving (or skipping) the same problem independently.

## Non-goals

- No visual redesign/rebrand — this is about interaction correctness and consistency, not a new
  look.
- No new features — every unit below fixes or standardizes something that already exists.
- Observability and DevOps/Pipelines remain out of scope per the existing product decision
  (`docs/features/README.md`, `docs/features/archive/demo-mode-parity/index.md`) — Monitoring here
  means the App Insights alert-rule surface only.
- Backend/domain changes are limited to what's needed to surface an error that already exists
  (e.g. threading `isError`/`error` through a hook that already has it) — no new backend
  capabilities are introduced by this plan.
- Per-unit Aikido scanning (`docs/security/aikido-mcp-scan.md`) is a worker responsibility, not a
  separate outcome of this plan.

## Traceability

- Technical plan (research summary, work units, sequencing, worker template): `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Source audit: direct code review of `web/src/components/{aks,redis,service-bus,api-client,
  monitoring,storage,agent,settings,dashboard,layout,shared,ui}/**` and `web/src/lib/hooks/**`,
  conducted 2026-09-12 for this plan.
- Related in-flight features (see "Relationship to in-flight work" above): `../aks-log-parity/`,
  `../redis-entra-auth/`, `../api-client-variable-scoping/`, `../settings-save-performance/`.
- Pitfalls consulted: `docs/pitfalls/react-frontend.md` (TanStack Query key matching, SSE cleanup,
  Tauri boundary, Playwright traps — relevant to several units below).
