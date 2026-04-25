# Status - winui3-pipelines-releases-parity

---

title: "Status - winui3-pipelines-releases-parity"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

Pipelines already has routed native coverage and improved readiness handling, but it is still one of the highest-pressure migration seams. The next step is to split that seam and then restore the deeper MAUI workflows.

**Jira:** not linked

**Current focus:** define the seam reduction and the MAUI-only workflow depth that must come back after Settings and layout foundations land.

## Progress checklist

- [x] MAUI versus WinUI Pipelines gap captured
- [x] Readiness-state gap separated from generic error handling
- [ ] Page and view-model seam reduction plan confirmed
- [ ] Tree/detail and editing parity scope confirmed
- [ ] Readiness-to-settings repair contract confirmed
- [ ] Focused automated validation plan confirmed
- [ ] Docs aligned after implementation begins

## Completed

- Confirmed that Pipelines already has native route coverage and a stronger readiness story than before.
- Identified Pipelines as a high-refactor-pressure slice because one view-model still owns too many workflows.

## Remaining

- Reduce the size and responsibility of the current Pipelines page seam.
- Restore the deeper MAUI workflow surface that still remains narrower in WinUI.
- Add focused validation for both demo-mode and live Azure DevOps scenarios.

## Blockers

- Layout redesign and settings completeness are intended to land first so this page can reuse the final layout and repair surfaces.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- Pipelines remains one of the cutover-critical slices because Azure DevOps readiness is environment-sensitive and operator-visible.
