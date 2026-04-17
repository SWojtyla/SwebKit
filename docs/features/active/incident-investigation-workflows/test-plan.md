# Test Plan - incident-investigation-workflows

---

title: "Test Plan - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that SwebKit can launch an evidence-backed investigation from existing source pages into Incident Timeline, preserve the triggering context accurately, and export a bounded incident snapshot without implying root cause or silently persisting inferred mappings.

## Scope

- In scope: drill-through from Observability, Service Bus, and Pipelines; investigation seed normalization; correlation-ID passthrough; landing-banner behavior; snapshot export; mapping proposals; dependency-observation groundwork; Settings handoff.
- Out of scope: background watchlists, automation-triggered investigations, remediation actions, and any root-cause inference features.

## Main scenarios (priority)

1. Scenario: launch from an Observability failure row with a selected time range. Expected result: `/incident-timeline` opens with a draft investigation seed that preserves the time window, source provenance, and any explicit workload mapping already available.
2. Scenario: launch from an Observability logs query that includes `operation_Id` or another correlation pivot. Expected result: the correlation identifier is carried into the draft seed and shown as supporting context, not as a proof of causation.
3. Scenario: launch from a Service Bus dead-letter message detail. Expected result: the target entity, message identifiers, and DLQ context are preserved and the landing page explains whether workload mapping exists or still requires confirmation.
4. Scenario: launch from a Pipelines run or deployment record. Expected result: the selected run, deployment window, and mapped environment are preserved and surfaced as contextual evidence only.
5. Scenario: a seed has incomplete workload mapping. Expected result: Incident Timeline shows the triggering evidence plus a focused mapping or settings handoff; it does not silently broaden the query.
6. Scenario: incident snapshot export from a partially degraded timeline result. Expected result: export includes current items, source coverage, truncation flags, and a redaction marker for omitted fields.
7. Scenario: mapping discovery finds a likely Observability or Service Bus binding. Expected result: the proposal is rendered with explanation text and remains non-persistent until the user explicitly accepts it through Settings or a review action.
8. Scenario: dependency observations exist but are low-confidence. Expected result: they are marked as candidate-only and do not change Incident Timeline inclusion rules.
9. Scenario: rapid repeat launches from different source pages. Expected result: only the latest launch seed is applied and stale state is not left behind.
10. Scenario: wording audit across launch banners, export metadata, and proposal panels. Expected result: the UI remains evidence-first and does not use root cause, culprit, likely cause, or inferred dependency language as a fact claim.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Add or extend coverage around source-page launch actions, landing-banner rendering, stale-seed replacement, export dialog states, and Settings handoff.
- Likely affected suites: `ObservabilityPageTests`, `ServiceBusPageTests`, `IncidentTimelinePageTests`, plus new focused tests for the investigation launcher or export dialog.
- Unit tests: `tests/SwebKit.Core.Tests`
- Add coverage for investigation-seed normalization, snapshot redaction, proposal-only persistence boundaries, dependency-observation ranking, and correlation-ID passthrough rules.
- Integration tests: `tests/SwebKit.Azure.Tests`, `tests/SwebKit.DevOps.Tests`, and existing incident-timeline adapter suites where needed.
- Validate that source-specific evidence references normalize cleanly into the new seed contracts without breaking `ServiceBusEvidenceSignalSourceTests`, `DevOpsReleaseTimelineSignalSourceTests`, or `AppInsightsTimelineSignalSourceTests`.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Add focused flows for Observability to Incident Timeline, Service Bus to Incident Timeline, and Pipelines to Incident Timeline.

## Test data and setup

- Deterministic fixture windows around incident events so source-page launches can assert exact seed timestamps.
- Example message fixtures with `CorrelationId`, `SessionId`, `operation_Id`, DLQ reason, and expiry metadata.
- Example release or deployment snapshots that anchor before-and-after investigation windows.
- Redaction fixtures containing large payloads, PII-like test values, and missing mappings to verify export hygiene.

## Manual checks

- Check: Observability launch fidelity. Steps: open `/observability`, select a resource and time range, trigger Investigate from a failure or logs pivot, then verify the landing banner on `/incident-timeline` reflects the same source and window.
- Check: Service Bus launch fidelity. Steps: open `/service-bus`, inspect a DLQ message, launch the investigation flow, and verify entity path, correlation ID, and unmapped guidance behave as planned.
- Check: Pipelines launch fidelity. Steps: open `/pipelines`, pick a run or deployment record, launch into Incident Timeline, and verify the deployment evidence is contextual and not phrased as a root cause.
- Check: snapshot export hygiene. Steps: export a timeline result with partial coverage and confirm the bundle includes coverage states, truncation markers, and redacted fields where required.
- Check: proposal-only safety. Steps: trigger a mapping proposal and verify no config change is written until the operator explicitly accepts the proposal.

## Regression risks & mitigations

- Risk: drill-through changes destabilize existing source-page interactions. Mitigation: keep launch actions additive and cover them with focused component tests.
- Risk: seed launch bypasses the manual-refresh model of `incident-timeline-workbench`. Mitigation: landing behavior should prefill draft scope and explain what is seeded before execution.
- Risk: snapshot export grows into a raw data dump. Mitigation: cap item counts, redact payloads, and assert redaction in tests.
- Risk: proposals silently mutate settings. Mitigation: test repository persistence boundaries and require an explicit accept path.

## Acceptance criteria

- All high-priority launch scenarios preserve source provenance and bounded time context.
- The landing experience stays evidence-first and does not imply that the investigation is pre-solved.
- Snapshot export is bounded, sanitized, and explicit about partial coverage.
- Mapping and dependency suggestions remain proposals until explicitly accepted.
- Tests and feature docs are updated together.

## Validation status

- Automated (unit): 49 unit tests passing — `IncidentInvestigationSeedResolverTests` (16), `IncidentSnapshotExporterTests` (22), `IncidentMappingProposalGeneratorTests` (11)
- Automated (component): Not started — deferred. No component test changes in SwebKit.App.Tests for the new launch actions or dialog states. Deferral accepted for Wave 1+2 ship; must be addressed before Wave 3.
- Automated (E2E): Not started — deferred. No E2E flows for drill-through paths. Accepted for Wave 1+2 ship.
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
