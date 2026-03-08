---
title: 'Technical Plan â€” Foundation MVP: UI'
owner: ''
status: 'In Progress'
created: '2026-03-08'
updated: ''
---

# Technical Plan â€” Foundation MVP: UI

## Status

- Current: In Progress

## Component Hierarchy

```
App.razor
â””â”€â”€ Routes.razor
    â””â”€â”€ MainLayout.razor
        â”œâ”€â”€ LeftNav
        â”œâ”€â”€ TopBar
        â”‚   â””â”€â”€ CommandPalette
        â””â”€â”€ [page slot]
            â”œâ”€â”€ ServiceBusPage
            â”œâ”€â”€ ObservabilityPage
            â”œâ”€â”€ AksPage
            â”œâ”€â”€ ProjectsPage
            â””â”€â”€ SettingsPage
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-1** (`_Imports.razor`), **BL-2** (`InvokeAsync`), **BL-3** (guard before `await`), **BL-4** (`@if` destroy/recreate), **BL-5** (`OnParametersSetAsync` frequency).

## Implementation Sequence

1. Finalize MAUI Blazor shell composition (`MainPage.xaml`, `MauiProgram.cs`).
2. Complete page scaffolds with environment switching behavior.
3. Complete `AppStateService` integration across all pages.
4. Add keyboard shortcut wiring and command palette baseline.
5. Validate production safety badge in `TopBar`.

## Detailed Tasks

- [ ] Finalize MAUI Blazor shell composition.
- [ ] Complete page interaction wiring and environment switching behavior.
- [ ] Ensure `_Imports.razor` covers all component subdirectory namespaces.
- [ ] Validate keyboard shortcut handlers.

## Acceptance Checks

- [ ] App launches and shows baseline navigation.
- [ ] Project and environment switching updates all pages consistently.
- [ ] Production safety badge appears in `TopBar` for production environments.
- [ ] Command palette opens and closes correctly.
- [ ] No RZ10012 warnings for components in subdirectories.

## Traceability Backlinks

- `docs/features/active/foundation-mvp/index.md`
- `docs/features/active/foundation-mvp/technical-plan-backend.md`
- `docs/features/active/foundation-mvp/test-plan.md`
