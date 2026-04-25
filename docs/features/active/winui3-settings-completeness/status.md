# Status - winui3-settings-completeness

---

title: "Status - winui3-settings-completeness"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The settings gap is now isolated as its own feature. The next step is to reuse the layout-redesign primitives to rebuild the native Settings page as the repair path for every in-scope operator domain.

**Jira:** not linked

**Current focus:** define the missing native settings sections and the deep-link/readiness contract each downstream route expects.

## Progress checklist

- [x] MAUI versus WinUI settings gap documented
- [ ] Native section list confirmed for all in-scope domains
- [ ] Validation and readiness handoff defined per section
- [ ] Persistence and credential flows verified against current services
- [ ] Downstream feature dependencies updated after implementation starts
- [ ] Tests and docs aligned

## Completed

- Confirmed that the current WinUI Settings page is materially narrower than the MAUI Settings page.
- Identified Settings as a cutover-critical dependency rather than a cosmetic follow-up.
- Linked this feature to all downstream parity slices that depend on live configuration repair.

## Remaining

- Rebuild Settings with native sections for every in-scope domain.
- Add per-section validation and repair guidance.
- Ensure Pipelines and Observability readiness actions deep-link into the right section.

## Blockers

- Layout redesign is the intended first dependency because the current Settings frame should adopt the shared layout primitives instead of locking in another one-off page structure.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- Incident Timeline stays explicitly deferred unless a separate migration feature is created for it.
