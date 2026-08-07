# Release Train Cockpit — Technical Plan

See `index.md` for scope, non-goals, and product-decision context. This document tracks the module-by-module implementation. Modules are ordered by dependency.

## Phase 0 — Feature contract and real ADO validation

- [x] Create `docs/features/active/release-train-cockpit/` with `index.md`, `technical-plan.md`, `test-plan.md`, and `ux-plan.md`.
- [x] Record the product-decision reversal: DevOps remains excluded as a generic pipeline browser, but a focused Release Trains capability is now in scope for Tauri + React.
- [ ] Execute merge-strategy/run-correlation validation against representative existing ADO data. **This is a hard implementation gate.**
  1. Inspect the ADO repository allowed/default completion strategy for `development` → `main`.
  2. Complete a controlled test PR or inspect a recent completed release PR and compare PR source commit, merge commit, `main` commit after completion, existing release tag target, and pipeline run `version`/commit metadata.
  3. Confirm whether the real strategy is fast-forward, merge-commit, squash, or variable.
  4. Configure each component’s expected merge strategy. For fast-forward, require the merged/main commit to equal the tagged source commit. For merge-commit, require the tag commit to be the recorded PR source commit and an ancestor represented by the completed PR. Flag squash/rebase as incompatible with a pre-merge immutable tag unless the release rule changes.
  5. If run metadata cannot be correlated reliably through the Pipelines Runs API, add the minimal Build API lookup needed to match `sourceVersion` and repository ID. Never fall back silently to the latest main run; expose a manual “attach run” recovery action with candidate evidence.
- [ ] Capture ADO response fixtures with secrets/identities removed for PRs, completed merges, pipeline run resource versions, timelines, and approvals; lock mapping tests to those fixtures.
- [ ] Finalize compatibility rules for fast-forward vs. merge-commit before implementing tag progression.

## Phase 1 — Core models and durable local state

- [x] Extend `src/SwebKit.Core/Domain/DevOpsConfig.cs` with authentication mode and reusable release-group/component binding models.
- [x] Add release-train aggregate/state/event models under `src/SwebKit.Core/Models/ReleaseTrainModels.cs`.
- [x] Extend `ReleaseStoreData` and `ReleaseRepository` with train CRUD, atomic per-action updates, lookup, and serialized write coordination.
- [x] Add normalization for missing fields/legacy JSON so existing profiles and `releases.json` remain loadable.
- [x] Ensure configuration export/import includes group configuration and train data according to the existing bundle contract.
- [ ] Add Core tests for legacy deserialization, group snapshots, atomic resume state, partial outcomes, backup recovery, and concurrent update serialization.

## Phase 2 — Azure DevOps API expansion

- [x] Add `AdoPullRequest`, PR commit references, build details, and correlation candidates to `src/SwebKit.Core/Models/DevOpsModels.cs`.
- [x] Extend `IDevOpsClient` with narrowly required read/write methods:
  - find/list active PRs by repository/source/target;
  - create PR;
  - get PR with source/target/merge commit metadata and web URL;
  - get branch head/ref;
  - get tag by name with object ID;
  - get pipeline runs including repository/version/source commit;
  - get one build/run detail and timeline/checkpoint waiting-gate data.
- [x] Do **not** add PR completion, approval, tag deletion/move, or routine pipeline-trigger operations to the release-train UI contract.
- [x] Add DTOs and REST mappings to `SwebKit.DevOps/AdoApiModels.cs` and `DevOpsClient.cs`, with URL escaping and cancellation propagation.
- [x] Improve pipeline run mapping so `AdoPipelineRun` includes source repository `version`/commit where ADO exposes it.
- [x] Update `DemoDevOpsClient` with deterministic PR, tag, merge, run, stage, approval, drift, and partial-failure scenarios.
- [ ] Add request/response tests for duplicate PR/tag idempotency, different-SHA tag conflict, pagination, special characters, malformed metadata, cancellation, and auth isolation.

## Phase 3 — PAT authentication and sidecar integration

- [ ] Add `SwebKit.DevOps` as a project reference in `src-sidecar/SwebKit.Sidecar.csproj`.
- [ ] Register `DevOpsAuthHandler` as transient, named `AzureDevOps` `HttpClient`, `IDevOpsClientFactory`, and `ReleaseTrainService` in `Program.cs`.
- [ ] Add PAT credential endpoints that accept the secret only in the request body, save through `ICredentialStore`, return only configured/not-configured status, and support replace/delete with UI confirmation.
- [ ] Never return, log, export, or persist PAT values. Redact ADO error bodies that could contain sensitive request context.
- [ ] Add `DevOpsEndpoints.cs` with grouped endpoints for connection test, discovery, group validation, train list/detail/create/preflight, confirmed tag/PR execution and retry, refresh/correlation/manual run attachment, and remarks/update/complete.
- [ ] Use body/query DTOs for names and refs rather than putting arbitrary branch/project names in path segments.
- [ ] Load `ReleaseRepository` during sidecar startup before serving train requests.
- [ ] Add endpoint/service tests for validation, no-secret responses, idempotency, partial failure/retry, cancellation, persisted resume after service recreation, and expected ProblemDetails/error payloads.

## Phase 4 — Reusable group and PAT settings UI

- [ ] Correct the React `DevOpsConfig` TypeScript shape in `web/src/lib/types.ts` to mirror the real C# config and add train/group types.
- [ ] Add `useDevOps.ts` and `useReleaseTrains.ts` TanStack Query hooks.
- [ ] Add `DevOpsSettings.tsx` to `SettingsPage.tsx` with PAT setup, organization/auth mode, discovery selectors, group CRUD, branch defaults/overrides, stage alias mapping, and merge-strategy field.
- [ ] Reuse shared form/button/dialog/notification components; every mutation exposes disabled/loading states and feedback.
- [ ] Preserve unsaved edits safely and require confirmation before deleting a group or replacing/deleting credentials.
- [ ] Add settings Playwright coverage for empty/loading/error/single-component/multi-project groups, special characters, PAT masking/status, and persistence across reload.

## Phase 5 — Release wizard and active cockpit

- [ ] Add a lazy `/release-trains` route in `web/src/App.tsx`, navigation item in `AppLayout.tsx`, and command-palette navigation.
- [ ] Build `ReleaseTrainsPage.tsx` with active/history list, status filters, empty/error states, and detail workspace.
- [ ] Build the multi-step wizard with per-component versions/remarks, full preflight results, an exact action review, and one explicit batch confirmation.
- [ ] Build the component matrix/timeline showing Tag → PR → TST → STG approval → STG → PRD approval → PRD, with blockers and deep links.
- [ ] Implement bounded polling only for active visible trains, pause when hidden, and offer manual refresh. Avoid one query per row where a batch endpoint can return a complete train snapshot.
- [ ] Add safe retry controls only for failed/pending actions and evidence-rich manual run attachment.
- [ ] Add drift warnings when PR source moves after tagging and merge-strategy mismatch warnings after completion.
- [ ] Add `data-testid`, keyboard/focus behavior, Escape-to-close, and readable 1280px layouts.
- [ ] Extend demo mode so the full journey can be exercised without ADO credentials.

## Phase 6 — Confluence-ready draft

- [ ] Add a pure formatter that converts a train snapshot into row data, Markdown, and rich/plain clipboard representations.
- [ ] Add editable overall/component remarks persisted through train update endpoints.
- [ ] Add preview, copy rich table, and copy Markdown actions with notifications and deterministic output.
- [ ] Include unresolved blockers and refresh time so stale handoffs cannot look finished.
- [ ] Unit-test escaping for pipes, line breaks, links, Unicode, empty remarks, and missing pipeline/approval URLs.
- [ ] Add Playwright coverage proving copied content contains the correct per-component versions and links after simulated stage progression.

## Phase 7 — Entra authentication milestone

- [ ] Introduce an ADO auth-provider abstraction rather than branching throughout `DevOpsClient`.
- [ ] Keep PAT behavior unchanged and add an Entra token provider using the existing centrally managed `Azure.Identity` dependency.
- [ ] Use an explicit desktop-safe credential chain; do not permit an interactive browser flow in the sidecar unless intentionally surfaced and tested through Tauri.
- [ ] Add connection diagnostics that distinguish not signed in, tenant/organization denial, missing ADO permissions, and network errors without exposing tokens.
- [ ] Test PAT and Entra clients in parallel for no credential cross-bleed, token refresh, cancellation, and configuration switching.
- [ ] Mark Entra available only after a real organization smoke test confirms the tenant’s ADO policy supports it.

## Phase 8 — Documentation, security, and final validation

- [ ] Update `docs/features/README.md`, architecture index/codebase guide, `functionalities/releases.md`, settings/configuration docs, and top-level architecture notices to distinguish focused React Release Trains from the legacy MAUI Pipelines hub.
- [ ] Update `docs/pitfalls/react-frontend.md` or DevOps pitfalls if implementation uncovers repeatable correlation/auth/tagging traps.
- [ ] Document required least-privilege permissions: code read/contribute and PR creation; tag creation; pipelines/build and environment/approval read; no PR-complete or approval permission required by v1 unless inherited.
- [ ] Run Aikido full scans on every added/modified first-party file and fix/rescan to zero findings; report unavailable MCP tooling explicitly.
- [ ] Run the verification matrix and record results in feature status/test-plan docs.
