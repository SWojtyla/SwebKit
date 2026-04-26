# Test Plan - winui3-pipelines-releases-parity

---

title: "Test Plan - winui3-pipelines-releases-parity"
owner: ""
status: "Done"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Validate that the Pipelines and Releases workspace reaches the delivered native seam-reduction and readiness slice while keeping deeper workflow restoration and live Azure DevOps verification explicit as future follow-up.

## Scope

- In scope: page seam reduction, readiness-to-settings repair loop, focused view-model coverage for the extracted Releases workspace seam, and the compact content-first WinUI page layout for the Pipelines workspace
- Out of scope: Azure DevOps backend redesign and unrelated product features
- Out of scope: deeper tree/detail and editing parity, broader approval workflows, and live Azure DevOps verification beyond the shipped readiness baseline

## Main scenarios (priority)

1. Scenario: invalid or missing Azure DevOps configuration is survivable. Expected result: the page shows the readiness state and opens the correct native Settings repair surface.
2. Scenario: the delivered Releases workspace seam is available natively. Expected result: release selection, scoped component summaries, and release-tag flows are isolated behind a dedicated native workspace seam instead of remaining trapped in the page coordinator.
3. Scenario: readiness repair remains native. Expected result: invalid or missing Azure DevOps configuration still opens the correct Settings repair surface.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Existing tests: keep `tests/SwebKit.DevOps.Tests/` and `tests/SwebKit.WinUI.Tests/` green.
- New tests: keep focused WinUI coverage on the extracted Releases workspace seam and any validation logic restored from MAUI.
- Focused slice: `PipelinesReleaseWorkspaceViewModelTests` covers preferred release selection and release-tag confirmation, while `ReadinessStateViewModelTests` continues to assert the readiness-driven refresh gate and Settings repair loop.

## Test data and setup

- Demo mode covers initial navigation and empty-state behavior.
- Live validation needs a working Azure DevOps organization, PAT path, and representative projects or releases.

## Manual checks

- Check: readiness repair loop. Steps: trigger an invalid DevOps configuration, open Settings from the readiness action, repair the config, and reload the page.
- Check: workflow-depth parity. Steps: exercise the agreed Pipelines and Releases flows and confirm they match the planned native surface.
- Check: layout parity. Steps: open the native Pipelines route and verify the compact context band keeps readiness, scope, and summary state visible while the project tree and active workspace start without a tall top-of-page card.

## Regression risks & mitigations

- Risk: more workflow parity expands the same oversized view-model. Mitigation: require seam reduction before or alongside deeper feature restoration.
- Risk: live Azure DevOps validation stays flaky. Mitigation: keep demo-mode and live validation separate and explicit.

## Acceptance criteria

- The shipped Releases workspace seam and readiness repair loop are available natively.
- Readiness repair loops through native Settings successfully.
- `build-winui`, `tests/SwebKit.DevOps.Tests/`, and focused WinUI tests stay green.
- Deeper Pipelines workflow restoration and live Azure DevOps validation stay explicit as future follow-up instead of being implied by this closed slice.

## Validation status

- Automated: `build-winui` passed again after the compact layout follow-up. The earlier focused WinUI release-workspace and readiness test pass remains the latest domain-level test evidence for this slice.
- Manual: Final cutover review can still exercise the shipped native baseline, but no remaining manual check blocks close-out of this slice.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** The first native Pipelines seam-reduction slice is complete; deeper workflow restoration and live Azure DevOps validation are future follow-up rather than blockers for this slice.
