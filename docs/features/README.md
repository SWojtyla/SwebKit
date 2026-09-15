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

**Data-fetch performance (2026-09-14):** `docs/features/active/data-fetch-performance/` is in
Review — AKS, Service Bus and Redis all felt slow to fetch, and Redis filtering in particular
"sometimes takes a lot of time and I don't know if it crashed." The root cause was a connection
leak, not a slow query: Redis and Service Bus built an SDK client per request and disposed none of
them, so a browsing session leaked connections until commands started blocking for the full timeout
instead of failing. Both now pool through the existing `ClientCache`, as Storage already did
(`cc700f33`). On top of that: Redis stopped firing two 500-key metadata sweeps while merely
browsing keys and now scans server-side under a budget; AKS stopped shipping Helm release manifests
and ConfigMap values it never renders, and stopped blocking first paint on the namespace list;
Service Bus reads message counts in pages of 100 instead of one call per entity. Two correctness
bugs fell out of the tracing — subscription entity paths were unencoded and 404'd every peek/purge,
and Redis "Load all" stopped after one page. Cluster-scoped AKS list calls are the largest
remaining win and are recorded as a follow-up.

**API client auth (2026-09-14):** `docs/features/active/api-client-auth-variables/` is in Review —
auth was the one part of a request the variable scope never reached, so a bearer token entered as
`{{AUTH_PI2_KEY}}` was sent as those sixteen characters and came back a 400, and an auth secret was
write-only once set, which is why nobody could see that a variable was involved. Every auth field
now substitutes at send time, and secrets have a reveal toggle that shows the variable-aware input.
Direct follow-on from `api-client-variable-scoping/`, which closed the same blind spot for the body.

**ACP external agents (2026-09-15):** `docs/features/active/acp-external-agents/` is Planned —
a design-only commit so far. Adds the Agent Client Protocol as a fourth agent provider kind so
external agents (Claude via `claude-agent-acp`, Gemini CLI, Codex, Mistral Vibe, …) can drive the
existing assistant: the sidecar spawns the agent over stdio JSON-RPC, maps `session/update` onto
the existing SSE stream, and hands the agent SwebKit's own tools through an MCP bridge in
`session/new` — preserving demo mode, per-area tool scoping, and the propose→confirm mutation
flow. fs/terminal client capabilities stay off; agent permission requests auto-approve behind a
per-profile toggle.

**Agent correlation (2026-09-15):** `docs/features/active/agent-correlation/` is Planned —
makes the cross-service correlation machinery actually reachable: ACP profiles get the
workspace-scope escape hatch the UI currently locks behind a stale capability gate, the agent
learns (via prompt + MCP error hints) that fenced areas exist and how the user unlocks them, a
rejected out-of-scope call surfaces a one-click "retry with workspace scope", agent reasoning
(`thought` events) gets rendered, `investigate_workspace_issue` gains a real Storage health
tool instead of skipping it, and relationship suggestions learn to read pod logs, not just env
vars and ConfigMaps.

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
