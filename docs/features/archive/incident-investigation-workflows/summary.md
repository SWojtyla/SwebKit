# Archive Summary - incident-investigation-workflows

---

title: "Archive Summary - incident-investigation-workflows"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-17"
pr: ""
commit: "sw/dev/timeline"

---

## Goal

Turn Incident Timeline into the shared investigation target for SwebKit so operators can launch an evidence-backed investigation from Observability, Service Bus, or Pipelines without re-entering scope or losing the triggering context.

## Delivered

- **Investigation seed contracts** — `IncidentInvestigationSeed`, `IncidentSeedEvidenceRef`, `IncidentInvestigationDraft` typed models carry the triggering context (time range, evidence references, candidate scope, suggested sources) from source pages to the investigation target.
- **Seed resolver** — `IIncidentInvestigationSeedResolver` / `IncidentInvestigationSeedResolver` normalizes a seed against existing workload mappings, produces a draft scope and pre-selected source toggles, and writes a human-readable provenance summary.
- **Investigation launcher** — `IncidentInvestigationLauncher` (scoped) holds the pending seed and navigates to `/incident-timeline`; replaced on every new launch to eliminate stale state.
- **Landing banner** — `InvestigationSeedBanner` renders source provenance, unresolved scope assumptions, and a Confirm/Dismiss action before any evidence is loaded.
- **Drill-through entry points** — `ObservabilityFailures`, `MessageDetailPane`, and `PipelineDetail` each gained an `Investigate` button that seeds the investigation with source-specific evidence references (ResourceId, EntityPath, PipelineId, etc.).
- **Snapshot export** — `IIncidentSnapshotExporter` / `IncidentSnapshotExporter` builds a sanitized, bounded export bundle from a loaded timeline page. Metadata is filtered to an explicit allow-list; values are truncated at 200 chars; every export includes source coverage and a disclaimer. Exported as JSON or Markdown via a JS blob download.
- **Mapping proposals** — `IIncidentMappingProposalGenerator` / `IncidentMappingProposalGenerator` inspects source statuses after each refresh and generates advisory-only candidate proposals for unmapped or unconfigured sources. Never persists automatically.
- **UI components** — `IncidentSnapshotExportDialog` (format picker, file name preview, confirm) and `MappingProposalPanel` (advisory text, dismiss, Settings handoff).
- **DI registrations** — `IIncidentInvestigationSeedResolver` (singleton), `IIncidentSnapshotExporter` (singleton), `IIncidentMappingProposalGenerator` (singleton), `IncidentInvestigationLauncher` (scoped).
- **JS helper** — `SwebKitUi.downloadTextFile` added to `uiState.js` for blob-based file download in WebView2/MAUI.

## Key decisions

- **Reuse `/incident-timeline` as the investigation target** — avoids splitting the evidence model across multiple pages; all drill-through routes go to the same workbench. (Decision 001)
- **Explicit seed contract over URL-only state** — `IncidentInvestigationSeed` is a typed model passed through an app-layer launcher rather than query string parameters, allowing the payload to evolve without URL brittleness. (Decision 002)
- **`IncidentInvestigationLauncher` registered as `AddScoped`** — `NavigationManager` is scoped in Blazor MAUI; registering the launcher as singleton causes a lifetime mismatch DI error at runtime. (Decision 003)
- **Metadata allow-list in the exporter** — the exporter uses an explicit set of safe keys rather than a deny-list pattern to avoid accidental leakage from newly added metadata keys in signal source adapters.
- **Advisory-only proposals** — mapping proposals are generated from already-loaded evidence only, never trigger queries, and require explicit operator acceptance via Settings before any config change occurs.

## Validation performed

- Unit tests: 49 passing — `IncidentInvestigationSeedResolverTests` (16), `IncidentSnapshotExporterTests` (22), `IncidentMappingProposalGeneratorTests` (11)
- Component tests (SwebKit.App.Tests): deferred — no test changes for launch actions or dialog states; accepted for Wave 1+2 ship.
- E2E drill-through flows: deferred — no new E2E scenarios; accepted for Wave 1+2 ship.
- App build: 0 errors on `SwebKit.App` (net10.0-windows10.0.19041.0).
- Manual: not performed.

## Lessons learned

- In Blazor MAUI, any service that injects `NavigationManager` must be registered as scoped. Registering such a service as singleton causes a captured-scope DI error at runtime — check DI lifetime before registering launchers or navigators.
- An explicit metadata allow-list is safer than a deny-list for export sanitization. Deny-lists silently miss new metadata keys added later; allow-lists reject them by default.
- Proposals that cannot be dismissed visually can feel like committed topology to operators. A dismiss button and explicit "candidate suggestions" wording on `MappingProposalPanel` are both required, not optional.
- When wiring a new overlay component (export dialog), the parent page's `@inject` declaration is not needed if the dialog injects the service itself — removing the unused inject avoids accidental coupling.

## Follow-up

- Component tests for launch actions, seed banner states, and export dialog — deferred; should be addressed before Wave 3 implementation starts.
- E2E flows for Observability → Incident Timeline, Service Bus → Incident Timeline, Pipelines → Incident Timeline — deferred; should be added during Wave 3 prep.
- Wave 3 (watchlists and light automation) — explicitly deferred; requires its own active feature folder before implementation begins.

---

## Drill-through addition (2026-04-18)

**Goal:** Surface page-level "Investigate" buttons on ObservabilityPage, ServiceBusPage, and PipelinesPage so operators can seed an investigation directly from the page context rather than from sub-component pivots.

**Delivered:**

- "Investigate" action on `ObservabilityPage` — seeds from selected resource ID + active time range.
- "Investigate" action on `ServiceBusPage` — seeds from active entity path, optional message ID and correlation ID.
- "Investigate" action on `PipelinesPage` — seeds from pipeline ID, project name, pipeline name.
- 6 targeted tests: 2 bUnit (`ObservabilityPage` seed + no-launch guard, `ServiceBusPage` button hidden with no active tab), 4 pure-logic seed-construction tests.

**Validation:** 6/6 tests passing. Build clean. Manual validation accepted by user (2026-04-18).

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/incident-investigation-workflows/`.
