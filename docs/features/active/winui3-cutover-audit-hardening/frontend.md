# Frontend — WinUI cutover audit and hardening

## Scope

This module covers the remaining WinUI host work after the first migration baseline: parity gap closure, shared UI refactors, runtime blocker triage, and cutover readiness evidence. It does not re-plan Incident Timeline or add new product features.

## Architecture touchpoints

- Project: `src/SwebKit.WinUI/`
- Current shell entry points: `src/SwebKit.WinUI/App.xaml.cs`, `src/SwebKit.WinUI/MainWindow.xaml`, `src/SwebKit.WinUI/MainWindow.xaml.cs`
- Shared shell controls: `src/SwebKit.WinUI/Controls/Shell/`
- Shared page primitives: `src/SwebKit.WinUI/Controls/Shared/PageScaffold.xaml`
- Key routed views and view-models:
  - `src/SwebKit.WinUI/Views/Dashboard/`
  - `src/SwebKit.WinUI/Views/ServiceBus/`
  - `src/SwebKit.WinUI/Views/Aks/`
  - `src/SwebKit.WinUI/Views/Redis/`
  - `src/SwebKit.WinUI/Views/Storage/`
  - `src/SwebKit.WinUI/Views/Pipelines/`
  - `src/SwebKit.WinUI/Views/Observability/`
- Data flow: `App.xaml.cs` boots the host, `MainWindow` maps area keys to native pages, page code-behind schedules initial async loads, and view-models own the operator workspace and service orchestration.

## Current evidence summary

### What is already true

- The current WinUI host is a real native route shell, not a blank scaffold. `MainWindow.xaml.cs` maps dashboard, settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability directly to WinUI pages.
- The app launches and remains alive outside the debugger. The current blocker is not a launch-time failure.
- Shared shell primitives exist for banner, status, context header, notification history, and workspace hub.
- A shared page wrapper exists as `PageScaffold`, and all major routed pages now use it.

### What is still incomplete structurally

- The shared page layer is still narrow. The repo currently has `PageScaffold`, but not the broader shared `StateView`, metric-card, section-card, or detail-pane host set that the migration plan expected.
- Initial page activation for `Dashboard`, `AKS`, `Redis`, `Storage`, `Pipelines`, and `Observability` now runs through `DeferredPageLoadScheduler`; the remaining lifecycle hardening work is about reusable state views and environment-readiness treatment rather than duplicated `Loaded` handlers.
- Some page orchestration seams are becoming too large. `PipelinesPageViewModel` now owns project loading, releases, approvals, and release-tag flows in one class.
- The auth-sensitive routes now expose explicit readiness treatment instead of generic failure text: Pipelines maps failed Azure DevOps connection validation into a settings-oriented readiness state, and Observability maps `DefaultAzureCredential` failures into a sign-in/readiness state while keeping unexpected errors as true error cases.

### Exception investigation findings

- The debugger stop in generated `App.g.i.cs` is the WinUI-generated debug hook for unhandled exceptions. It is not itself a fix surface.
- Build-time analysis is clean for `App.xaml`, `PipelinesPage.xaml`, and `PipelinesPageViewModel.cs`.
- Runtime evidence gathered so far points to handled failures, not an unconditional startup crash:
  - Pipelines baseline load now maps Azure DevOps auth/access validation failures into an explicit readiness state instead of falling through the generic error-log branch.
  - Observability resource discovery currently logs an `Azure.Identity.CredentialUnavailableException` when `DefaultAzureCredential` cannot resolve a token.
- `App.xaml.cs` now logs the underlying unhandled exception before the generated WinUI debug hook breaks in `App.g.i.cs`, so any remaining debugger stop can be traced to the real exception payload instead of the generated file alone.
- Follow-up requirement: capture the exact route and interaction that still produces a debugger break, if it remains reproducible.

## Domain gap matrix

| Area                         | Current WinUI baseline                                                                                               | Remaining parity / hardening work                                                                                                                                                        | Refactor pressure                                                                |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| Shell / Dashboard / Settings | Native route coverage exists, theme application and shell chrome are present, dashboard is the default landing route | Finish shared state views, tighten manual shell checkpoint, complete settings/readiness parity, document cutover cues                                                                    | High: shell primitives exist but the shared page-state layer is still incomplete |
| Service Bus                  | Baseline browsing and core message workflows are native                                                              | Later-phase parity from the migration plan still needs closure: scheduled workflows, templates, advanced filters/columns, destructive bulk safety, and final workspace-restore hardening | Medium                                                                           |
| AKS                          | Native bootstrap, pod browse, logs, port-forward, and shell launch exist                                             | Remaining parity still includes broader resource coverage, richer diagnostics panels, and deeper operational actions from the original checklist                                         | High: repeated diagnostics layout patterns should move into shared primitives    |
| Redis                        | Native key browse and typed detail baseline exist                                                                    | Health/prefix tooling, slow-log/deeper analysis, and wider bulk-operation coverage remain open                                                                                           | Medium                                                                           |
| Storage                      | Native account/container/blob baseline exists, including SAS copy and text-friendly preview                          | Bulk ZIP/version-download polish, large-file/binary-preview hardening, and final parity checks remain open                                                                               | Medium                                                                           |
| Pipelines / Releases         | Native tabs, approvals, and release-tag manager baseline exist                                                       | Deeper tree/detail parity, tag-manager validation, richer release editing coverage, and environment-sensitive failure handling remain open                                               | High: page/view-model scope is widening quickly                                  |
| Observability                | Native route, five-tab baseline, selected charts, heatmap toggle, and saved-query baseline exist                     | Monaco/WebView2 editor host, broader chart parity, deeper drill-through flows, and credential/readiness hardening remain open                                                            | High: editor and auth/readiness seams should be shared rather than page-local    |

## Design decisions

| #   | Decision                                                                                        | Rationale                                                                                                  | Alternative considered                                        |
| --- | ----------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| 1   | Treat the next wave as hardening-first rather than page-expansion-first                         | The remaining work clusters around parity, validation, and shared structure more than missing route shells | Keep widening the current migration feature until phase 8     |
| 2   | Do not patch generated `App.g.i.cs`                                                             | The generated debugger-break line only reports unhandled exceptions in debug sessions                      | Suppress the generated break and lose a useful signal         |
| 3   | Consolidate repeated page activation and shared state layouts before more feature-specific XAML | The repetition is already visible across most routed pages                                                 | Continue with per-page `Loaded` handlers and later unify them |

## Implementation tasks

- [ ] Confirm the cutover-critical checklist for each domain and move non-critical polish out of the cutover path.
- [ ] Add a shared state-view layer for loading, empty, error, not-configured, and environment-readiness states.
- [ ] Add shared metric-card / section-card / detail-pane primitives and refactor the most repetitive pages onto them.
- [x] Replace the repeated per-page initial-load scheduling pattern with one shared approach.
- [ ] Extract the highest-pressure page seams, starting with Pipelines and any page that mixes connection orchestration with multiple nested workspaces.
- [ ] Add a reproducible exception-triage workflow for debugger-break investigations.
- [x] Normalize Pipelines and Observability auth/readiness behavior so live-environment failures are explicit and non-misleading.
- [ ] Record the first manual checkpoint run and expand focused WinUI automated coverage beyond the readiness formatter seam.

## Validation notes

- Unit tests: initial focused coverage now exists in `tests/SwebKit.WinUI.Tests/`, covering readiness formatter classification, readiness-state suppression, and the generic error fallback paths in the Pipelines and Observability view-models; expand next into shell state, page activation sequencing, and higher-risk view-model seams.
- Integration tests: keep `build-winui` green and re-run targeted domain tests when domain-layer behavior changes.
- Manual checks: verify route activation, failure-state handling, and the exact debugger-break reproduction path under a debugger.
- Known edge cases to verify explicitly:
  - Azure DevOps PAT missing or invalid
  - `DefaultAzureCredential` unavailable on the current machine
  - page navigation away during initial async load
  - large routed pages with mixed list/detail state
