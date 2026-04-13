# Frontend Plan - environment-and-configuration-health

---

title: "Frontend Plan - environment-and-configuration-health"
owner: "GitHub Copilot"
status: "Review"

---

## Goal

Expose readiness in the shell and settings experience so operators can see setup progress, health state, and configuration gaps before they hit broken runtime pages.

## Impacted areas

- Current readiness and configuration surfaces:
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Shared/HealthTile.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- Feature settings forms likely to receive readiness CTAs or inline health:
- `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Pages/DevOpsConfigForm.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor`
- Supporting shell/config context:
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`
- Tests:
- `tests/SwebKit.App.Tests/`
- `tests/SwebKit.E2E.Tests/AppUiTests.cs`

## UX notes

- Target user flow:
- Open the app for the first time and immediately see what is missing for an Azure-focused operator workflow.
- Move from checklist item to the correct Settings section without guessing which page owns the config.
- Return to the dashboard or readiness view and see that a previously missing area is now configured or ready.
- See which capability areas are configured, missing prerequisites, or blocked without reading raw JSON.
- Presentation rules:
- The first-run checklist should be action-first, not explanatory prose first.
- Health states must distinguish `Not configured`, `Configured but not ready`, `Ready`, `Warning`, and `Error`.
- Configuration-gap summaries should highlight meaningful differences and next steps, not create a giant wall of config text.
- Readiness copy must never imply a secret value is visible or stored in plain text.
- Accessibility expectations:
- Health state should never rely on color alone.
- Checklist items and readiness rows should expose meaningful labels and keyboard focus targets.
- CTA handoff into Settings must be screen-reader and keyboard friendly.

## API / contract changes

- Frontend should consume one aggregated readiness/health report rather than piecing together status from multiple page-local heuristics.
- Settings sections should accept focused CTA handoff targets where useful, following the pattern already used by Incident Timeline.
- Dashboard health surfaces should evolve from simple metric tiles into a combination of readiness, checklist, and live status summaries without losing quick-scan value.

## Tasks

### Wave 1 - First-run checklist and setup handoff [blazor-expert] (depends on `shell-ux-foundation`)

- [x] Add a first-run or not-ready checklist surface to the dashboard or a closely related shell view.
- [x] Deep-link checklist items into the correct Settings section.
- [x] Keep the checklist derived from real config/readiness state instead of from separate wizard state.
- [x] Make the checklist useful for both brand-new and partially configured setups.

### Wave 2 - Health overview and status language [blazor-expert] (depends on Wave 1)

- [x] Add a richer connection/configuration health overview to replace or extend the current dashboard-only tiles.
- [x] Clarify the difference between "configured" and "ready" in the UI.
- [x] Reuse shell-level status and CTA patterns from `shell-ux-foundation`.
- [x] Surface partial failures without collapsing the whole readiness view.

### Wave 3 - Readiness detail and configuration-gap drill-through [blazor-expert] (depends on Waves 1-2)

- [x] Add operator-readiness detail for Azure-facing flows that need CLI/credential/resource configuration.
- [x] Keep readiness data focused on actionable differences, not raw model dumps.
- [x] Ensure production-marked configurations remain visually explicit during readiness review.

### Wave 4 - Validation and adoption [blazor-expert] (depends on Waves 1-3)

- [x] Add component coverage for checklist states, readiness cards, configuration-gap rows, and settings handoff.
- [ ] Add E2E coverage for first-run and partially configured flows.
- [x] Capture final copy and handoff decisions in `decisions.md`.

## Validation

- Component tests: `ConfigurationReadinessComponentsTests` added and passing.
- Manual UX checks:
- Verify first-run experience does not require reading implementation details to understand next steps.
- Verify readiness and configuration-gap views remain understandable with both partial success and partial failure states.
- Verify no UI element exposes secret content while explaining credential health.
- End-to-end readiness coverage still depends on the existing Playwright / Windows App SDK launch path becoming reliable in this environment.

## Notes

- Relevant pitfalls from `docs/pitfalls/blazor-maui.md`:
- BL-2 - readiness refresh UI must dispatch renders safely after async probe completion.
- BL-5 - avoid duplicate health loads from parent rerenders on settings or dashboard pages.
- BL-11 - keep shared health/checklist styles in the right component or global style scope.
- Focused bUnit coverage now targets extracted readiness components instead of the route pages because the current app test project still does not materialize `DashboardPage` and `SettingsPage` reliably.
- Relevant pitfalls from `docs/pitfalls/dotnet-csharp.md`:
- CS-2 - readiness refresh and explicit retry flows must propagate cancellation instead of reporting false errors.
