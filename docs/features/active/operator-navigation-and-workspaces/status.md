# Status - operator-navigation-and-workspaces

---

title: "Status - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The workspace/navigation plan is defined and intentionally sits behind `shell-ux-foundation`. The next step is to finalize the shell context contract and then implement the shared search/resource/workspace model on top of it.

Jira: not linked

Current focus: design the persistent model split for favorites, recents, and named workspaces before implementation starts.

## Progress checklist

### Planning

- [x] Define scope and implementation waves
- [x] Confirm dependency on `shell-ux-foundation`
- [ ] Finalize persistence model for recents, favorites, and workspaces
- [ ] Finalize page-contributor contract for search and workspace restore

### Implementation focus

- [ ] Command palette precision and unified resource search
- [ ] Shell-level recent/favorite resources
- [ ] Saved investigation workspace authoring and restore
- [ ] Component, unit, and E2E validation

## Completed

- Created the active feature folder and core docs.
- Identified the existing seams this feature should extend instead of replacing: `CommandRegistry`, `SelectionContext`, `TabService`, `UiStateRepository`, and `AppConfig.FavoriteEntities`.
- Scoped the work as shell-level navigation/workspace behavior rather than as a domain feature.

## Remaining

- Decide the exact storage split between `ui-state.json` and `profiles.json`.
- Implement provider-based resource search and workspace snapshot contributors.
- Roll out the shared model across the main investigation pages.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started.

## Notes

- This feature should not start by rewriting page-local tabs. It should first define the canonical shell model and then adapt pages to it.
