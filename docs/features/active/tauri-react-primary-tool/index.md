# Tauri + React as the Primary Tool

## Summary

SwebKit exists today as two parallel implementations: the original .NET MAUI Blazor Hybrid app
(`src/SwebKit.App` + `SwebKit.Core/.Azure/.Kubernetes/.Redis/.Agents`) and the newer Tauri + React
rewrite (`web/` frontend, `src-tauri/` shell, `src-sidecar/` .NET backend reusing the same core
libraries over HTTP). `docs/tauri-react-rewrite-megaplan.md` planned this as a full replacement;
in practice both stacks have coexisted on `main` past the point the megaplan expected a clean
cutover, and 11 active feature docs have accumulated fixes and polish for the new stack without a
single place tracking whether it's actually ready to be the tool people reach for every day.

This feature makes that decision explicit and operational: **Tauri + React becomes the primary
tool.** MAUI is deprioritized (no new feature work), kept only until the gaps below are closed,
then archived. This doc and its siblings are the single source of truth for what "ready" means and
what's left to get there.

**Jira:** not linked

## Goal

A developer can use the Tauri + React app as their only SwebKit for daily work — Service Bus, AKS,
Redis, Storage, the API client, and the AI agent — with no functional regression versus MAUI (or a
deliberate, documented substitution), no known data-corruption or duplicate-action bugs, and enough
operational visibility (logs, crash reports, signed/updatable installer) that a real user's problem
in the field is diagnosable and fixable rather than invisible.

## Scope

### In scope

- Closing every **feature-parity gap** between MAUI and Tauri+React that isn't an intentional
  substitution (catalogued in [production-readiness-review.md](production-readiness-review.md)).
- Fixing every **correctness/functional bug** found during this review (the React key-collision
  duplicate-render bug is the most serious — see the review's top findings).
- A **UX/UI pass**: [ux-plan.md](ux-plan.md) covers accessibility, visual consistency, and the
  backlog of already-tracked-but-unshipped work across the 11 existing active features.
- A **code-quality/architecture/performance pass**: [technical-plan.md](technical-plan.md) covers
  the monolithic page components, sidecar architecture issues, security hardening, and docs hygiene
  (stale/contradictory architecture docs, dangling references to deleted feature folders).
- A **test coverage pass**: [test-plan.md](test-plan.md) covers the sidecar's near-total lack of
  endpoint tests, CI gaps (Vitest and Rust checks don't run in CI today), and accessibility testing.
- **Production/packaging readiness**: crash telemetry, sidecar file logging, sidecar crash recovery,
  code signing, auto-update, and a real app identifier — all currently absent (see review §6).
- ~~Committing the work already sitting uncommitted on `feat/api-client-ux-and-git`
  (`api-client-git-completion`, `api-client-ux-overhaul`) and the finished-but-uncommitted
  `monitoring-rebuild`~~ — **dropped (2026-08-01)**: `feat/api-client-ux-and-git` doesn't exist
  anywhere reachable in this repo, confirmed by the user as gone, not merely unlanded. See
  [ux-plan.md](ux-plan.md)'s Phase 1 for the full note.

### Out of scope (explicitly, do not re-litigate)

- **Observability** (Application Insights logs/traces/metrics) and **DevOps/Pipelines/Releases** —
  permanently dropped per the 2026-07-26 product decision recorded in `docs/features/README.md`
  (the original decision doc, `demo-mode-parity/index.md`, no longer exists in the repo — see the
  review's docs-hygiene section for the dangling-reference cleanup this implies).
- **Incident Timeline** — never built for the new stack, not requested.
- Full MAUI feature-for-feature parity on capabilities that were deliberately redesigned rather than
  ported (e.g. Dashboard's tile-builder replaced by lightweight pinning; this is a good substitution,
  not a gap).
- Rewriting `SwebKit.Core`/`.Azure`/`.Kubernetes`/`.Redis`/`.Agents` — these are reused unchanged by
  the sidecar per the megaplan and are out of scope except where a specific bug in them is a live
  risk for the sidecar (flagged individually in the technical plan where relevant).

## Current state (see production-readiness-review.md for the full picture)

- All 8 initial-release features (Dashboard, Settings, Service Bus, AKS, API Client, Redis, Storage,
  Agent) are functionally built and demo-able. This is further along than `docs/tauri-react-rewrite-megaplan.md`
  or `docs/plans/test-coverage-expansion.md` describe — both are stale on this point.
- Test coverage is real but uneven: ~191 Playwright e2e tests, 116 Vitest unit tests (pure-logic
  only, by design), and a sidecar xUnit project with only 3 files covering ~9 endpoint groups.
- Several previously-flagged critical bugs (plaintext auth-secret storage, dead Storage
  upload/copy/metadata routes, broken Gateway API grids) are **already fixed** — the tracking docs
  that flagged them are stale and should not be re-worked from.
- A hands-on pass through the running app (demo mode) found several **new, unfixed, and previously
  undocumented bugs**, the most serious being a React list-key collision that causes duplicate
  renders of error messages, alert rows, and action-confirmation toasts — and may indicate the
  underlying mutation is fired twice, not just rendered twice.
- No crash telemetry, no persisted sidecar logs in production, no sidecar crash recovery, no code
  signing, no auto-updater, and CI doesn't run the Vitest suite or any Rust checks.

## Related docs

- [production-readiness-review.md](production-readiness-review.md) — the full findings, organized
  by domain, with severity
- [ux-plan.md](ux-plan.md) — phased UX/UI/functionality plan
- [technical-plan.md](technical-plan.md) — phased clean-code/architecture/performance/security plan
- [test-plan.md](test-plan.md) — phased test-coverage and CI plan
- [status.md](status.md) — progress tracking
- Existing active features this plan depends on and consolidates:
  `aks-internal-refactor`, `aks-ux-improvements`, `api-client-key-vault`,
  `post-migration-ux-review`, `react-polish-aug-01`, `service-bus-download-parity`,
  `storage-detail-overflow`, `ux-followup-july-27`
  (`api-client-git-completion`, `api-client-ux-overhaul`, and `monitoring-rebuild` are excluded here —
  their implementing branch no longer exists; see the dropped-scope note above)
- `docs/tauri-react-rewrite-megaplan.md`, `docs/MIGRATION-NOTES.md`,
  `docs/architecture/functionalities/api-client.md`
