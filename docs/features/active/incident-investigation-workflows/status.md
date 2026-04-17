# Status - incident-investigation-workflows

---

title: "incident-investigation-workflows"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-14"

---

## Quick summary

Wave 1 and Wave 2 are complete. Snapshot export, mapping proposals, and all DI wiring are implemented. Build is clean; 49 unit tests pass (16 Wave 1 + 33 Wave 2). Pre-ship review is the next step.

Jira: not linked

Current focus: Pre-ship review before shipping Waves 1 + 2.

## Progress checklist

### Wave 1 - investigation launch and evidence continuity

- [x] Finalize the `IncidentInvestigationSeed` contract and source provenance model
- [x] Choose the app-layer launch mechanism — singleton `IncidentInvestigationLauncher` storing pending seed + navigation
- [x] Define and implement landing-banner behavior on `/incident-timeline` (`InvestigationSeedBanner`)
- [x] Register `IIncidentInvestigationSeedResolver` and `IncidentInvestigationLauncher` in `MauiProgram.cs`
- [x] Add `Investigate` action to `ObservabilityFailures` (exception group → seed with ResourceId + ExceptionType)
- [x] Add `Investigate` action to `MessageDetailPane` (message → seed with EntityPath + MessageId + CorrelationId)
- [x] Add `Investigate` action to `PipelineDetail` (pipeline → seed with PipelineId + RunId + ProjectName)
- [x] Unit tests: `IncidentInvestigationSeedResolverTests` (16 tests covering all matching paths, biasing, and provenance)

### Wave 2 - snapshot export + mapping proposals

- [x] Add Wave 2 models to `IncidentTimelineModels.cs`: `IncidentSnapshotExportItem`, `IncidentSnapshotSourceCoverage`, `IncidentSnapshotExport`, `IncidentProposalStatus`, `IncidentMappingProposal`, `IncidentDependencyObservation`
- [x] `IIncidentSnapshotExporter` abstraction + `IncidentSnapshotExporter` implementation (allow-list metadata redaction, value truncation at 200 chars, JSON/Markdown output, deterministic file naming, coverage label)
- [x] `IIncidentMappingProposalGenerator` abstraction + `IncidentMappingProposalGenerator` implementation (unmapped/notconfigured source → advisory proposal with rationale; never persists or mutates config)
- [x] `IncidentSnapshotExportDialog.razor` + CSS (format picker, file name preview, JS blob download via `SwebKitUi.downloadTextFile`)
- [x] `MappingProposalPanel.razor` + CSS (advisory-only, dismiss button, Settings handoff link)
- [x] Wire export dialog + proposal panel into `IncidentTimelinePage.razor` (Export Snapshot button, `_showExportDialog`, `_mappingProposals`, `GenerateMappingProposals` on refresh success)
- [x] Add `downloadTextFile` to `wwwroot/js/uiState.js`
- [x] Register `IIncidentSnapshotExporter` and `IIncidentMappingProposalGenerator` as singletons in `MauiProgram.cs`
- [x] Unit tests: `IncidentSnapshotExporterTests` (22 tests) + `IncidentMappingProposalGeneratorTests` (11 tests)

### Wave 3 - deferred watchlists and light automation

- [ ] Decide what qualifies as a watchlist versus a saved investigation preset
- [ ] Constrain any automation to advisory or prefill-only behavior

## Completed

- Wave 1 fully implemented: seed contracts, resolver service, launcher, banner component, IncidentTimelinePage wiring, all three source-page buttons, DI registration, unit tests.
- Wave 2 fully implemented: snapshot export with metadata redaction, mapping proposals with Settings handoff, 33 new unit tests, build clean.

## Remaining

- Ship (commit + push + PR)
- Wave 3 (watchlists/automation) — explicitly deferred
- Component tests (SwebKit.App.Tests) for launch actions and export dialog — deferred to Wave 3 prep
- E2E drill-through flows — deferred to Wave 3 prep

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Wave 1: 16 unit tests passing.
- Wave 2: 33 unit tests passing (22 exporter + 11 proposal generator).
- App build: 0 errors (SwebKit.App project, net10.0-windows10.0.19041.0).

## Notes

- Evidence-first wording is mandatory for the launch banner, export metadata, and proposal explanations.
- Any design that turns a source-page click directly into an auto-refreshed, auto-inferred incident result should be treated as a design regression.
- `IncidentInvestigationLauncher` is registered as `AddScoped` (not singleton) because it depends on `NavigationManager` which is scoped in Blazor.
- `IIncidentSnapshotExporter` and `IIncidentMappingProposalGenerator` are registered as singletons (stateless services).
- Snapshot exporter uses an explicit allow-list of metadata keys — no key-contains matching to avoid false negatives leaking new safe keys accidentally added by signal sources.
