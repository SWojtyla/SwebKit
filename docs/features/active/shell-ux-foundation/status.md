# Status - shell-ux-foundation

---

title: "Status - shell-ux-foundation"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The shell-foundation plan is ready. The next meaningful step is implementation of route-derived shell context and a shared page-header contract before any broader workspace or readiness features begin.

Jira: not linked

Current focus: lock the shell context model, header standard, and status/empty/loading/error conventions that later features will consume.

## Progress checklist

### Wave 1 - Shell context and navigation

- [x] Define scope and implementation waves
- [ ] Route-derived shell context model
- [ ] Navigation grouping and active-state behavior
- [ ] Top-bar context for current area and active environment

### Wave 2 - Shared state patterns and status signals

- [ ] Shared page-header contract
- [ ] Shared loading/error/empty-state pattern rollout
- [ ] Trustworthy refresh/status signal updates
- [ ] Cross-page adoption on core routed pages

### Wave 3 - Polish and safety

- [ ] Notification-center polish
- [ ] Theme consistency and token cleanup
- [ ] Production-safety treatment alignment
- [ ] Component and E2E regression coverage

## Completed

- Created the active feature folder and core planning docs.
- Identified the specific shell seams already present in the codebase: `MainLayout`, `LeftNav`, `TopBar`, `StatusBar`, shared components, and routed pages.
- Captured the current inconsistency sources that this feature must resolve.

## Remaining

- Implement route-aware shell context and page metadata.
- Standardize headers, empty/loading/error patterns, and status signals across top-level pages.
- Roll out shell-level production cues and notification/theme polish.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started.

## Notes

- This feature is intentionally broad enough to be foundational but should still remain implementation-sized.
- Any request that adds new shell-level surface area during implementation should be evaluated against this feature before landing as a one-off page change.
