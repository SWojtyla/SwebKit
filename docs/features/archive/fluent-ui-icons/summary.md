# Archive Summary — Fluent UI Icons

---

title: "Archive Summary — Fluent UI Icons"
owner: ""
completed_date: "2026-03-21"
pr: ""
commit: ""

---

## Goal

Replace all emoji-based and Unicode symbol icons in the app with proper `<FluentIcon>` components from `Microsoft.FluentUI.AspNetCore.Components`, which was already a project dependency. Covers the left nav, dashboard cards, and all feature-specific components.

## Delivered

- `NavItem.razor`: `string Icon` param replaced with `Icon NavIcon`; emoji span → `<FluentIcon Width="20px" />`
- `LeftNav.razor`: all 6 nav items pass Fluent icon instances (ArrowSwap, CloudCube, Database, FolderOpen, Rocket, Settings)
- `DashboardPage.razor`: all 6 dashboard card emoji divs → `<FluentIcon Width="32px" />`
- `DashboardPage.razor.css`: `.dashboard-card-icon` updated to use flex alignment; removed `font-size` emoji sizing
- `_Imports.razor`: changed to alias form `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` to enable `Icons.Regular.SizeXX.Name` syntax
- `ReadinessGate.razor`: `ComputeGlobalReadiness()` return type `(string, string, string)` → `(Icon, string, string)`; template uses `<FluentIcon>`; 5 states mapped (DismissCircle, Clock, CheckmarkCircle, Warning, CircleOff)
- `ReleaseBoard.razor`: `ComputeReadiness()` same pattern; 6 states mapped including `LockClosed` for awaiting approval
- `ConfirmDialog.razor`, `ApprovalCenter.razor`, `DlqView.razor`, `TagManager.razor`, `StorageConfigForm.razor`: all `&#9888;`/`⚠` warning banners → `<FluentIcon Warning Size16 />`
- `TagManager.razor`: `✓` after confirmed tag name → `<FluentIcon Checkmark Size16 />`
- `ServiceBusPage.razor`: connection status `●` dots → `<FluentIcon Filled.Circle Size12 />` with color wrapper; `🕐` clock → `<FluentIcon Clock />`; remove `✕` and DLQ tab `&#9888;`
- `AksPage.razor`: `☸` empty state → `<FluentIcon CloudCube Size32 Width="48px" />`; all events warning/normal icons; Edit/Search/Rollback/Clear buttons; 11 close/cancel `FluentButton` content items
- `MessageListView.razor`: `✓` column title string → empty string (cannot use component in a string attribute)

## Key decisions

- **`_Imports.razor` alias form** — `@using Icons = ...Icons` is required for `Icons.Regular.Size16.X` to resolve in Razor files. The bare `@using` form imports a namespace containing no directly-usable types in razor expressions.
- **Size selection** — size is a class choice (`Size16`, `Size24`) but rendering is controlled by the `Width` prop. Used `Size16` classes with `Width="14px"`/`"16px"` for inline icons, `Size24` for nav, `Size32` for empty states.
- **Color inheritance** — `<FluentIcon>` inherits CSS `color` via `currentColor`. No `Color` prop needed; wrapping in a colored `<span>` is sufficient. Active nav items automatically tint icons via `.nav-item.active { color: var(--color-accent) }`.
- **`Filled` variants for strong status** — Used `Icons.Filled.Size16.CheckmarkCircle` and `Icons.Filled.Size16.DismissCircle` for success/failure states (solid icon carries more visual weight than Regular outline).
- **Context menu icon spans excluded** — `ctx-item-icon` spans with text characters (`{ }`, `&#9776;`, `&#9881;`, etc.) are a consistent pattern inside a custom context menu component. Replacing them requires a separate design decision about the context menu architecture.
- **`→` text decorators excluded** — `→` in "Replay →", "Schedule →" are prose label connectors, not standalone icons.

## Validation performed

- Build: 0 errors, 0 warnings (`dotnet build` against net10.0-windows10.0.19041.0)
- Visual verification pending (manual check at 100% and 150% DPI, expanded/collapsed nav)

## Lessons learned

- **`_Imports.razor` must use the alias form** — `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` is required; the bare `@using` form doesn't expose `Icons.Regular.Size24.X` syntax in Razor expressions. This is a non-obvious gotcha that will affect any future Fluent icon work.
- **Verify icon names before writing code** — the Fluent icon set is large but naming is inconsistent (`LockClosed` not `Lock`, `Warning` not `WarningTriangle`). Always search the DLL binary or NuGet source before assuming a name.
- **Return type changes cascade** — changing a C# method return type from `string` to `Icon` in a Razor code block requires updating the template binding site at the same time; the compiler error is clear but easy to miss when editing in parts.
- **`Filled` variants for binary state icons matter** — `CheckmarkCircle` and `DismissCircle` in `Filled` carry stronger visual weight than the `Regular` outline variants; this is the right choice for success/failure status cells.
- **`FluentButton` inner content accepts components** — replacing text/entity content inside a `<FluentButton>` with `<FluentIcon>` works normally; no wrapper needed.

## Follow-up

- Context menu icon spans (`ctx-item-icon`) in `AksPage.razor` — still use text Unicode characters; could be addressed as part of a broader context menu redesign
- Visual spot-check at 150% DPI for SVG sharpness
