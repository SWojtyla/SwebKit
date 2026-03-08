# Technical Plan — Foundation MVP: UI

## Status

- Current: In Progress

## Component Hierarchy

```
App.razor
└── Routes.razor
    └── MainLayout.razor
        ├── LeftNav
        ├── TopBar
        │   └── CommandPalette
        └── [page slot]
            ├── ServiceBusPage
            ├── ObservabilityPage
            ├── AksPage
            ├── ProjectsPage
            └── SettingsPage
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-1** (`_Imports.razor`), **BL-2** (`InvokeAsync`), **BL-3** (guard before `await`), **BL-4** (`@if` destroy/recreate), **BL-5** (`OnParametersSetAsync` frequency).

## Implementation Sequence

1. Finalize MAUI Blazor shell composition (`MainPage.xaml`, `MauiProgram.cs`).
2. Complete page scaffolds with environment switching behavior.
3. Complete `AppStateService` integration across all pages.
4. Add keyboard shortcut wiring and command palette baseline.
5. Validate production safety badge in `TopBar`.

## Detailed Tasks

- [ ] Finalize MAUI Blazor shell composition.
  - Files: `src/SwebKit.App/MainPage.xaml`, `src/SwebKit.App/MauiProgram.cs`, `src/SwebKit.App/Components/*`
- [ ] Complete page interaction wiring and environment switching behavior.
  - Files: `src/SwebKit.App/Components/Pages/*`
- [ ] Ensure `_Imports.razor` covers all component subdirectory namespaces.
  - Files: `src/SwebKit.App/Components/_Imports.razor`
- [ ] Validate keyboard shortcut handlers.
  - Files: `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`

## Acceptance Checks

- [ ] App launches and shows baseline navigation.
- [ ] Project and environment switching updates all pages consistently.
- [ ] Production safety badge appears in `TopBar` for production environments.
- [ ] Command palette opens and closes correctly.
- [ ] No RZ10012 warnings for components in subdirectories.

## Traceability Backlinks

- `docs/features/foundation-mvp/index.md`
- `docs/features/foundation-mvp/technical-plan-backend.md`
- `docs/features/foundation-mvp/test-plan.md`
