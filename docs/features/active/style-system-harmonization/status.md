# Status - style-system-harmonization

---

title: "Status - style-system-harmonization"
owner: ""
state: "Review"
jira: ""
branch: ""
started: "2026-06-13"
last_updated: "2026-06-14"

---

## Quick Summary

Shared style primitives are implemented and high-value API Client, AKS, and Service Bus slices are migrated while preserving their existing visual direction.

**Jira:** not linked

**Current focus:** Ready for review. Continue remaining hotspots as follow-up slices only when a specific feature area is already in scope.

## Progress Checklist

- [x] Project architecture context loaded
- [x] Relevant Blazor/MAUI pitfalls reviewed
- [x] Active feature overlap checked
- [x] Current CSS and Razor control usage measured
- [x] Honest current-state review captured
- [x] Frontend/style-system plan drafted
- [x] Test plan drafted
- [x] Scope confirmed by maintainer
- [x] Design reviewed
- [x] Implementation wave 0 - style contract and token cleanup
- [x] Implementation wave 1 - shared control primitives
- [x] Implementation wave 2 - high-drift feature migration
- [x] Implementation wave 3 - remaining feature sweep after visual review
- [x] Automated validation passed
- [x] Ready for review

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
- Added compatibility aliases in `src/SwebKit.App/wwwroot/app.css` for old token names used by existing component CSS.
- Added `scripts/style-inventory.ps1` to make future style drift measurable.
- Added styling-system navigation and ownership rules to `docs/architecture/codebase-guide.md`.
- Documented the initial canonical control semantics and accepted the `--color-danger` to `--color-error` compatibility mapping.
- Added shared primitives: `AppButton`, `AppIconButton`, `FormField`, `AppSelect`, `AppDropdown`, `StatusBadge`, and `SegmentedControl`.
- Extended `PageToolbar` with density, wrapping, and custom-class conventions while preserving its existing default behavior.
- Added focused shared primitive bUnit coverage in `tests/SwebKit.App.Tests/StyleSystemPrimitiveTests.cs`.
- Migrated the API Client initial toolbar action buttons to `AppButton` with existing `api-client-toolbar-btn` classes passed through `CssClass`.
- Migrated remaining API Client raw buttons using `api-client-toolbar-btn`, `api-client-toolbar-btn--danger`, `api-client-secret-warning__btn`, `api-client-dialog__btn`, and `api-client-dialog__btn--primary` to `AppButton` while preserving existing visual classes through `CssClass`.
- Migrated the bounded API Client auth and variable generator selects to `AppSelect` with existing `auth-panel__type-select` and `var-gen-editor__select` classes passed through `CssClass`, plus scoped `::deep` selectors to preserve isolated component styling.
- Migrated the post-request capture builder add button, source/scope selects, and delete action to `AppButton`, `AppSelect`, and `AppIconButton`, with scoped `::deep` selectors preserving the existing compact capture-rule visual styling.
- Migrated the AKS auto-refresh toggle button and interval select to `AppButton` and `AppSelect`, with existing class hooks and scoped `::deep` selectors preserving the active dot, compact interval control, and current timer behavior.
- Migrated the bounded Service Bus MessageListView toolbar button family (`message-list-view__toolbar-button`) to `AppButton`, preserving existing classes via `CssClass` and adding scoped `::deep` selectors for isolated CSS reach-through.
- Migrated the bounded Service Bus `MessageDetailPane` `mdp-btn` button family to `AppButton`, preserving existing classes via `CssClass` and adding scoped `::deep` selectors for isolated CSS reach-through.
- Added focused post-request capture builder tests and reused existing AKS connection-bar tests for the AKS slice.
- Verified inventory movement after the first migrations: raw button occurrences dropped from 615 to 572, and raw select occurrences dropped from 54 to 48.
- Hardened `AppDropdown` with Escape-key close behavior and the existing focus save/trap/restore helpers before broader adoption.
- Added scoped API Client toolbar content spacing so icon/text toolbar buttons keep their pre-migration spacing inside `AppButton`.
- Migrated the API Client environment picker to `AppDropdown`, preserving the existing trigger and menu classes with scoped `::deep` selectors.
- Inventory after the Service Bus toolbar slice: raw button occurrences 556, raw select occurrences 48, `AppDropdown` usages 1.
- Final inventory after the MessageDetailPane slice: raw button occurrences 546, raw select occurrences 48, `AppDropdown` usages 1.

## Remaining

- Perform visual review of the migrated API Client, AKS, and Service Bus slices.
- Continue remaining hotspots as follow-up slices when those feature areas are already in scope. Current top candidates from inventory are Incident Timeline config buttons, Dashboard controls, page-header actions, Redis copy buttons, Observability copy buttons, and Pipelines/Releases form/select helpers.
- Avoid removing compatibility aliases until the inventory shows no dependent legacy token references remain.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Wave 2C bounded API Client select migration passed Razor/CSS diagnostics for `AuthPanel` and `VariableGeneratorEditor`, raw-select search in both migrated Razor files, and app project build with local MSIX signing disabled; existing unrelated build warnings remain. Post-request capture builder migration passed Razor/CSS diagnostics, raw-control search, focused bUnit tests, whitespace check, and app project build with local MSIX signing disabled; existing unrelated build warnings remain.
- AKS auto-refresh toolbar slice passed Razor/CSS diagnostics, focused `AksConnectionBarTests` (12/12), and app project build with local MSIX signing disabled; existing unrelated build warnings remain.
- Service Bus MessageListView toolbar slice passed Razor/CSS diagnostics, raw toolbar-button search, focused `MessageListViewTests` (27/27), whitespace check, and app project build with local MSIX signing disabled; existing unrelated warnings remain.
- Service Bus MessageDetailPane slice passed Razor/CSS diagnostics, raw `<button ... mdp-btn` search, focused `MessageDetailPaneTests` (22/22), and app project build with local MSIX signing disabled; existing unrelated warnings remain.
- Final focused validation passed: `StyleSystemPrimitiveTests`, `PostRequestCaptureBuilderTests`, and `AksConnectionBarTests` passed 36/36.
- Final app build passed with local MSIX signing disabled. Existing out-of-scope warnings remain in `DlqView`, `OAuth2TokenManager`, and WinAppSDK PRI qualifiers.
- `git diff --check` passed with no whitespace issues.

## Notes

- The first implementation should avoid sweeping all pages at once. Start with tokens and primitives, then migrate one high-drift feature area such as API Client.
- Keep compatibility aliases during migration for old classes and tokens to avoid breaking multiple routes in one change.
- Refactor the styling model underneath AKS and API Client without redesigning their current look.
- When migrating a feature-local class onto a shared child component, preserve scoped visual styling with `::deep` selectors or move only truly shared styling into the global primitive layer. Passing a feature class through `CssClass` alone is not enough under Blazor CSS isolation.
