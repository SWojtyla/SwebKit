# Frontend Plan - Redis Follow-up

---

title: "Frontend Plan - Redis Follow-up"
owner: ""
status: "Done"

---

## Goal

Deliver a clearer Redis UX for selecting caches, understanding key namespaces, and running destructive actions with unambiguous language.

## Impacted areas

- Files / components: `src/SwebKit.App/Components/Pages/RedisPage.razor`, `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`, `src/SwebKit.App/Components/Redis/*`
- Pages / routes: `/redis`, settings page Redis section
- Shared components: confirmation dialog/action labels

## UX and accessibility notes

- Add cache selector dropdown in the Redis toolbar and in settings management.
- Show editable cache display name in toolbar connection text.
- Replace `Flush DB` with `Purge All` consistently (button, dialog title, message).
- Remove Server Info button and related panel from Redis page.
- Add pattern examples/help text adjacent to scan pattern input.
- Add namespace grouping tree with editable separator control and clear empty/loading/error states.
- Add prefix memory distribution panel with understandable labels and coverage hints.

## API / contract changes

- UI contract for selected cache id/key and list of caches.
- UI state for namespace separator and grouping mode.
- UI data contract for prefix memory distribution.

## Tasks

- [ ] Update UI contract / viewmodel
- [ ] Implement components / pages
- [ ] Handle loading, error, and empty states
- [ ] Wire to backend / state layer
- [ ] Add unit / component tests
- [ ] Add e2e tests for core flows
- [ ] Accessibility review

## Validation

- Component tests: Not started
- Manual UX checks:
  - Switch caches and verify keyspace updates.
  - Rename cache and verify display text updates.
  - Change separator and verify namespace tree regrouping.
  - Verify `Purge All` language and confirmation behavior.
  - Verify pattern examples are visible and usable.

## Notes

- Keep visual hierarchy focused on primary workflows; avoid reintroducing unused telemetry controls.
