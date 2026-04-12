# Backend Plan - incident-investigation-workflows

---

title: "Backend Plan - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Add a safe, additive backend contract layer for investigation seeds, incident snapshot export, and mapping or dependency proposals so source pages can launch into Incident Timeline without weakening the workload-scoped evidence model already established by `incident-timeline-workbench`.

## Impacted areas

- Existing contracts and models:
- `src/SwebKit.Core/Models/IncidentTimelineModels.cs`
- `src/SwebKit.Core/Domain/IncidentTimelineConfig.cs`
- `src/SwebKit.Core/Models/ObservabilityModels.cs`
- `src/SwebKit.Core/Models/ServiceBusModels.cs`
- `src/SwebKit.Core/Models/DevOpsModels.cs`
- `src/SwebKit.Core/Models/ReleaseModels.cs`
- Existing services and abstractions:
- `src/SwebKit.Core/Abstractions/IIncidentTimelineService.cs`
- `src/SwebKit.Core/Abstractions/IIncidentTimelineSignalSource.cs`
- `src/SwebKit.Core/Services/IncidentTimelineService.cs`
- Existing source adapters that may contribute richer evidence references:
- `src/SwebKit.Observability/IncidentTimeline/AppInsightsTimelineSignalSource.cs`
- `src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs`
- `src/SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs`
- `src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs`
- Planned new contracts and services:
- `src/SwebKit.Core/Abstractions/IIncidentSnapshotExporter.cs`
- `src/SwebKit.Core/Abstractions/IIncidentInvestigationSeedResolver.cs`
- `src/SwebKit.Core/Services/IncidentSnapshotExporter.cs`
- `src/SwebKit.Core/Services/IncidentInvestigationSeedResolver.cs`

## Design

The design should keep the existing incident-timeline aggregation model intact:

1. A source page emits an `IncidentInvestigationSeed` containing source provenance, bounded time context, explicit evidence references, optional correlation identifiers, and any existing workload candidate already known to the source.
2. A resolver normalizes that seed into an `IncidentTimeline` draft scope and landing summary. The resolver may use existing `IncidentTimelineConfig` mappings, release snapshots, or source-specific evidence references, but it must not invent ownership silently.
3. `IncidentTimelineService` continues to execute workload-scoped evidence fan-out. A launch seed may narrow the query or preselect sources, but it must not bypass existing inclusion rules from `incident-timeline-workbench`.
4. After a result is loaded, an exporter builds a sanitized snapshot containing the seed, selected scope, returned items, coverage states, and redaction or truncation metadata.
5. Proposal generation should reuse the evidence already loaded in memory and return `candidate` mapping or dependency observations with explanation text. Persistence remains explicit and separate.

## API / Contracts

- Planned additive contracts in `IncidentTimelineModels.cs`:
- `IncidentInvestigationSeed` with source area, launched-at time, selected range, evidence references, candidate workload scope, selected sources, and correlation identifiers.
- `IncidentInvestigationSource` enum covering Observability, ServiceBus, Pipelines, and future launchers.
- `IncidentSeedEvidenceRef` for source-specific identifiers such as resource ID, queue path, message ID, run ID, or release snapshot keys.
- `IncidentSnapshotExport` and `IncidentSnapshotExportItem` for JSON and markdown serialization.
- `IncidentMappingProposal` and `IncidentDependencyObservation` with status, rationale, and persistence boundary metadata.
- Planned service seams:
- `IIncidentInvestigationSeedResolver` to normalize a seed into a draft query plus banner metadata.
- `IIncidentSnapshotExporter` to build exportable bundles from the latest timeline page result.
- Backward compatibility rules:
- Existing `IIncidentTimelineService.GetTimelineAsync` remains the single execution path for cross-source evidence.
- Seed launch may prefill draft state, but it must not become a hidden alternate query engine.

## Tasks

### Wave 1 - seed resolution and evidence continuity [dotnet-expert]

- [ ] Add investigation seed contracts and source provenance models.
- [ ] Implement seed resolution against `IncidentTimelineConfig` and existing source references.
- [ ] Ensure correlation-ID passthrough narrows or explains evidence only inside the selected workload scope.
- [ ] Keep cancellation and last-request-wins behavior unchanged when a new seed replaces an older one.

### Wave 2 - snapshot export [dotnet-expert]

- [ ] Define sanitized export schemas for JSON and markdown.
- [ ] Implement payload redaction, body truncation, and source-coverage summaries.
- [ ] Add deterministic file naming and export metadata for partial or truncated results.

### Wave 3 - proposals and dependency groundwork [dotnet-expert]

- [ ] Define candidate mapping and dependency-observation contracts.
- [ ] Generate proposals only from explicit evidence references and existing loaded data.
- [ ] Add an accept path that hands off to Settings or explicit config updates rather than writing automatically.

### Wave 4 - later-wave watchlists and light automation [dotnet-expert]

- [ ] Evaluate optional config additions for saved watchlists or investigation presets.
- [ ] Keep any automation prefill-only and out of the early implementation path.

## Migration and runtime changes

- No mandatory schema migration is required for Wave 1 or Wave 2.
- Likely additive config changes may appear later in `IncidentTimelineConfig` for accepted proposals or saved watchlists, but early implementation should avoid broadening persisted config until the proposal model is reviewed.
- Export output should be local and operator-triggered only. No background persistence or auto-upload is planned.

## Validation

- Unit tests: Not started. Add coverage in `tests/SwebKit.Core.Tests` for seed normalization, proposal-only behavior, snapshot redaction, and stale-seed replacement.
- Integration tests: Not started. Extend source-adapter tests only where new evidence references or proposal inputs must be normalized.
- Manual checks: Verify that a drill-through seed never bypasses workload inclusion rules and that exports remain sanitized and bounded.

## Notes

- `dotnet-csharp.md` guidance on `OperationCanceledException` still applies. Seed replacement must not swallow cancellation.
- `azure-sdk.md` guidance matters for any export or proposal flow that enumerates Azure resources or Service Bus entities while collecting evidence references.
- Any dependency observation should be described as an observed candidate edge, not as a proven topology relationship.
