# Frontend Plan - remove-environment-model

---

title: "Frontend Plan - remove-environment-model"
owner: ""
status: "Implemented"

---

## Goal

Remove all user-facing traces of the abandoned local environment/profile concept while preserving the existing shell context, accessibility behavior, and all live feature workflows.

## Impacted areas

- Shell components:
  - `src/SwebKit.App/Components/Layout/TopBar.razor`
  - `src/SwebKit.App/Components/Layout/MainLayout.razor`
  - `src/SwebKit.App/Components/Layout/ShellNavigation.cs`
- Routed pages and settings:
  - `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
  - `src/SwebKit.App/Components/Pages/SettingsPage.razor`
  - `src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor` if copy needs clarification
- Frontend tests:
  - `tests/SwebKit.App.Tests`
  - `tests/SwebKit.E2E.Tests`

## UX notes

- Wave 0 cleanup already removed the visible environment pill from `TopBar` and the environment facts from `IncidentTimelinePage`.
- Remaining UI cleanup is structural rather than visual:
  - remove now-dead `EnvironmentLabel` plumbing from `MainLayout` and `ShellNavigation`
  - remove outdated “per environment” copy in settings and related docs
  - ensure any remaining accessible labels or aria summaries no longer reference the abandoned local model
- Preserve:
  - top bar title, eyebrow, demo badge, production badge, and connection-status badges
  - `h1` semantics used by `FocusOnNavigate`
  - incident-timeline action affordances and scope summary behavior apart from the removed environment field

## API / contract changes

- `ShellRouteContext` no longer needs `EnvironmentLabel` once shell consumers are updated.
- `MainLayout` should stop deriving a shell environment label from `AppState.ActiveEnvironmentName ?? AppState.Config.Name`.
- `IncidentTimelinePage` should construct `IncidentWorkloadScope` without an environment argument once the backend contract changes.

## Tasks

- [x] Remove visible environment/profile labels from shared shell surfaces as preview cleanup.
- [x] Remove remaining `EnvironmentLabel` plumbing from shell components.
- [x] Update settings and incident-timeline copy that still implies environment-scoped local settings.
- [x] Update bUnit/component tests for shell and page header expectations.
- [x] Update E2E selectors and assertions that referenced environment labels.
- [x] Align docs with the simplified single-config UX.

## Validation

- Component tests: Focused shell and incident-timeline coverage passed locally
- Manual UX checks:
  - open dashboard, settings, and incident timeline and confirm no local environment/profile label remains
  - confirm demo and production badges still render when expected
  - confirm navigation focus behavior still targets the routed page `h1`

## Notes

- Do not remove Azure DevOps “environment” wording from pipeline/release screens; that terminology comes from Azure DevOps data, not the abandoned local profile model.
