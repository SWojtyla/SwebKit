# Status - winui3-service-bus-parity

---

title: "Status - winui3-service-bus-parity"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

Service Bus baseline coverage already exists in WinUI. The remaining work is deeper workflow parity and safety hardening after layout and settings dependencies land.

**Jira:** not linked

**Current focus:** translate the remaining MAUI-only Service Bus workflows into explicit native work items instead of leaving them inside the cutover umbrella.

## Progress checklist

- [x] MAUI versus WinUI gap captured
- [ ] Scheduled message workflow scope confirmed
- [ ] Template and advanced filter parity designed
- [ ] Bulk-operation safety and workspace restore hardening planned
- [ ] Focused test strategy defined for the new flows
- [ ] Docs aligned after implementation begins

## Completed

- Confirmed that Service Bus no longer blocks the native-route baseline.
- Isolated the remaining parity debt as workflow depth rather than missing route coverage.

## Remaining

- Add scheduled message management parity.
- Add template reuse and richer list/filter behavior.
- Add production-safety confirmations for destructive workflows.

## Blockers

- Layout redesign and settings completeness are intended to land first so the page does not grow on top of temporary layout or configuration surfaces.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- This feature is a medium-priority parity slice, not the first cutover dependency.
