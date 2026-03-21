# Feature Overview — Fluent UI Icons in Navigation

---

title: "Fluent UI Icons in Navigation"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Replace all emoji-based icons in the left navigation bar with proper Fluent UI icons from the `Microsoft.FluentUI.AspNetCore.Components` icon set, which is already a project dependency.

## Value

Emoji icons have inconsistent visual weight, vary in rendering across Windows versions and display scaling, and look out of place in a developer-focused dark-theme tool. Fluent UI icons are SVG-based, scale perfectly, and match the Fluent design system already used for all other UI components.

## Scope

### In scope

- Replace all emoji icons in `LeftNav.razor` and `NavItem.razor` with `<FluentIcon>` components
- Replace emoji icons in `DashboardPage.razor` dashboard cards with matching `<FluentIcon>` components
- Map each feature area to an appropriate Fluent icon:
  - Service Bus → `Icons.Regular.Size24.MailInbox` or `ArrowSwap`
  - AKS → `Icons.Regular.Size24.Cloud` or `CubeAdd`
  - Redis → `Icons.Regular.Size24.Database`
  - Storage → `Icons.Regular.Size24.Storage`
  - Releases → `Icons.Regular.Size24.Rocket`
  - Settings → `Icons.Regular.Size24.Settings`
- Ensure icons render correctly at both expanded (icon + label) and collapsed (icon only) nav states
- Ensure icons in the dashboard cards have consistent size and alignment

### Out of scope

- Replacing icons in feature-specific components (grids, detail panels, buttons) — that is broader icon system work
- Custom SVG icons
- Animated icons

## Dependencies

- `Microsoft.FluentUI.AspNetCore.Components` — already in the project; icon pack included
- `LeftNav.razor`, `NavItem.razor`, `DashboardPage.razor`

## Risks

- Icon name availability: the Fluent icon set is large but not every conceptual icon has a perfect match. Final icon choices should be confirmed against the available set before implementation.
- CSS sizing: `<FluentIcon>` uses its own sizing props; the existing `.nav-icon` CSS rules may need adjustment to align with the SVG rendering.

## Related documents

- Architecture: `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
