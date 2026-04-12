# Test Plan - observability-explainer-and-reliability

---

title: "Test Plan - observability-explainer-and-reliability"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that the Observability page can explain telemetry changes more directly through dependency health, dimension pivots, deployment comparison, and SLO tracking while preserving the current detail and query workflows.

## Scope

- In scope: explanation cards, dependency health, custom-dimension pivots, deployment before-and-after comparison, SLO tracking, and drill-through into Logs or Incident Timeline.
- Out of scope: automated incident declaration, unrestricted dashboard building, and external alert-delivery pipelines.

## Main scenarios (priority)

1. Scenario: a selected resource has a recent failure spike. Expected result: the explainer summary highlights the change and links to the supporting failure or logs view.
2. Scenario: a dependency is degrading while top-level request volume remains stable. Expected result: dependency health shows the unhealthy dependency and its latency or failure characteristics.
3. Scenario: failures are concentrated in a custom dimension such as tenant, route, queue, or cloud role. Expected result: the dimension pivot surfaces the top contributors and their relative weight.
4. Scenario: a deployment or release snapshot is available in the selected time range. Expected result: before-and-after comparison shows the selected deployment anchor and telemetry deltas.
5. Scenario: no deployment anchor exists. Expected result: deployment comparison remains unavailable or asks for an explicit anchor instead of guessing.
6. Scenario: SLO definitions exist for failure rate, latency, or availability. Expected result: SLO status shows current attainment, target, and risk or burn summary transparently.
7. Scenario: dimension or dependency query hits configured caps. Expected result: truncation is explicit and drill-through remains available.
8. Scenario: explanation card launches an investigation. Expected result: Incident Timeline receives bounded source context without implying causation.
9. Scenario: resource selection changes or time range changes rapidly. Expected result: stale explainer results are ignored consistently with current Observability behavior.
10. Scenario: wording audit across explanation cards and SLO states. Expected result: the page avoids likely cause or root-cause claims.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Extend `ObservabilityPageTests`, `ObservabilityFailuresTabTests`, `ObservabilityPerformanceTabTests`, and `ObservabilityLogsGuidedModeTests` where drill-through or explainer links affect current behavior.
- Add focused tests for new explainer, dependency, comparison, and SLO components.
- Unit tests: `tests/SwebKit.Core.Tests`
- Add tests for explainer summarization, SLO evaluation, deployment-window calculation, and dimension-pivot normalization.
- Integration tests: `tests/SwebKit.Core.Tests/DemoObservabilityProviderTests.cs` plus any provider-level additions, and `tests/SwebKit.DevOps.Tests` where deployment anchors depend on live client behavior.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Add a focused Observability explanation smoke path after the UI settles.

## Test data and setup

- Telemetry fixtures with dependency failures, stable and degraded latency, and dimension-heavy failure spikes.
- Release or deployment fixtures that can anchor before-and-after comparisons.
- SLO fixtures with meeting, near-breaching, and breached states.
- Truncation fixtures for high-cardinality dimension results.

## Manual checks

- Check: explanation-first overview. Steps: open `/observability`, pick a resource and time range, and verify the page highlights what changed with a clear route to the underlying detail.
- Check: dependency health. Steps: inspect a resource with dependency failures and verify the health summary explains what is measured.
- Check: deployment comparison. Steps: choose a deployment anchor and verify the before-and-after window and deltas are explicit.
- Check: SLO transparency. Steps: review a configured SLO and verify target, current state, and any burn summary are understandable.
- Check: incident handoff. Steps: launch an investigation from an explanation card and verify the bounded context is preserved.

## Regression risks & mitigations

- Risk: explanation layers obscure the underlying data path. Mitigation: every explanation card links to the source query or detail tab.
- Risk: cost grows due to wide dependency or dimension queries. Mitigation: cap results and assert truncation behavior.
- Risk: deployment comparison anchors to the wrong change. Mitigation: require explicit anchor selection and test missing-anchor behavior.
- Risk: SLO math becomes opaque. Mitigation: keep formulas simple and config-driven, and cover them with unit tests.

## Acceptance criteria

- Operators can identify major telemetry shifts, unhealthy dependencies, key dimensions, deployment deltas, and SLO state faster than in the current query-first flow.
- Explanation copy remains grounded and non-causal.
- Logs and detail tabs remain available as first-class escape hatches.
- Tests and docs stay aligned with the implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
