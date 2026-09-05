# Release Train Cockpit — Technical Plan

See `index.md` for scope, non-goals, and product-decision context. This document tracks the module-by-module implementation. Modules are ordered by dependency.

## Phase 0 — Feature contract and real ADO validation

- [x] Create `docs/features/active/release-train-cockpit/` with `index.md`, `technical-plan.md`, `test-plan.md`, and `ux-plan.md`.
- [x] Record the product-decision reversal: DevOps remains excluded as a generic pipeline browser, but a focused Release Trains capability is now in scope for Tauri + React.
- [x] Implement merge-strategy/run-correlation support in the model and client (`expectedMergeStrategy`, `sourceVersion`/`sourceCommit` discovery, manual run-attach candidate evidence).
- [ ] Validate against representative real ADO data before the feature leaves review. This is the remaining hard gate.

## Phase 1 — Core models and durable local state

- [x] Extend `src/SwebKit.Core/Domain/DevOpsConfig.cs` with authentication mode and reusable release-group/component binding models.
- [x] Add release-train aggregate/state/event models under `src/SwebKit.Core/Models/ReleaseTrainModels.cs`.
- [x] Extend `ReleaseStoreData` and `ReleaseRepository` with train CRUD, atomic per-action updates, lookup, and serialized write coordination.
- [x] Add normalization for missing fields/legacy JSON so existing profiles and `releases.json` remain loadable.
- [x] Ensure configuration export/import includes group configuration and train data according to the existing bundle contract.
- [x] Add Core tests for legacy deserialization, group snapshots, atomic resume state, partial outcomes, and concurrent update serialization (tests pass except one pre-existing git-proxy test).

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
- [x] Add request/response tests covering PAT/Entra auth isolation, cancellation, and client constructor normalization.

## Phase 3 — PAT authentication and sidecar integration

- [x] Add `SwebKit.DevOps` as a project reference in `src-sidecar/SwebKit.Sidecar.csproj`.
- [x] Register `DevOpsAuthHandler` as transient, named `AzureDevOps` `HttpClient`, `IDevOpsClientFactory`, and `ReleaseTrainService` in `Program.cs`.
- [x] Add PAT credential endpoints that accept the secret only in the request body, save through `ICredentialStore`, return only configured/not-configured status, and support replace/delete with UI confirmation.
- [x] Never return, log, export, or persist PAT values.
- [x] Add `ReleaseTrainEndpoints.cs` with endpoints for connection test, train list/detail/create/preflight/execute/refresh/complete, manual run attach, remarks update, and demo advance.
- [x] Use body/query DTOs for names and refs rather than putting arbitrary branch/project names in path segments.
- [x] Load `ReleaseRepository` during sidecar startup before serving train requests.
- [x] Add endpoint/service tests for validation, no-secret responses, idempotency, partial failure/retry, cancellation, and persisted resume after service recreation.

## Phase 4 — Reusable group and PAT settings UI

- [x] Correct the React `DevOpsConfig` TypeScript shape in `web/src/lib/types.ts` to mirror the real C# config and add train/group types.
- [x] Add `useDevOps.ts` TanStack Query hooks covering PAT, connection test, release groups, and the full release-train lifecycle.
- [x] Add `DevOpsSettings.tsx` to `SettingsPage.tsx` with PAT setup, organization/auth mode, group CRUD, branch defaults/overrides, stage alias mapping, and merge-strategy field.
- [x] Reuse shared form/button/dialog/notification components; every mutation exposes disabled/loading states and feedback.
- [x] Preserve unsaved edits safely and require confirmation before deleting a group or replacing/deleting credentials.
- [x] Settings E2E suite continues to pass; remaining Playwright coverage for DevOps-specific flows is a follow-up item.

## Phase 5 — Release wizard and active cockpit

- [x] Add a lazy `/release-trains` route in `web/src/App.tsx`, navigation item in `AppLayout.tsx`, and command-palette navigation.
- [x] Build `ReleaseTrainsPage.tsx` with active/history list, status filters, empty/error states, and detail workspace.
- [x] Build the multi-step wizard with per-component versions/remarks, full preflight results, an explicit action review, and batch confirmation.
- [x] Build the component matrix/timeline showing Tag → PR → TST → STG approval → STG → PRD approval → PRD, with blockers and deep links.
- [x] Implement bounded polling, manual refresh, and safe retry controls for failed/pending actions.
- [x] Add drift warnings when PR source moves after tagging.
- [x] Add `data-testid`, keyboard/focus behavior, Escape-to-close, and responsive layouts.
- [x] Extend demo mode so the full journey can be exercised without ADO credentials.

## Phase 6 — Confluence-ready draft

- [x] Add a pure formatter that converts a train snapshot into row data, Markdown, and rich/plain clipboard representations.
- [x] Add editable overall/component remarks persisted through train update endpoints.
- [x] Add preview, copy rich table, and copy Markdown actions with notifications and deterministic output.
- [x] Include unresolved blockers and refresh time so stale handoffs cannot look finished.
- [x] Unit-test escaping for pipes, line breaks, Unicode, and missing pipeline/approval URLs.
- [ ] Add Playwright coverage for the full copy/draft flow is a follow-up item.

## Phase 7 — Entra authentication milestone

- [x] Introduce an ADO auth-provider abstraction (`IAuthenticationTokenProvider`) instead of branching throughout `DevOpsClient`.
- [x] Keep PAT behavior unchanged and add an Entra token provider using the existing centrally managed `Azure.Identity` dependency.
- [x] Use a desktop-safe `DefaultAzureCredential` chain; no interactive browser flow in the sidecar unless intentionally surfaced through Tauri.
- [x] Add connection diagnostics that distinguish not signed in, tenant/organization denial, missing ADO permissions, and network errors without exposing tokens.
- [x] Test PAT and Entra clients for no credential cross-bleed and configuration switching.
- [ ] Mark Entra available only after a real organization smoke test confirms the tenant’s ADO policy supports it.

## Phase 8 — Documentation, security, and final validation

- [x] Update `docs/features/README.md` and this feature folder to distinguish focused React Release Trains from the legacy MAUI Pipelines hub.
- [ ] Update `docs/pitfalls/react-frontend.md` or DevOps pitfalls if repeatable correlation/auth/tagging traps emerge during real-ADO validation.
- [x] Document required least-privilege permissions: code read/contribute and PR creation; tag creation; pipelines/build and environment/approval read; no PR-complete or approval permission required by v1 unless inherited.
- [ ] Run Aikido full scans on every added/modified first-party file and fix/rescan to zero findings; Aikido MCP was not available in this environment and is reported explicitly.
- [x] Run the verification matrix and record results in the test plan.
