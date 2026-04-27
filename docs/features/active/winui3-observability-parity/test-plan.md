# Test Plan - winui3-observability-parity

---

title: "Test Plan - winui3-observability-parity"
owner: ""
status: "Done"
created: "2026-04-25"
updated: "2026-04-27"

---

## Goal

Validate that the Observability workspace reaches the agreed native parity slice for richer overview analysis, discovery, readiness, and the already-landed native query workflow, while keeping deeper seam work and live Azure credential validation as explicit future follow-up.

## Scope

- In scope: deployment comparison, SLO status, cloud-role or operation pivots, readiness-to-settings repair loop, focused tab-state validation, regression coverage for the native logs or query baseline, and the compact content-first WinUI page layout for the Observability workspace
- Out of scope: new analytics capabilities beyond MAUI parity
- Out of scope: any second discovery or provider-activation seam split unless a later Observability slice explicitly reopens it

## Main scenarios (priority)

1. Scenario: missing Azure credentials are survivable. Expected result: Observability shows readiness guidance and opens the correct native Settings repair surface.
2. Scenario: operators can complete the richer overview workflow natively. Expected result: deployment comparison, SLO status, and cloud-role or operation pivots render against the active resource and recorded release anchors.
3. Scenario: the existing native query workflow stays stable while overview parity expands. Expected result: overview changes do not regress discovery, tab activation, saved queries, logs mode state, the extracted logs-workspace seam, or the Ctrl+Enter run-query shortcut in advanced mode.

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
- Check: advanced-query shortcut parity. Steps: activate a resource, switch Logs to Advanced mode, press Ctrl+Enter inside the KQL editor, and confirm the current query runs without moving focus to the Run button.
- Check: layout parity. Steps: open the native Observability route and verify the compact context band keeps discovery, provider, and time-range controls visible while the resource list and active analysis tab appear without a tall preamble.

## Regression risks & mitigations

- Risk: richer overview state destabilizes page activation or tab changes. Mitigation: validate discovery, navigation, and overview analysis state separately from the logs workflow.
- Risk: overview parity work hides readiness regressions. Mitigation: keep credential-readiness scenarios as first-class acceptance criteria.

## Acceptance criteria

- The agreed richer overview workflow, including deployment comparison and SLO status, is available natively.
- Readiness repair loops through native Settings successfully.
- Feature-local validation stays clean, and any wider build or live Azure gaps remain explicit follow-up instead of being implied as complete.

## Validation status

- Automated: `build-winui` passed on the current repo state after the advanced-query shortcut and copy-alignment follow-up. No focused test rerun was needed for this page-local WinUI shortcut sweep.
- Manual: Final cutover review can still exercise the shipped native baseline, but no remaining manual check blocks close-out of this slice.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** Richer overview parity and the first seam-reduction slice are complete; further seam work and live Azure validation are future follow-up rather than blockers for this slice.
