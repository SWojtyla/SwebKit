# Feature Overview - style-system-polish-9

---

title: "Feature Overview - style-system-polish-9"
owner: ""
status: "Review"
jira: ""
created: "2026-06-14"
updated: "2026-06-14"

---

## Goal

Raise SwebKit styling consistency from the current review-ready 7.5/10 state to at least 9/10 by migrating the remaining repeated control families to the shared style primitives and reducing global/style drift without changing the AKS or API Client visual direction.

## Value

The first style-system feature established the primitives, migration pattern, inventory tooling, compatibility aliases, and proof migrations across API Client, AKS, and Service Bus. The remaining gap is breadth: several feature areas still carry local button/select/input families that make new work copy-driven instead of primitive-driven. This follow-up turns that system into the default path across the app.

## Baseline

Baseline inventory captured before this follow-up from `scripts/style-inventory.ps1 -Top 20`:

- `app.css`: 5,688 lines
- Source CSS files: 126
- Component-scoped CSS files: 125
- Component-scoped CSS lines: 22,180
- Razor component files under `Components`: 186
- Raw `<button>` occurrences: 546
- Raw `<select>` occurrences: 48
- `PageToolbar` usages: 2
- `AppDropdown` usages: 1
- `app-native-control` occurrences: 87

Current top drift families:

- `ctx-item` - 81 occurrences; keep for context menus unless replacing the menu primitive in a dedicated slice.
- `incident-timeline-config__button` - 16 occurrences across normal/secondary/danger variants.
- `dashboard-action-button` / `dashboard-add-button` - dashboard control surface.
- `page-header-action-btn` - repeated page header actions.
- `copy-btn` and `obs-copy-btn` - Redis and Observability copy/export commands.
- `pill-tab` - Pipelines page tab strip.
- `filter-select`, `form-input`, and `dashboard-field` - old select/input helper families.

## Scope

### In scope

- Migrate the remaining high-count button families to `AppButton` / `AppIconButton` while preserving current feature visuals through scoped `::deep` selectors where needed.
- Migrate repeated native selects to `AppSelect` and repeated form wrappers to `FormField` where labels/hints/errors are present.
- Replace simple repeated tab/mode button strips with `SegmentedControl` only where the existing UI shape maps cleanly.
- Increase `PageToolbar` adoption where pages are already using page-header action rows or local toolbar rows.
- Improve the inventory script if it needs better tracking for shared primitive adoption.
- Update status/test notes after each migrated feature area.

### Out of scope

- No visual redesign or palette/theme refresh.
- No removal of compatibility aliases until inventory confirms no dependent legacy token usage.
- No replacement of context menu `ctx-item` in this feature unless it becomes the only remaining large drift family and has tests.
- No broad rewrite of dashboard layout or page composition.
- No API Client synthetic demo-data work.

## Target 9/10 Criteria

The feature is done when all of these are true:

- Raw `<button>` occurrences are below 480, excluding `ctx-item` context-menu entries and controls that intentionally remain native for table rows or third-party components.
- Raw `<select>` occurrences are below 32, excluding simple native selects that are already using `app-native-control app-native-select` and have no label/error contract.
- `PageToolbar` or another shared toolbar primitive is used by at least 5 page or feature toolbar surfaces.
- `AppDropdown` is used by at least 3 dropdown/flyout surfaces.
- No new feature-local button/select family is introduced without documenting why a shared primitive is not appropriate.
- All migrated surfaces preserve the existing visual direction under dark and light themes.
- Focused tests pass for each migrated area, plus app build with local MSIX signing disabled.
- The feature status and inventory snapshot are updated with final counts.

## Priority Migration Waves

### Wave 1 - Incident Timeline config controls

Migrate `incident-timeline-config__button` normal/secondary/danger buttons in `src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor` to `AppButton`. Preserve the existing config-form visual style with scoped `::deep` selectors and run `IncidentTimelineConfigFormTests`.

### Wave 2 - Dashboard command controls

Migrate `dashboard-action-button`, `dashboard-add-button`, and the main dashboard shell selects/fields where low risk. Preserve the current dashboard look; do not redesign the command-center composition. Run dashboard shared/component tests and focused dashboard render tests.

### Wave 3 - Page header actions

Migrate simple `page-header-action-btn` usages in page headers to `AppButton` or a page-header action primitive. Include pages such as Service Bus, Observability, Pipelines, Storage, Incident Timeline, and Monitoring. Preserve links where anchors are semantically needed.

### Wave 4 - Redis and Observability copy/export controls

Migrate `copy-btn`, `obs-copy-btn`, and similar small command buttons to `AppIconButton` or `AppButton`. Preserve compact density and copy-state text. Run Redis and Observability focused tests.

### Wave 5 - Pipelines/Releases form helpers

Migrate `filter-select`, old `form-input` selects, and simple branch/status dropdowns to `AppSelect` / `FormField` where labels or validation are present. Keep raw inputs where they are already simple and not causing drift.

## Dependencies

- Foundation feature: `docs/features/active/style-system-harmonization/`
- Styling guidance: `docs/architecture/codebase-guide.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md` BL-11 for CSS isolation and `::deep` usage
- Inventory tool: `scripts/style-inventory.ps1`

## Risks & Mitigations

- Risk: shared components change feature visuals. Mitigation: keep feature classes and scoped `::deep` bridges during migration.
- Risk: over-migrating table-row or context-menu controls makes markup noisier. Mitigation: exclude `ctx-item` and row-local controls unless there is a clear benefit.
- Risk: global CSS layers grow while adding compatibility styles. Mitigation: migrate feature-local style bridges in component CSS, not global CSS, unless the rule is truly shared.
- Risk: dashboard visual balance changes. Mitigation: dashboard wave must be visual-review-first and test-backed.

## Related Documents

- Source feature: `docs/features/active/style-system-harmonization/`
- CSS architecture follow-up: `docs/features/active/style-system-css-architecture/`
- Status: `status.md`
- Test plan: `test-plan.md`
- Frontend plan: `frontend.md`
- Decisions: `decisions.md`

## Quick Links

- Jira: not linked
- Implementation modules: `frontend.md`, `decisions.md`
