# Layout Plan - visual-restyle-and-theme-overhaul

---

title: "Layout Plan - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
status: "Done"

---

## Goal

Finish the shell-layout plan for the chosen `Studio Ledger` direction without changing the app's global geometry, route model, or navigation structure.

## Impacted areas

- Shell host and theme owner:
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/wwwroot/app.css`
- Shell primitives:
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- `src/SwebKit.App/Components/Shared/PageHeader.razor`
- `src/SwebKit.App/Components/Shared/RoutePageHeader.razor`
- Routed pages that currently rely on shell-level support content:
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`

## Layout constraints

- Keep the current shell grid, navigation placement, route URLs, and major page composition intact.
- The top bar owns route identity: group label, page title, and summary should come from `ShellNavigation` via `MainLayout` and `TopBar`.
- Page-level layout surfaces should exist only when they carry page-specific value such as actions, filters, scope pills, or warnings.
- Future palettes must branch from the selected `Studio Ledger` structure instead of introducing a second shell language.

## Current state

- `Studio Ledger` is now the chosen global dark direction and the default dark theme.
- The previous blank route-page header shell has been removed.
- Routed pages now use the compact support-strip pattern where page-local pills or actions still matter.
- Top bar, left nav, status bar, pills, and page support rows now share the chosen `Studio Ledger` shell language without changing the shell geometry.

## Chosen shell model

### Top bar

- Owns route context for every page: group, title, and summary.
- Carries shell-wide badges only: connection health, production, demo, workspace hub, notifications, and command palette access.
- Should not absorb page-specific controls such as route-local settings links or workflow scopes.

### Page support strip

- Replaces the old empty route-header shell with a compact row that only renders when a page has route-local pills or actions.
- Hosts page-specific scope pills, counts, warnings, and links such as `Settings`.
- Must remain visually lighter than a full card/header shell so it does not read like a second title bar.

### Full page header

- Keep `PageHeader` available for the rare cases where a page genuinely needs a visible title, subtitle, and actions inside the page body.
- Do not use it by default while the top bar already communicates the page identity.

## Delivery slices

### Slice 1 - Shell context ownership

- Confirm `ShellNavigation` copy remains the single route-context source.
- Keep `TopBar` title, eyebrow, and summary readable across desktop widths without forcing pages to duplicate that copy.
- Audit any page that still feels unclear without an in-page title and decide whether it needs a true body header or just better support-strip content.

### Slice 2 - Page entry cleanup

- Replace the old route-header shell pattern with the compact support-strip pattern on routed pages.
- Remove dead `page-shell-header` wrappers where a page no longer exposes any meta or actions.
- Keep route-local `Settings` links, counts, and scope pills visible after the header cleanup.

### Slice 3 - Top bar polish

- Refine spacing, typography, and emphasis for the context stack.
- Align workspace hub, notifications, demo toggle, and command palette controls with the selected `Studio Ledger` surface language.
- Ensure shell badges use the same semantic visual language as page-local pills without competing for attention.

### Slice 4 - Left-nav polish

- Normalize group spacing, active-item framing, hover states, and collapsed-mode readability.
- Keep group labels, icon alignment, and footer-group behavior stable in both expanded and collapsed modes.
- Strengthen keyboard focus and current-area clarity without altering nav information architecture.

### Slice 5 - Status-bar polish

- Reduce noise while preserving operational value.
- Keep connection summaries, refresh recency, task progress, and port-forward status readable at a glance.
- Ensure production state still reads as stronger than normal informational chrome.

### Slice 6 - Shared shell token pass

- Separate shell-surface tokens from page/table tokens where that improves clarity.
- Make top bar, nav, status bar, pills, and support-strip surfaces palette-ready so alternate `Studio Ledger` colorways can reuse the same layout language.
- Keep radii, shadows, spacing, and typography stable across those palette variants.

### Slice 7 - Adoption waves

- Wave 1: Dashboard, AKS, Storage, and Service Bus.
- Wave 2: Pipelines, Observability, Settings, Redis, and Incident Timeline.
- Each wave should finish shell-entry cleanup before deeper table or feature-level polish in that area.

## Palette readiness

- Reserve future palette slots as additional `dark-studio-ledger-*` theme values.
- Palette work may change accent, surface hue, and semantic color families.
- Palette work must not change shell geometry, nav behavior, page-entry pattern, or the shared table/layout contract.
- Candidate palette families to evaluate after the token audit:
- `Slate` - current premium default.
- `Ink` - cooler, more neutral monochrome variant.
- `Brass` - warmer accent-led variant that still keeps the same shell structure.
- `Sage` - softer green-blue operations palette within the same layout language.

## Acceptance criteria

- No routed page shows an empty header shell at entry.
- Route identity is clearly communicated by the top bar across all major pages.
- Page-specific pills and actions remain available through compact support strips or true body headers where justified.
- Top bar, left nav, status bar, pills, and support strips feel like one coherent `Studio Ledger` shell.
- Future palette additions can be introduced without changing layout markup or shell behavior.

## Validation

- Component/build check: focused shell-foundation coverage remains green and the app/test projects compile after the shell-layout rollout.
- Manual check: open Dashboard, AKS, Storage, Service Bus, Pipelines, Observability, Redis, Incident Timeline, and Settings and verify the top-of-page layout reads intentionally with no blank shell surface.
- Manual check: verify page-specific pills and actions remain reachable after header cleanup.
- Manual check: resize to narrow desktop widths and ensure top bar context, left nav, support strips, and status bar still read clearly.
- Manual check: verify keyboard focus order across nav toggle, top-bar controls, page support-strip actions, and status-bar actions.

## Notes

- This module owns shell-level layout decisions. Table-system planning remains in `frontend.md` until a dedicated table module is needed.
- If a page truly needs an in-body title again, record the reason in `decisions.md` instead of silently drifting back to per-page title bars.
- This module is complete; remaining validation is feature-level sign-off rather than additional shell-layout implementation.
