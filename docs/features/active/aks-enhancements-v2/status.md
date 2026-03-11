# Status - AKS Enhancements v2

---

title: "Status - AKS Enhancements v2"
owner: ""
state: "Planned"
branch: "sw/main/aks"
started: "2026-03-11"
last_updated: "2026-03-11"

---

## Quick summary

Second enhancement phase for AKS: UX improvements (resizable panels, context menus), mutative operations (restart, kill, rollback), and Helm release inspection.

**Current focus:** Planning and design.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend implementation
- [ ] Frontend implementation
- [ ] Tests
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scope defined and module docs created.

## Remaining

- All implementation work.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Mutative operations require production guard (confirmation dialogs).
- Context menus must be custom HTML/CSS (not browser native) for MAUI BlazorWebView compatibility.
