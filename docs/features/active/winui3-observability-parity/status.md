# Status - winui3-observability-parity

---

title: "Status - winui3-observability-parity"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

Observability already has native discovery, tabs, and improved readiness handling, but it still lacks the richer MAUI analysis workflow and a safe seam for the missing query-editor path.

**Jira:** not linked

**Current focus:** define the seam reduction and the editor or chart parity work that should follow layout and settings completion.

## Progress checklist

- [x] MAUI versus WinUI Observability gap captured
- [x] Readiness-state gap separated from generic error handling
- [ ] Page and view-model seam reduction plan confirmed
- [ ] Query-editor and broader chart parity scope confirmed
- [ ] Readiness-to-settings repair contract confirmed
- [ ] Focused automated validation plan confirmed
- [ ] Docs aligned after implementation begins

## Completed

- Confirmed that Observability already has native routing, discovery, and a stronger readiness story than before.
- Identified Observability as a high-refactor-pressure slice because discovery, tabs, and future editor state still accumulate in one page seam.

## Remaining

- Reduce the current Observability page seam.
- Restore the agreed MAUI editor and analysis workflows.
- Add focused validation for readiness, discovery, tabs, and editor flows.

## Blockers

- Layout redesign and settings completeness are intended to land first so the page can reuse the final layout and repair surfaces.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- Observability remains cutover-critical because credential-readiness and deeper operator workflows are both still in play.
