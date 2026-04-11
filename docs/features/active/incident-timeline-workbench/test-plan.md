# Test Plan - incident-timeline-workbench

---

title: "Test Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-03-28"
updated: "2026-04-11"

---

## Goal

Validate that the Incident Timeline Workbench produces a correct, time-ordered, and responsive workload-scoped evidence view for one namespace and one incident window, without making causal claims.

## Scope

- In scope: workload scope filtering, evidence ordering, inclusion-rule correctness, explanation labels, cancellation correctness, partial coverage handling, page usability, and regression safety for existing pages.
- Out of scope: remediation actions, root-cause detection, long-term incident persistence, deep paging, auto-refresh, and infrastructure deployment validation.

## Main scenarios (priority)

1. Scenario: Pod-down investigation in namespace `prd-phonotif` for the selected workload - Expected result: AKS evidence anchors the timeline and related evidence from other mapped sources appears in UTC timestamp order.
2. Scenario: AKS-only view for the same workload - Expected result: only AKS evidence remains while scope summary and time-window behavior stay stable.
3. Scenario: App Insights failures inside the incident window with explicit workload mapping - Expected result: failures appear with Direct or Corroborating explanations.
4. Scenario: App Insights failures exist but no workload mapping or correlation ID exists - Expected result: those records are not included and coverage indicates unmapped or unavailable evidence.
5. Scenario: Service Bus symptoms on a mapped queue or topic - Expected result: symptoms appear with an explanation based on topology mapping or existing correlation ID.
6. Scenario: Recent deployment or release activity targeted the same namespace during the incident window - Expected result: the deployment item appears as contextual evidence, not as a cause claim.
7. Scenario: One source timeout or auth failure - Expected result: page renders partial data with source-level coverage state and no global crash.
8. Scenario: Rapid scope or time-window changes during manual refresh - Expected result: previous requests are canceled, stale responses are discarded, and the final request wins.
9. Scenario: Empty scoped window - Expected result: empty state renders with explanatory copy and no error styling.
10. Scenario: Result cap reached in a 6 hour window - Expected result: truncation messaging is shown and the UI remains responsive.
11. Scenario: Navigation away during load and return - Expected result: no background exception leaks and no updates to disposed components.
12. Scenario: UI wording review - Expected result: the page does not show root cause, culprit, or likely cause language.

## Automated coverage

- Component tests: tests/SwebKit.App.Tests
- Validate scope toolbar behavior, loading or empty or error states, coverage strip rendering, truncation messaging, and detail-panel explanation text.
- Validate row rendering, source badges, severity badges, and relevance labels.
- Unit tests: tests/SwebKit.Core.Tests
- Validate workload scope normalization, merge ordering, inclusion-rule evaluation, link-reason generation, source coverage aggregation, truncation, and cancellation token propagation.
- Integration tests: tests/SwebKit.Azure.Tests, tests/SwebKit.Kubernetes.Tests, tests/SwebKit.DevOps.Tests
- Validate adapter mappings for AKS workload evidence, App Insights workload mapping, Service Bus topology mapping, and deployment or release mapping.
- End-to-end tests: tests/SwebKit.E2E.Tests
- Validate the `prd-phonotif` investigation flow from page load to scoped evidence review and manual refresh behavior.
- CI gates: all newly added tests pass and no regressions appear in existing suites.

## Test data and setup

- Deterministic fixture timestamps in UTC to avoid timezone-flaky ordering assertions.
- Workload mapping fixtures that cover explicit mapping, correlation-ID-supported mapping, and unmapped cases.
- Fake provider responses with sparse, bursty, empty, partial-failure, and truncation-sized data sets.
- Fault-injection fixtures for source timeout, 401 or 403 auth failure, and transient network failure.
- Cancellation test harness that issues overlapping scope or window changes with linked CancellationTokenSource instances.

## Manual checks

- Check: Baseline cockpit usability - steps
- Open Incident Timeline, select the target profile, namespace `prd-phonotif`, workload, and Last 1 hour, then verify row order, scope summary, source chips, and explanation labels.
- Check: Unmapped source visibility - steps
- Use a scope with no App Insights or Service Bus mapping, refresh, and verify the coverage strip explains unmapped sources rather than silently omitting them.
- Check: Partial source outage visibility - steps
- Simulate one provider failure, refresh, and verify degraded-source messaging while remaining evidence continues to render.
- Check: Cancellation and responsiveness - steps
- Trigger rapid scope or range changes and refresh clicks, then verify no stale rows flash and the page remains responsive.
- Check: Terminology audit - steps
- Review timeline rows, detail panel, empty state, and coverage strip text to confirm the UI stays evidence-first and avoids causal wording.

## Regression risks & mitigations

- Risk: Shared client abstractions regress existing feature pages.
- Mitigation: additive contracts only; run affected project test suites before merge.
- Risk: UI wording drifts back toward correlation or root-cause claims.
- Mitigation: add copy assertions in component tests and include wording review in manual validation.
- Risk: Cancellation is swallowed by broad exception handling.
- Mitigation: explicit OperationCanceledException passthrough tests and code review checklist.
- Risk: Source mappings are applied too broadly and include unrelated workload evidence.
- Mitigation: unit and integration tests that prove unmapped evidence is excluded.

## Acceptance criteria

- All priority scenarios pass in automated or manual validation.
- Every included item can explain why it is present.
- No item is included solely because of vague cross-source correlation language.
- No source failure causes a full-page failure unless every selected source fails.
- Cancellation behavior is deterministic: last request wins.
- Performance target: first paint for Last 6 hours completes in under 3 seconds for representative fixture volumes on development hardware.
- Tests and feature docs are updated together with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
