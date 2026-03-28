# Test Plan - incident-timeline-workbench

---

title: "Test Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Validate that the Incident Timeline Workbench produces a correct, time-ordered, and responsive cross-source incident view under normal, partial-failure, and rapid-refresh conditions.

## Scope

- In scope: normalized timeline ordering, cross-source correlation, cancellation correctness, partial-failure handling, page usability, and regression safety for existing pages.
- Out of scope: remediation actions from timeline rows, long-term timeline persistence, and infrastructure deployment validation.

## Main scenarios (priority)

1. Scenario: Unified timeline load for Last 1 hour across all sources - Expected result: timeline renders with events sorted by UTC timestamp descending and source badges for Observability, AKS, Service Bus, and DevOps.
2. Scenario: Filter to one source (AKS only) - Expected result: only AKS events/restarts remain while global time range and cursor behavior remain stable.
3. Scenario: App Insights failure burst overlaps with release trigger - Expected result: correlated ordering is preserved and release trigger appears in the same timeline window.
4. Scenario: Service Bus DLQ activity with no App Insights failures - Expected result: timeline still renders with DLQ events and clear absence indicators for other sources.
5. Scenario: One source timeout/failure - Expected result: page renders partial data with source-level error status, no global crash.
6. Scenario: Rapid time range changes (1h -> 6h -> 24h) - Expected result: previous requests are canceled, stale responses are discarded, final range wins.
7. Scenario: Manual refresh spam while auto-refresh is enabled - Expected result: at most one active fetch per page instance; no duplicate overlapping loads.
8. Scenario: Empty window (no events in all sources) - Expected result: empty state renders with explanatory copy and no error styling.
9. Scenario: Large result set in 24h window - Expected result: item capping and cursor/paging behavior prevent UI lockups.
10. Scenario: Navigation away during load and return - Expected result: no background exception leaks and no updates to disposed components.

## Automated coverage

- Component tests: tests/SwebKit.App.Tests
- Validate timeline toolbar filters, loading/empty/error states, and partial-data banners.
- Validate row rendering, source badges, and detail panel drill-in behavior.
- Unit tests: tests/SwebKit.Core.Tests
- Validate timeline normalization, merge ordering, deduplication, source health aggregation, and cancellation token propagation.
- Integration tests: tests/SwebKit.Azure.Tests, tests/SwebKit.Kubernetes.Tests, tests/SwebKit.DevOps.Tests
- Validate adapter mappings for DLQ metrics, AKS event/restart shaping, and release trigger mapping.
- End-to-end tests: tests/SwebKit.E2E.Tests
- Validate incident triage flow from page load to filtered investigation and refresh behavior.
- CI gates: all newly added tests pass; no regressions in existing test suites.

## Test data and setup

- Deterministic fixture timestamps in UTC to avoid timezone-flaky ordering assertions.
- Fake provider responses with mixed density (sparse, bursty, and empty).
- Fault-injection fixtures for source timeout, 401/403 auth failure, and transient network failure.
- Cancellation test harness that issues overlapping refresh requests with linked CancellationTokenSource instances.

## Manual checks

- Check: Incident timeline baseline usability - steps
- Open Incident Timeline page, select a profile with all four source configs, load Last 1 hour, verify row order and source chips.
- Check: Partial source outage visibility - steps
- Simulate one provider failure, refresh, verify degraded-source callout and remaining events continue to render.
- Check: Cancellation and responsiveness - steps
- Trigger rapid range changes and refresh clicks, verify no stale rows flash and page remains responsive.
- Check: Navigation cleanup - steps
- Start load, navigate to another page, return, verify no stale error toasts or disposed component updates.

## Regression risks & mitigations

- Risk: Shared client abstractions regress existing feature pages.
- Mitigation: additive contracts only; run affected project test suites before merge.
- Risk: Blazor render thrash under high-volume updates.
- Mitigation: throttled render batching and component-level performance assertions.
- Risk: Cancellation swallowed by broad exception handling.
- Mitigation: explicit OperationCanceledException passthrough tests and code review checklist.
- Risk: Azure SDK paging/listing assumptions differ per source.
- Mitigation: adapter contract tests with realistic mocked SDK responses and edge-case fixtures.

## Acceptance criteria

- All priority scenarios pass in automated or manual validation.
- No source failure causes full-page failure; degraded state is explicit.
- Cancellation behavior is deterministic: last request wins.
- Performance target: first paint for Last 24 hours completes in under 3 seconds for representative fixture volumes on dev hardware.
- Tests and feature docs are updated together with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
