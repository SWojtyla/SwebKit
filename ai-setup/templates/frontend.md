# Frontend Plan - <feature-name>

---

title: "Frontend Plan - <feature-name>"
owner: ""
status: "Not started"

---

## Goal

Describe the UI/client outcome: what the user sees or can do that they couldn’t before.

## Impacted areas

- Files / components: `src/...`
- Pages / routes
- Shared components

## UX notes

- User flows: describe the happy path and key edge cases (empty state, loading, error)
- Component states: loading / loaded / error / empty — all must be handled
- Accessibility: keyboard navigation, contrast, screen reader _(if applicable for target platform)_

## API / contract changes

- DTOs, props, events, and contracts that will change
- Backward compatibility notes

## Tasks

- [ ] Update UI contract / viewmodel
- [ ] Implement components / pages
- [ ] Handle loading, error, and empty states
- [ ] Wire to backend / state layer
- [ ] Add unit / component tests
- [ ] Add e2e tests for core flows
- [ ] Record key design choices in `decisions.md` _(if decisions exist)_

## Validation

- Component tests: Not started / In progress / Passed
- Manual UX checks: list of acceptance steps

## Notes

- Important implementation details, style guide references, or design tokens
