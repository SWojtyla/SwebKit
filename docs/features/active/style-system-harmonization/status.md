# Status - style-system-harmonization

---

title: "Status - style-system-harmonization"
owner: ""
state: "Planned"
jira: ""
branch: ""
started: "2026-06-13"
last_updated: "2026-06-13"

---

## Quick Summary

Planning and codebase styling review are complete; next step is scope review before implementation starts.

**Jira:** not linked

**Current focus:** Confirm the staged style-system cleanup scope and choose the first migration slice.

## Progress Checklist

- [x] Project architecture context loaded
- [x] Relevant Blazor/MAUI pitfalls reviewed
- [x] Active feature overlap checked
- [x] Current CSS and Razor control usage measured
- [x] Honest current-state review captured
- [x] Frontend/style-system plan drafted
- [x] Test plan drafted
- [ ] Scope confirmed by maintainer
- [ ] Design reviewed
- [ ] Implementation wave 0 - style contract and token cleanup
- [ ] Implementation wave 1 - shared control primitives
- [ ] Implementation wave 2 - high-drift feature migration
- [ ] Implementation wave 3 - remaining feature sweep and docs alignment
- [ ] Automated and manual validation passed
- [ ] Ready for review

## Completed

- Confirmed no existing active feature directly owns a global styling harmonization effort.
- Captured maintainer preference that AKS and API Client currently look good and should be preserved as visual reference surfaces.
- Measured source styling footprint:
  - `app.css`: 5,255 lines
  - Source CSS files: 126
  - Component-scoped CSS files: 125
  - Component-scoped CSS lines: 22,099
  - Razor component files under `Components`: 179
  - Approximate raw button occurrences: 615
  - Approximate raw select occurrences: 54
  - `PageToolbar` usages: 2
  - `Dropdown` component usages: 0
  - `app-native-control` occurrences in Razor markup: 85 total matches, with 20 direct class attributes in the source metric pass
- Identified fragmented control families and undefined/legacy token names.
- Assigned current global styling score: 6/10.

## Remaining

- Confirm whether the intended scope is a broad app-wide style-system cleanup rather than a smaller API Client or button/dropdown-only cleanup.
- Decide the canonical button/select/dropdown approach.
- Decide whether `app.css` should remain a single physical file or become an import entry point for layered files.
- Create or update architecture documentation for styling conventions if implementation proceeds.
- Implement migrations in small, reviewable slices.

## Blockers

- Maintainer scope confirmation is still needed before code changes. The inferred scope is broad, app-wide styling harmonization with no visual rebrand.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- The first implementation should avoid sweeping all pages at once. Start with tokens and primitives, then migrate one high-drift feature area such as API Client.
- Keep compatibility aliases during migration for old classes and tokens to avoid breaking multiple routes in one change.
- Refactor the styling model underneath AKS and API Client without redesigning their current look.