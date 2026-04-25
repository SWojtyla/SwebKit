# Test Plan - winui3-observability-parity

---

title: "Test Plan - winui3-observability-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the Observability workspace reaches the agreed native parity for richer overview analysis, discovery, readiness, and the already-landed native query workflow while staying clear under both demo-mode and live Azure credential conditions.

## Scope

- In scope: deployment comparison, SLO status, cloud-role or operation pivots, readiness-to-settings repair loop, focused tab-state validation, and regression coverage for the native logs or query baseline
- Out of scope: new analytics capabilities beyond MAUI parity

## Main scenarios (priority)

1. Scenario: missing Azure credentials are survivable. Expected result: Observability shows readiness guidance and opens the correct native Settings repair surface.
2. Scenario: operators can complete the richer overview workflow natively. Expected result: deployment comparison, SLO status, and cloud-role or operation pivots render against the active resource and recorded release anchors.
3. Scenario: the existing native query workflow stays stable while overview parity expands. Expected result: overview changes do not regress discovery, tab activation, saved queries, logs mode state, or the extracted logs-workspace seam.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Existing tests: keep `tests/SwebKit.WinUI.Tests/` green.
- New tests: add focused WinUI coverage for richer overview parity, release-anchor comparison, readiness transitions, and the extracted logs-workspace seam in the native route.

## Test data and setup

- Demo mode covers initial route and tab-state validation.
- Live validation needs Azure sign-in or equivalent credentials plus representative observability resources.

## Manual checks

- Check: readiness repair loop. Steps: trigger missing-credential behavior, open Settings from the readiness action, repair the environment, and reload Observability.
- Check: overview parity. Steps: activate a representative Application Insights resource, confirm cloud-role or operation pivots render, select a recorded release anchor, and verify deployment comparison plus configured SLO status.
- Check: logs regression. Steps: switch between guided and advanced query modes after using the overview panels and confirm the native logs workflow still behaves correctly.

## Regression risks & mitigations

- Risk: richer overview state destabilizes page activation or tab changes. Mitigation: validate discovery, navigation, and overview analysis state separately from the logs workflow.
- Risk: overview parity work hides readiness regressions. Mitigation: keep credential-readiness scenarios as first-class acceptance criteria.

## Acceptance criteria

- The agreed richer overview workflow, including deployment comparison and SLO status, is available natively.
- Readiness repair loops through native Settings successfully.
- `build-winui` and focused WinUI tests stay green.

## Validation status

- Automated: focused Problems checks report no errors in the touched Observability and readiness files, including the new logs-workspace view model. Full `build-winui` validation is blocked by an unrelated compile error in `src/SwebKit.WinUI/ViewModels/Pipelines/PipelinesReleaseWorkspaceViewModel.cs`, and the `runTests` tool returned 0 discovered tests for the focused files in this environment.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
