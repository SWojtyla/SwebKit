# Frontend — API Client Advanced Workflows

## Scope

Frontend work adds investigation and orchestration surfaces to the existing API Client page: trace actions, visual diff viewer, assertion builder/results, flow editor/runner, and JSONPath helper/autocomplete. It should reuse the current `src/SwebKit.App/Components/ApiClient/` folder and keep UI state owned by `ApiClientPage.razor` or focused child components.

## Architecture Touchpoints

- `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`
  - Add entry points or management screens for flows, assertions, diffs, and trace correlation.
- `RequestBuilderPanel.razor`
  - Add assertion tab/section if assertions belong directly on requests.
- `ResponseViewerPanel.razor`
  - Add diff and trace affordances from saved examples/current results where appropriate.
- New candidate components:
  - `AssertionBuilderPanel.razor`
  - `ResponseDiffPanel.razor`
  - `TraceCorrelationPanel.razor`
  - `FlowEditorPanel.razor`
  - `FlowRunnerPanel.razor`
  - `JsonPathHelperPanel.razor`

## UX Surfaces

### Trace Correlation

- Toolbar or response action: `Trace` when a request/result has a correlation value or can generate one.
- Panel shows correlation value, time window, target App Insights resource, and generated KQL.
- User can open Observability logs with the generated query.
- If Observability is not configured, show setup guidance rather than failing silently.

### Visual Response Diff

- Diff entry points:
  - compare two saved response examples on a request
  - compare two runner results
  - compare selected environment runs when metadata is present
- Viewer sections:
  - status and timing
  - headers
  - body summary
  - JSON/text body diff
- Large bodies should use capped previews with explicit load-more behavior.

### Assertions Without Scripting

- Assertion builder uses familiar controls:
  - kind selector: Status, Header, JSONPath, Body contains, Response time
  - operator selector: equals, not equals, contains, exists, less than, greater than
  - target/value inputs
  - enabled toggle
- Results appear in request response and runner/flow result rows with pass/fail/warning badges.
- Invalid assertions should be shown as warnings, not app-breaking errors.

### Request Flows

- Flow list/manager from API Client toolbar.
- Flow editor:
  - ordered step list
  - request picker per step
  - environment and variable overrides
  - capture mappings from response to variables
  - assertion summary per step
  - failure policy selector
- Flow runner:
  - per-step status, elapsed time, assertion result, captured values
  - cancellation
  - latest response/detail inspection

### JSONPath Helper

- Available from capture builder, assertion builder, and flow step capture mapping.
- Can use latest response body or saved example body as sample input.
- Offers tested path result preview.
- Nice-to-have: simple path suggestions from JSON object structure.

## Design Decisions

| # | Decision | Rationale | Alternative considered |
| - | -------- | --------- | ---------------------- |
| 1 | Keep flows in API Client, not a new route initially | Maintains context with collections/environments/results | Separate route deferred unless UI grows too large |
| 2 | Use builder controls over freeform script editors | Matches no-script safety model | Postman-like script editor rejected |
| 3 | Keep KQL visible/editable for trace correlation | Operators need transparency | Hidden telemetry query rejected |
| 4 | Use existing panels and tabs where possible | Avoids another large shell pattern | Brand-new workspace shell deferred |

## Implementation Tasks

- [ ] Add assertion builder component and request integration.
- [ ] Add assertion result rendering in response/runner/flow surfaces.
- [ ] Add response diff panel and example/result selection UI.
- [ ] Add trace correlation panel and Observability handoff action.
- [ ] Add flow manager/editor/runner screens.
- [ ] Add JSONPath helper component and wire it into capture/assertion/flow mapping.
- [ ] Add focused bUnit coverage for key states and validation messages.

## Validation Notes

- bUnit should cover rendering and edit-state transitions for assertion builder, diff panel, flow editor, and trace panel.
- Manual checks are required for App Insights trace handoff.
- Use BL-4/BL-5 guards: lift state that must survive toggles, and guard expensive `OnParametersSet` work.
- CSS for new components should live beside the component unless shared globally.
