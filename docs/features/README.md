# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

The repo is organized into two folders:

- `docs/features/active/` — features currently being worked on or awaiting a final pass; each has
  its own `index.md`/`technical-plan.md`/`test-plan.md` per the Folder Contract below.
- `docs/features/archive/` — completed or superseded features, kept for history/traceability.

**Start here for current priorities:** `docs/features/active/workspace-intelligence/` is the current
active feature (created 2026-08-02, on the same branch as `ai-augmented-app`) — cross-system
correlation (a user-curated workspace topology, heuristic relationship suggestions, a cross-area
investigation tool, proactive insights from Monitoring alerts) plus context management for long
conversations (token-aware budgeting, a reasoning trace, and a usage indicator). As of 2026-08-03,
Modules 1, 2, 3, 4, 5, and 6 are all done and verified — see that feature's `status.md` for exact
detail per module. **Only Module 7 (local-model adaptive behavior) remains**, plus `ai-augmented-app`
Module 7 (manual LM Studio verification), which is explicitly the user's own task, not something to
implement. `ai-augmented-app/` itself (2026-08-02) made AI assistance a first-class, contextual
capability in every feature area (Ask/Ask & do, working against both cloud and local/LM Studio
models) and is fully done — `workspace-intelligence` is its follow-on. Both supersede
`tauri-react-primary-tool/`, which shipped (merged to `main` via PR #75) and has been removed from
`active/`.

**API client (2026-09-09):** `docs/features/active/api-client-variable-scoping/` is in Review —
`{{variable}}` highlighting in the request body editor (the URL field already had it, the body
did not, so an undefined variable was sent literally and came back a 400 with no warning), a cURL
panel that actually reproduces the request, and a global environment layer that applies underneath
the collection-scoped one so shared values are defined once. It builds on
`api-client-ux-improvements/` (Review, shipped as PR #82) and deliberately does not re-plan the
Environment Manager resizing that shipped there.

**AKS logs (2026-09-09):** `docs/features/active/aks-log-parity/` is in Review — the multi-pod log
view now streams every pod on open and shares one toolbar, buffer and windowing model with the
single-pod view, instead of having almost none of its controls. Log lines carry the container's own
timestamp (the `timestamps` option was never passed to Kubernetes) and multi-pod output is ordered by
it rather than by arrival. Also normalises the two AKS clients, which disagreed about line shape, and
fixes a text filter that matched the timestamp prefix.

**Redis (2026-09-10):** `docs/features/active/redis-entra-auth/` is in Review — the Redis
settings panel gains an Entra ID (AAD) auth mode alongside connection strings (same shared
credential Storage/Service Bus already use — just the Azure Cache for Redis resource name, no new
login flow), plus explanatory helper text for Namespace Separator, Database and Active, which had
none.

**Settings (2026-09-09):** `docs/features/active/settings-save-performance/` is in Review — every
settings field used to save the whole profile on every keystroke (a disk rewrite and a refetch per
character), saves were not serialized so concurrent edits raced, and the Service Bus Entra ID option
sent a value that is not a member of the C# `SbAuthMode` enum, so the save was rejected and the radio
reverted. All seven profile-writing settings pages now commit on blur through `DraftInput`.

**Note (2026-07-26, partially superseded 2026-08-03):** Observability and DevOps/Pipelines were
dropped from the Tauri + React rewrite by product decision — not deferred, not planned for a later
pass. See `docs/features/archive/demo-mode-parity/index.md` for the original context (recreated
stub — see that doc's own note on why). **DevOps/Pipelines remains fully out of scope.**
Observability was partially reversed on 2026-08-03 (`ai-augmented-app` Module 13,
`workspace-intelligence/index.md`'s "Decision resolved" section): there is still no dedicated
Observability page/menu, but the agent now has direct tool access to Application Insights
(`get_metrics`/`query_logs`, exempt from the per-feature-area tool filter), with a minimal
resource-id/name Settings widget — a genuine middle ground the user chose, not a full reversal.

**UX consistency (2026-09-12/13):** `docs/features/active/ux-interaction-consistency/` is in
Review — a full audit and implementation pass across all nine feature areas (AKS, Redis, Service
Bus, API Client, Monitoring, Storage, Agent, Settings, Dashboard), triggered by two reports (AKS
rows need a right-click to do anything; Redis isn't collapsed by default) and scoped much wider per
the request to go in depth on every feature, AKS weighted highest as the most-used one. All 58 work
units across 10 batches are implemented and merged to the (unpushed, local-only) branch
`ux-interaction-consistency`: missing click affordances, inconsistent destructive-action
confirmation, silent mutation failures, misleading loading/empty states, and several outright
correctness bugs fixed along the way (Storage's Download was silently corrupting binary blobs; a
breadcrumb rendered blank/mislabeled segments; AKS could leave a pod shell connected to the wrong
cluster after a context switch; a Tauri struct's return values weren't actually camelCased, so two
port-forward session fields silently read `undefined`). Full automated verification (`tsc`, `dotnet
test`, `cargo test`, lint, unit tests, and the full Playwright e2e suite) passes; the Aikido scan
and a short list of manual/live-infra checks are still the user's own, per `status.md`.

## Folder Contract

Each feature folder contains:

- `index.md` - scope, outcomes, dependencies, and source traceability
- `technical-plan.md` - detailed technical plan with step-by-step tasks
- `test-plan.md` - feature-level test scope, levels, scenarios, and traceability

## Traceability Contract

- All links must resolve inside `docs/features/` or active supporting docs
  (`docs/architecture/architecture.md`, `docs/architecture/design.md`).
- Feature docs should not depend on phase-era or global plan-era files.
- Cross-feature dependencies should reference feature folder paths directly.
