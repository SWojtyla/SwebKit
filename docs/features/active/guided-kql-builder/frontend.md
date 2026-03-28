# Frontend Plan - guided-kql-builder

---

title: "Frontend Plan - guided-kql-builder"
owner: ""
status: "Not started"

---

## Goal

Provide an approachable guided Logs query workflow in the Observability UI, while preserving direct KQL authoring for advanced users through a clear fallback mode.

## Impacted areas

- Files and components:
  - `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
  - `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
  - `src/SwebKit.App/Components/Observability/` (new guided builder subcomponents)
  - `src/SwebKit.App/wwwroot/app.css` (shared observability styles)
  - Optional component-scoped styles in `src/SwebKit.App/Components/Observability/*.razor.css`
- Shared app services and state touchpoints:
  - `src/SwebKit.Core/Services/AppStateService.cs`
  - `src/SwebKit.Core/Domain/ObservabilityConfig.cs`
- Test coverage:
  - `tests/SwebKit.App.Tests/`
  - `tests/SwebKit.E2E.Tests/`

## UX notes

- User flows:
  - Happy path: select resource, build query from controls, execute, inspect result grid.
  - Expert path: switch to advanced mode, edit KQL directly, execute.
  - Edge cases: empty results, compile validation failures, provider errors, canceled execution.
- Component states:
  - loading: query in progress and resource changes
  - loaded: result set with metadata
  - error: validation or execution failures with recoverable actions
  - empty: no rows returned with guidance to broaden filters
- Accessibility:
  - Keyboard navigation for all builder controls.
  - Clear labels and helper text for filters/operators.
  - Focus management on mode switch and run action completion.

## API / contract changes

- Consume compiler/validation contracts from `SwebKit.Core` and `SwebKit.Observability`.
- UI-level state model must track:
  - current mode (Guided or Advanced),
  - guided draft definition,
  - advanced KQL text,
  - last compile or execution feedback.
- Backward compatibility:
  - Existing advanced text editor path remains available.
  - Existing saved query workflows remain functional.

## Sequencing and ownership

- Wave 1 owner: [blazor-expert], parallel: partial (after backend contracts are stable)
- Wave 2 owner: [blazor-expert], parallel: yes with backend persistence work
- Wave 3 owner: [blazor-expert], parallel: yes (UX polish and test expansion)
- Review checkpoints: [manual] UX sign-off for mode-switch behavior and accessibility baseline.

## Tasks

### Wave 1 - Guided UI foundation

- [ ] Add guided builder panel in `ObservabilityLogs.razor`.
- [ ] Add controls for table selection, time range, filters, sort, and limit.
- [ ] Show generated KQL preview (read-only) in guided mode.
- [ ] Wire Run action to compile then execute through existing page/provider flow.
- [ ] Add component tests for basic render and successful run path.

### Wave 2 - Advanced fallback UX

- [ ] Add explicit mode toggle between Guided and Advanced.
- [ ] Implement safe handoff rules and user messaging on mode transitions.
- [ ] Persist mode and draft state through app config/state.
- [ ] Add component tests for mode switching, conflict prompts, and persistence restore.

### Wave 3 - Hardening and accessibility

- [ ] Implement polished validation hint presentation and inline error affordances.
- [ ] Ensure loading, empty, and provider error states are clearly distinct.
- [ ] Add keyboard and focus behavior checks.
- [ ] Add e2e tests for guided-first and advanced-fallback journeys.
- [ ] Record any UX tradeoffs in `decisions.md`.

## Validation

- Component tests: Not started
  - Focus: render states, event flow, mode transitions, and guard behavior.
- Manual UX checks: Not started
  - Validate complete guided flow, advanced fallback flow, and restoration behavior.

## Notes

- Apply Blazor lifecycle guardrails from `docs/pitfalls/blazor-maui.md`:
  - Guard `OnParametersSetAsync` to avoid duplicate reloads.
  - Use `InvokeAsync(StateHasChanged)` after async operations.
  - Keep child-component CSS ownership local to avoid isolation leakage issues.
