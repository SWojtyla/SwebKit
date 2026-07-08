# Frontend Plan - style-system-polish-9

---

title: "Frontend Plan - style-system-polish-9"
owner: ""
status: "Review"

---

## Goal

Drive the remaining app styling drift down to a 9/10 level by applying the shared primitives created in `style-system-harmonization` to the highest-impact remaining feature surfaces.

## Existing Primitives To Use

- `AppButton` for text buttons and command buttons.
- `AppIconButton` for icon-only compact actions with required labels.
- `AppSelect` for native selects that need consistent theme behavior.
- `FormField` for labelled field groups with hint/error text.
- `AppDropdown` for flyouts and dropdown menus that need focus/backdrop behavior.
- `SegmentedControl` for simple mode/tab strips with string options.
- `StatusBadge` for compact status/source/severity chips.

## Migration Rules

- Preserve current visuals first. Pass existing feature classes through `CssClass` and add scoped `::deep` selectors in the owning `.razor.css` file.
- Do not move feature-specific styling into global layer files unless it is promoted to a true reusable primitive.
- Prefer one feature area per implementation slice.
- Add or reuse focused tests for every migrated area.
- Keep `ctx-item` as the context-menu standard unless a separate menu replacement is explicitly planned.

## Candidate Slices

| Priority | Area                       | Main classes                                                         | Likely files                                              | Validation                          |
| -------- | -------------------------- | -------------------------------------------------------------------- | --------------------------------------------------------- | ----------------------------------- |
| 1        | Incident Timeline config   | `incident-timeline-config__button`                                   | `Components/Pages/IncidentTimelineConfigForm.razor(.css)` | `IncidentTimelineConfigFormTests`   |
| 2        | Dashboard controls         | `dashboard-action-button`, `dashboard-add-button`, `dashboard-field` | `Components/Pages/DashboardPage.razor(.css)`              | Dashboard component/shared tests    |
| 3        | Page header actions        | `page-header-action-btn`                                             | page components using `RoutePageHeader` actions           | route/page render tests + app build |
| 4        | Redis key detail           | `copy-btn`                                                           | `Components/Redis/RedisKeyDetail.razor(.css)`             | `RedisKeyDetailTests`               |
| 5        | Observability copy/export  | `obs-copy-btn`                                                       | `Components/Observability/*`                              | Observability tab tests             |
| 6        | Pipelines/Releases filters | `filter-select`, `form-input` selects                                | `Components/Pipelines/*`, `Components/Releases/*`         | Pipelines/Releases focused tests    |

## Implementation Notes

- Run `scripts/style-inventory.ps1 -Top 20` before and after each wave.
- Update `status.md` with per-wave inventory movement.
- Do not remove compatibility aliases until `style-inventory.ps1` reports no dependent legacy token references outside `wwwroot/styles/00-tokens-themes.css`.
- If a raw button remains because it is semantically better as native row/context-menu markup, add that as an explicit exception in `status.md`.

## Expected End State

The app should feel the same visually, but the implementation should make the shared controls the default for new work. Remaining raw controls should be intentional exceptions rather than accidental local styling drift.
