# Frontend — API Client Advanced Workflows

## Scope

Frontend work now prioritizes the flow library, flow configuration screen, flow runner, and JSONPath helper/autocomplete for capture mappings. Assertion builder/results, trace actions, and visual diff viewer are deferred.

The work should reuse the current `src/SwebKit.App/Components/ApiClient/` folder and keep UI state owned by `ApiClientPage.razor` or focused child components.

## Architecture Touchpoints

- `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`
  - Add entry point for the flow library/configuration screen.
- Existing API Client linked-root panels and Git actions
  - Surface linked-root flow files alongside other linked API files where feasible.
- `ResponseViewerPanel.razor`
  - Reuse latest response body/header data as sample input for capture mapping helpers where useful.
- New candidate components:
  - `FlowLibraryPanel.razor`
  - `FlowEditorPanel.razor`
  - `FlowRunnerPanel.razor`
  - `JsonPathHelperPanel.razor`
  - Later: `AssertionBuilderPanel.razor`, `ResponseDiffPanel.razor`, `TraceCorrelationPanel.razor`

## UX Surfaces

### Flow Library

- Flow library entry point from the API Client toolbar/menu.
- Library list shows all available flows, grouped or filterable by storage location:
  - local workspace flows
  - linked-root flows
- Each flow row shows name, storage location, linked-root/repo badge when applicable, changed-file state when available, and unresolved-reference warnings.
- Users can create a local flow or create a flow inside a selected linked root.
- Cross-collection flows are allowed. The UI should show which collection/root each step references so users understand portability.

### Flow Configuration Screen

- Flow editor:
  - ordered step list
  - request picker per step
  - request source/collection/root indicator per step
  - scoped environment picker and variable overrides
  - capture mappings from response to variables
  - failure policy selector
- Flow runner:
  - per-step status, elapsed time, assertion result, captured values
  - cancellation
  - latest response/detail inspection

UX direction:

- Use a real configuration screen inside the API Client, large enough for a step list, request picker, capture mappings, policy settings, and run results.
- Prefer an in-page full-height panel/workspace over a tiny drawer. A separate app route is still deferred unless the screen becomes too large for the API Client page.
- Group environment choices by owner. Local flows default to local environments; linked-root flows default to environments stored in that linked root.
- If a user selects an environment from outside the flow's owner, show a portability warning.
- Show captured values with secret-looking names masked by default.
- Let the user choose stop or continue behavior per flow.
- Start with one selected flow at a time; do not rebuild the removed active collection runner.

### JSONPath Helper

- Available from flow step capture mapping first; later also from assertion builder.
- Can use latest response body or saved example body as sample input.
- Offers tested path result preview.
- Nice-to-have: simple path suggestions from JSON object structure.

### Deferred Assertions Without Scripting

- Assertion builder uses familiar controls:
  - kind selector: Status, Header, JSONPath, Body contains, Response time
  - operator selector: equals, not equals, contains, exists, less than, greater than
  - target/value inputs
  - enabled toggle
- Results appear in the current response view first, then in future flow result rows.
- Invalid assertions should be shown as warnings, not app-breaking errors.

### Deferred Trace Correlation

- Toolbar or response action: `Trace` when a request/result has a correlation value or can generate one.
- Panel shows correlation value, time window, target App Insights resource, and generated KQL.
- User can open Observability logs with the generated query.
- If Observability is not configured, show setup guidance rather than failing silently.

### Deferred Visual Response Diff

- Compare two saved response examples on a request.
- Compare two saved examples or future flow step results.
- Viewer sections include status, timing, headers, body summary, and JSON/text body diff.
- Large bodies should use capped previews with explicit load-more behavior.

## Design Decisions

| #   | Decision                                            | Rationale                                               | Alternative considered                            |
| --- | --------------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------- |
| 1   | Keep flows in API Client, not a new route initially | Maintains context with collections/environments/results | Separate route deferred unless UI grows too large |
| 2   | Use a real flow configuration screen                | Flow editing needs more room than a small drawer        | Tiny incidental drawer rejected                   |
| 3   | Support local and linked-root flow locations        | Repo-owned flows should be versioned with linked roots  | App-local-only flow storage rejected              |
| 4   | Defer assertions, trace, and diff UI                | Flow workflow is the current priority                   | Building all four workflow surfaces at once       |

## Implementation Tasks

### Near-Term Wave A — Flow Library and Configuration

- [ ] Add flow library screen with local and linked-root flow groups.
- [ ] Add create/edit/delete/rename affordances with visible storage location.
- [ ] Add flow editor with ordered step list and cross-collection request picker.
- [ ] Add scoped environment picker for flow/step execution.
- [ ] Add failure policy selector with stop and continue options.
- [ ] Add capture mapping editor and JSONPath helper entry point.
- [ ] Add unresolved-reference and portability warning states.
- [ ] Add focused bUnit coverage for edit-state transitions and validation messages.

### Near-Term Wave B — Flow Runner

- [ ] Add per-step run result rendering with response status, elapsed time, captured values, and warnings.
- [ ] Add cancellation and skipped-step UI states.
- [ ] Wire JSONPath helper into flow capture mapping.
- [ ] Add focused bUnit coverage for flow editing, run progress, cancellation, and failure policy states.

### Deferred Later

- [ ] Add assertion builder component and request integration.
- [ ] Add assertion result rendering in response/flow surfaces.
- [ ] Add response diff panel and example/result selection UI.
- [ ] Add trace correlation panel and Observability handoff action.

## Validation Notes

- bUnit should cover rendering and edit-state transitions for flow library, flow editor, and flow runner first.
- Assertion builder coverage is deferred until assertions are reprioritized.
- Manual checks for App Insights trace handoff are deferred until trace correlation is reprioritized.
- Use BL-4/BL-5 guards: lift state that must survive toggles, and guard expensive `OnParametersSet` work.
- CSS for new components should live beside the component unless shared globally.
