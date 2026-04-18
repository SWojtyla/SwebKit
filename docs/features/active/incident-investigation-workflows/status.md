# Status - incident-investigation-workflows

---

title: "Status - incident-investigation-workflows"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-17"
last_updated: "2026-04-17"

---

## Quick summary

Backend contracts, launcher, seed resolver, and landing banner are all in place from the
`incident-timeline-workbench` base. Current work is the source-page drill-through slice:
adding "Investigate" actions to ObservabilityPage, ServiceBusPage, and PipelinesPage.

Jira: not linked

Current focus: manual validation on a real environment to confirm the seed banner prefills
the correct scope and provenance from each source area.

## Progress checklist

### Contracts and infrastructure

- [x] `IncidentInvestigationSeed` and `IncidentSeedEvidenceRef` in `IncidentTimelineModels.cs`
- [x] `IncidentInvestigationLauncher` in `src/SwebKit.App/Services/`
- [x] `IIncidentInvestigationSeedResolver` and `IncidentInvestigationSeedResolver` in `SwebKit.Core`
- [x] `InvestigationSeedBanner` component in `src/SwebKit.App/Components/IncidentTimeline/`
- [x] Seed consume and draft resolve wired in `IncidentTimelinePage.razor`

### Source-page drill-through

- [x] "Investigate" action on ObservabilityPage
- [x] "Investigate" action on ServiceBusPage
- [x] "Investigate" action on PipelinesPage

### Tests

- [x] Seed construction unit tests for Observability area (bUnit + launcher assertion)
- [x] Seed construction unit tests for ServiceBus area (pure logic)
- [x] Seed construction unit tests for Pipelines area (pure logic)
- [x] bUnit coverage for Investigate button visibility on ObservabilityPage (enabled with provider)
- [x] bUnit coverage for Investigate button on ServiceBusPage (hidden with no active tab)
- [x] PipelinesPage button visibility: covered by pure-logic seed-construction tests; full bUnit rendering requires SwebKit.DevOps project reference not present in App.Tests — accepted as manual check
- [ ] Manual validation on a real environment

- [x] Feature folder created
- [x] `codebase-guide.md` already covers launcher and seed resolver entry points

## Completed

- Established the feature folder and aligned scope with the existing `incident-timeline-workbench` base.
- Added "Investigate" action button to ObservabilityPage (uses selected resource ID + active time range).
- Added "Investigate" action button to ServiceBusPage (uses active entity path, optional message ID and correlation ID).
- Added "Investigate" action button to PipelinesPage (uses pipeline ID, project name, pipeline name).
- Added 6 targeted tests: 2 bUnit (ObservabilityPage seed + no-launch guard, ServiceBusPage button hidden with no tab), 4 pure-logic seed construction tests.

## Remaining

- Manual validation on a real environment: confirm seed banner prefills correct scope and provenance from each source area.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: contracts validated as part of `incident-timeline-workbench`; drill-through not yet validated.

## Notes

- Seed must never carry message bodies, payloads, or secret values — only safe identifiers (IDs, paths, names).
- `InvestigationSeedBanner` always requires operator confirmation before the query runs; auto-confirm only
  triggers when `PendingAssumptions` is empty.
