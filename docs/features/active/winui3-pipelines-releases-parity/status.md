# Status - winui3-pipelines-releases-parity

---

title: "Status - winui3-pipelines-releases-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

Pipelines already has routed native coverage and improved readiness handling, but it is still one of the highest-pressure migration seams. The next step is to split that seam and then restore the deeper MAUI workflows.

**Jira:** not linked

**Current focus:** finish the next Pipelines seam reductions now that the native Releases workspace and tag-manager state are no longer trapped inside the page-level coordinator.

## Progress checklist

- [x] MAUI versus WinUI Pipelines gap captured
- [x] Readiness-state gap separated from generic error handling
- [x] Page and view-model seam reduction plan confirmed
- [ ] Tree/detail and editing parity scope confirmed
- [x] Readiness-to-settings repair contract confirmed
- [x] Focused automated validation plan confirmed
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that Pipelines already has native route coverage and a stronger readiness story than before.
- Identified Pipelines as a high-refactor-pressure slice because one view-model still owns too many workflows.
- Extracted a dedicated WinUI Releases workspace seam so release selection, scoped component summaries, and release-tag manager state are coordinated outside `PipelinesPageViewModel`.
- Kept readiness-state routing and the native `Open Settings` repair path in the page coordinator while the Releases seam moved out.
- Added focused WinUI coverage for the extracted release workspace and kept the existing readiness refresh-gate coverage green.

## Remaining

- Reduce the size and responsibility of the remaining Pipelines page seam, especially approval-action state and deeper pipeline tree/detail parity.
- Restore the deeper MAUI workflow surface that still remains narrower in WinUI.
- Add focused validation for both demo-mode and live Azure DevOps scenarios.

## Blockers

- No planning blocker remains. Live Azure DevOps validation is still environment-sensitive and should stay separate from the route-owned implementation work.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passed; focused WinUI release-workspace and readiness tests passed.

## Notes

- Pipelines remains one of the cutover-critical slices because Azure DevOps readiness is environment-sensitive and operator-visible.
- The first seam-reduction slice is now the extracted `PipelinesReleaseWorkspaceViewModel`; approval workflows still remain on the page coordinator and are the next bounded pressure point.
