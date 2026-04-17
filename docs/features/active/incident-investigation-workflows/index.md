# Feature Overview - incident-investigation-workflows

---

title: "Feature Overview - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "In Progress"
jira: "not linked"
created: "2026-04-17"
updated: "2026-04-17"

---

## Goal

Wire Observability, Service Bus, and Pipelines as drill-through launch points so operators
can start an evidence-backed incident investigation from inside any source page without
manually re-entering the workload scope on the Incident Timeline page.

## Value

The Incident Timeline Workbench already exists and is validated. The remaining gap is
discovery friction: an operator watching a failing App Insights resource or a stalled
Service Bus entity currently has to navigate to `/incident-timeline`, recall the correct
workload name, and re-select the right time window — with no memory of what triggered the
investigation.

This feature closes that gap with explicit drill-through: every source page can package its
current context into an `IncidentInvestigationSeed`, hand it to `IncidentInvestigationLauncher`,
and land on the workbench with pre-filled scope, pre-selected sources, and a visible
provenance banner that explains what was seeded and what still requires operator confirmation.

## Scope

- In scope:
  - "Investigate" action on ObservabilityPage (resource, time range, resourceId evidence ref).
  - "Investigate" action on ServiceBusPage (entity path, optional message ID / correlation ID evidence ref).
  - "Investigate" action on PipelinesPage (pipeline ID, project name, pipeline name evidence ref).
  - `InvestigationSeedBanner` display and confirm/dismiss flow on IncidentTimelinePage (already implemented).
  - Targeted unit tests for seed construction logic from each source area.
- Out of scope:
  - Auto-seeding workload scope without operator confirmation.
  - Passing message bodies, payloads, or secrets in the seed.
  - AKS-page drill-through (AKS already has native scope on the incident page).
  - Snapshot export or mapping proposal UI (pre-existing separate concerns).

## Dependencies

- `docs/features/active/incident-timeline-workbench/` — base workbench; must remain `Review`-validated before this feature goes to production.
- `src/SwebKit.Core/Models/IncidentTimelineModels.cs` — `IncidentInvestigationSeed`, `IncidentSeedEvidenceRef`
- `src/SwebKit.App/Services/IncidentInvestigationLauncher.cs`
- `src/SwebKit.App/Components/IncidentTimeline/InvestigationSeedBanner.razor`
- `src/SwebKit.Core/Services/IncidentInvestigationSeedResolver.cs`
- Architecture constraints:
  - `docs/architecture/architecture.md`
  - `docs/architecture/codebase-guide.md`
- Pitfalls:
  - `docs/pitfalls/blazor-maui.md`
  - `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: seed resolution produces a confusing banner if no workload mapping exists for the source entity.
  Mitigation: `IncidentInvestigationSeedResolver` already produces a `PendingAssumptions` list and a
  `ProvenanceSummary`; the banner displays both explicitly. No silent auto-fill.
- Risk: operators click "Investigate" during an incident, land on the workbench, and lose the source
  page context.
  Mitigation: drill-through navigates away but the source page state is preserved via workspace restore;
  back-navigation or a new tab reopens it cleanly.
