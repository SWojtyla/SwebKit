# Status - winui3-aks-parity

---

title: "Status - winui3-aks-parity"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

AKS already has a real native route. The remaining work is diagnostics depth, broader resource coverage, and action parity once layout and settings foundations are in place.

**Jira:** not linked

**Current focus:** pin down the MAUI-only AKS diagnostics and resource panels that still matter for cutover.

## Progress checklist

- [x] MAUI versus WinUI AKS gap captured
- [ ] Remaining resource-type coverage confirmed
- [ ] Diagnostics-card adoption planned against shared primitives
- [ ] Operational action parity defined
- [ ] Focused validation approach defined
- [ ] Docs aligned after implementation begins

## Completed

- Confirmed that AKS no longer blocks the baseline routed WinUI host.
- Identified AKS as one of the highest refactor-pressure pages because diagnostics patterns repeat across the view.

## Remaining

- Restore the AKS resource and diagnostics surfaces that still only exist in MAUI.
- Align the page with the shared card, state, and detail-pane primitives.
- Validate operational actions under navigation and cancellation pressure.

## Blockers

- Layout redesign and settings completeness are expected to land first.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- AKS should be one of the first domain adopters of the shared layout primitives after Dashboard and Settings.
