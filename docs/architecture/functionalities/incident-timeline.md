# Incident Timeline

## What It Supports Today

- A dedicated `/incident-timeline` workbench page in `SwebKit.App` with left-nav access and shell-level refresh integration.
- AKS-backed scope bootstrap for cluster context and namespace selection via `IAksClientBootstrapper`.
- Manual-refresh-only workflow for one workload scope at a time: context, namespace, workload kind, workload name, time window, and source toggles.
- Source toggles show explicit `On` / `Off` state text with stronger active/inactive styling so source inclusion is readable at a glance.
- Scope summary, source coverage strip, evidence timeline, and detail panel with explicit "linked because" explanations.
- Mapping guidance callouts when selected sources come back `Unmapped` or `Not configured`, with a direct deep link into Settings > Incident Timeline for the current scope.
- Empty, partial, truncation, and all-sources-failed states that stay evidence-first and never imply root cause.
- Cancellation-first request handling with last-request-wins versioning so rapid refreshes or scope edits do not flash stale evidence.
- Mapping-backed workload suggestions with a free-text workload name field so AKS-only investigations remain possible even when non-AKS mappings are absent.
- Shared workspace integration for context, namespace, workload kind/name, time window, and source toggles, allowing recent/favorite reopen and named favorite restore from shell surfaces.
- Investigation seed launch from Observability, Service Bus, and Pipelines source pages. Each source page carries a typed `IncidentInvestigationSeed` (time range, evidence references, candidate scope) through `IncidentInvestigationLauncher` into the investigation page without implying root cause.
- `InvestigationSeedBanner` renders on landing after a seed launch, showing source provenance and pending scope assumptions. Operators must confirm or dismiss before evidence is loaded.
- Investigation seed normalization via `IIncidentInvestigationSeedResolver` maps evidence references (resource IDs, entity paths, pipeline IDs, correlation IDs) to known workload mappings, producing a draft scope and pre-selected source toggles.
- Snapshot export via `IIncidentSnapshotExporter`: builds a sanitized, bounded export bundle from loaded evidence. Metadata is filtered to an explicit allow-list, values are truncated, and every export includes a source coverage summary and a disclaimer. Exported as JSON or Markdown.
- Mapping proposals via `IIncidentMappingProposalGenerator`: after each successful refresh, inspects source statuses for `Unmapped` or `NotConfigured` coverage and generates advisory-only candidate proposals. Proposals are never persisted automatically and require explicit operator acceptance through Settings.

## Core Runtime Flow

1. `IncidentTimelinePage` waits for `AppStateService` initialization and asks `IAksClientBootstrapper` for the current contexts, namespaces, active context, and default namespace.
2. The page seeds its initial workload scope from `AppConfig.IncidentTimeline.WorkloadMappings` when present, or falls back to the first watched deployment from `AksConfig`.
3. The page renders the last loaded evidence and coverage while scope edits are pending. Changing scope does not auto-query in v1.
4. Refresh cancels the current `CancellationTokenSource`, increments a request version, and calls `IIncidentTimelineService.GetTimelineAsync(query, ct)`.
5. The backend returns one aggregated `IncidentTimelinePage`; the UI projects the items and source statuses without re-implementing inclusion logic.
6. If the returned source coverage includes `Unmapped` or `Not configured`, the page renders a focused guidance note with a direct navigation path to `/settings?section=incident-timeline` for the selected workload scope.
7. Only the latest request version is allowed to update the page state. Stale responses are ignored even if they complete after a newer refresh.
8. Scope edits and successful refreshes publish a semantic workspace snapshot; route-first restore rehydrates the scope and selected sources before refresh.

## Key Design Notes

- `IncidentTimelinePage` is the only orchestrator for the workbench. Child components under `Components/IncidentTimeline/` stay projection-only.
- The page keeps a request fingerprint so it can distinguish draft scope from the currently loaded result and surface an explicit pending-refresh state.
- Source toggles always keep at least one selected source. The page disables refresh only when the scope is incomplete or AKS bootstrap is still running.
- Mapping discoverability is intentional: the page does not silently leave `Unmapped` or `Not configured` coverage unexplained.
- The first evidence item becomes the default detail-panel selection for a new result set. Operators can switch detail focus by selecting another timeline row.
- The page sets the `incident-timeline` area connection state so the shared status bar can reflect whether the workbench last connected successfully or failed globally.
- `IncidentTimelinePage` registers an area restore handler with `OperatorWorkspaceService` and keeps restore state semantic: context, namespace, workload kind/name, window, and selected sources.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor.css`
- `src/SwebKit.App/Components/IncidentTimeline/` — workbench toolbar, coverage strip, timeline list, detail panel, seed banner, export dialog, mapping proposal panel
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- `src/SwebKit.Core/Abstractions/IIncidentTimelineService.cs`
- `src/SwebKit.Core/Abstractions/IIncidentInvestigationSeedResolver.cs`
- `src/SwebKit.Core/Abstractions/IIncidentSnapshotExporter.cs`
- `src/SwebKit.Core/Abstractions/IIncidentMappingProposalGenerator.cs`
- `src/SwebKit.Core/Models/IncidentTimelineModels.cs`
- `src/SwebKit.Core/Services/IncidentInvestigationSeedResolver.cs`
- `src/SwebKit.Core/Services/IncidentSnapshotExporter.cs`
- `src/SwebKit.Core/Services/IncidentMappingProposalGenerator.cs`
- `src/SwebKit.App/Services/IncidentInvestigationLauncher.cs`
- `src/SwebKit.App/Services/AksClientBootstrapper.cs`

## Validation Pointers

- `tests/SwebKit.App.Tests/IncidentTimelinePageTests.cs`
- `tests/SwebKit.E2E.Tests/AppUiTests.cs`
- `tests/SwebKit.Core.Tests/IncidentTimelineServiceTests.cs`
