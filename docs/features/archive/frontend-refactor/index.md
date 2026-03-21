# Frontend Refactor

## Goal

Improve the frontend codebase quality to be maintainable, understandable, and testable. No new features — only quality improvements to existing code.

## Scope

- CSS architecture: token system, remove inline styles, z-index management
- Component extraction: shared UI primitives, eliminate duplication
- Component size: split god components, enforce single responsibility
- Async patterns: fix fire-and-forget, dispose leaks, StateHasChanged overuse
- Testability: enable bUnit tests for core components

## Non-goals

- New features or UI changes
- Dark/light mode toggle (separate feature)
- Accessibility audit (separate feature)
- Redesigning existing UX patterns

## Dependencies

None — self-contained refactor.

## Risks

- Extracting components may introduce regressions in rendering or event wiring
- CSS isolation changes may break scoping inadvertently
- Disposing timers incorrectly may break auto-refresh

## Quick links

- [Status](status.md)
- [Frontend plan](frontend.md)
- [Test plan](test-plan.md)
- [Decisions](decisions.md)
