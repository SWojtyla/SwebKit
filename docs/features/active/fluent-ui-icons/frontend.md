# Frontend Plan — Fluent UI Icons in Navigation

## Affected files

- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/NavItem.razor`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor.css` (if it exists) — sizing adjustments

## Icon mapping

| Area | Current emoji | Fluent icon (Regular/24) |
|---|---|---|
| Service Bus | ⇔ | `Icons.Regular.Size24.ArrowSwap` |
| AKS | ☸ | `Icons.Regular.Size24.CloudCube` or `Icons.Regular.Size24.Server` |
| Redis | 🧰 | `Icons.Regular.Size24.Database` |
| Storage | ☁ | `Icons.Regular.Size24.Storage` or `Icons.Regular.Size24.FolderOpen` |
| Releases | 🚀 | `Icons.Regular.Size24.Rocket` |
| Settings | ⚙ | `Icons.Regular.Size24.Settings` |

> Exact icon names must be verified against the installed version of `Microsoft.FluentUI.AspNetCore.Components`. Use the Fluent UI Blazor icon explorer or search the package source.

## `NavItem.razor` changes

Replace:
```html
<span class="nav-icon">@Icon</span>
```
With:
```html
<FluentIcon Value="@IconValue" Width="20px" />
```

`IconValue` is a `Icon` object passed as a parameter instead of a string emoji.

The `NavItem` parameter type changes from `string Icon` to `Icon Icon` (the Fluent icon type). Update `LeftNav.razor` to pass the correct icon objects.

## `DashboardPage.razor` changes

Each dashboard card currently has:
```html
<div class="dashboard-card-icon">🚀</div>
```

Replace with:
```html
<FluentIcon Value="@Icons.Regular.Size24.Rocket" Width="32px" />
```

Apply the same mapping table as above. Adjust `.dashboard-card-icon` CSS to size and centre the SVG icon correctly (remove `font-size` rule; add `display: flex; align-items: center; justify-content: center`).

## CSS adjustments

- Remove any `font-size` rules on `.nav-icon` that were sized for emoji
- Add `color: var(--color-text)` to `<FluentIcon>` wrapper if the icon doesn't inherit colour automatically
- Ensure active nav item icon uses `var(--color-accent)` — check if `FluentIcon` accepts a `Color` parameter or if a CSS wrapper is needed

## Tasks

- [ ] Verify icon names in installed Fluent UI Blazor version
- [ ] Update `NavItem.razor` parameter type from `string` to `Icon`
- [ ] Update `LeftNav.razor` to pass icon objects
- [ ] Update `DashboardPage.razor` dashboard card icons
- [ ] Adjust CSS for icon sizing in nav and dashboard
- [ ] Visual check at 100% and 150% DPI
- [ ] Visual check with nav expanded and collapsed
