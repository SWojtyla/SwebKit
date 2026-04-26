# Status - winui3-pipelines-releases-parity

---

title: "Status - winui3-pipelines-releases-parity"
owner: ""
state: "Done"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-26"

---

## Quick summary

Pipelines already has routed native coverage and improved readiness handling, and this slice closes the first seam reduction by moving release-selection, scoped component summaries, and release-tag manager state into a dedicated native workspace seam. Deeper MAUI workflow restoration, approval-action state reduction, and live Azure DevOps validation are explicit future follow-up rather than blockers for this archived slice.

**Jira:** not linked

**Current focus:** no immediate feature-owned implementation work; keep the shipped seam-reduction, compact layout, and readiness baseline stable while future follow-up decides whether deeper workflow restoration still justifies a separate slice.

## Progress checklist

- [x] MAUI versus WinUI Pipelines gap captured
- [x] Readiness-state gap separated from generic error handling
- [x] Page and view-model seam reduction plan confirmed
- [x] Remaining tree/detail, approval, and editing parity explicitly moved to future follow-up
- [x] Readiness-to-settings repair contract confirmed
- [x] Focused automated validation plan confirmed
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that Pipelines already has native route coverage and a stronger readiness story than before.
- Identified Pipelines as a high-refactor-pressure slice because one view-model still owns too many workflows.
- Extracted a dedicated WinUI Releases workspace seam so release selection, scoped component summaries, and release-tag manager state are coordinated outside `PipelinesPageViewModel`.
- Kept readiness-state routing and the native `Open Settings` repair path in the page coordinator while the Releases seam moved out.
- Added focused WinUI coverage for the extracted release workspace and kept the existing readiness refresh-gate coverage green.
- Moved the top-level readiness, scope, and summary card into the shared compact scaffold context band so the project tree and workspace detail panes reach the viewport earlier.

## Remaining

- No blocking remaining work inside this feature folder.
- Final cutover review can still exercise the shipped native baseline, but it is no longer feature-local archive debt.
- Remaining approval-action seam reduction, deeper tree/detail and editing parity, and live Azure DevOps validation remain explicit future Pipelines follow-up.

## Close-out checklist

- [x] Accept the shipped Releases workspace seam and readiness contract as sufficient closure for this slice.
- [x] Move deeper tree/detail, approval, editing, and live Azure DevOps validation into explicit future follow-up.
- [x] Promote the feature to `Done` and remove feature-local remaining work.

## Blockers

- No planning blocker remains. Live Azure DevOps validation is still environment-sensitive and should stay separate from the route-owned implementation work.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passed again after the compact layout follow-up, and the touched Pipelines page remained diagnostics-clean. Remaining live Azure DevOps validation is future follow-up rather than a blocker for this archived slice.

## Notes

- Pipelines remains one of the cutover-critical slices because Azure DevOps readiness is environment-sensitive and operator-visible.
- The first seam-reduction slice is now the extracted `PipelinesReleaseWorkspaceViewModel`; approval workflows still remain on the page coordinator and are the next bounded pressure point.
