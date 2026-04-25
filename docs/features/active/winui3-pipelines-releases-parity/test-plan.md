# Test Plan - winui3-pipelines-releases-parity

---

title: "Test Plan - winui3-pipelines-releases-parity"
owner: ""
status: "Not started"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the Pipelines and Releases workspace reaches the agreed WinUI parity while staying maintainable, testable, and clear under both demo-mode and live Azure DevOps conditions.

## Scope

- In scope: page seam reduction, workflow-depth parity, readiness-to-settings repair loop, focused view-model coverage
- Out of scope: Azure DevOps backend redesign and unrelated product features

## Main scenarios (priority)

1. Scenario: invalid or missing Azure DevOps configuration is survivable. Expected result: the page shows the readiness state and opens the correct native Settings repair surface.
2. Scenario: the deeper Pipelines and Releases workflows are available natively. Expected result: the agreed tree/detail, approval, history, and tag-management paths no longer require MAUI.
3. Scenario: the refactored page seam is testable. Expected result: release selection, scoped component summaries, and release-tag flows are isolated behind a dedicated native Releases workspace seam instead of remaining trapped in the page coordinator.

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

## Regression risks & mitigations

- Risk: more workflow parity expands the same oversized view-model. Mitigation: require seam reduction before or alongside deeper feature restoration.
- Risk: live Azure DevOps validation stays flaky. Mitigation: keep demo-mode and live validation separate and explicit.

## Acceptance criteria

- The agreed Pipelines and Releases workflows are available natively.
- Readiness repair loops through native Settings successfully.
- `build-winui`, `tests/SwebKit.DevOps.Tests/`, and focused WinUI tests stay green.

## Validation status

- Automated: `build-winui` passed; `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj -c Release --filter "FullyQualifiedName~PipelinesReleaseWorkspaceViewModelTests|FullyQualifiedName~ReadinessStateViewModelTests"` passed.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
