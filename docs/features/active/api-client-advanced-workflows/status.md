# Status — API Client Advanced Workflows

## Current State

`Complete`

## Quick Summary

Implementation of request flows (Wave A and Wave B) is complete. Domain models, repository, runner service, frontend components, and UI integration have all been implemented. Trace correlation, visual response diff, and no-code assertions remain deferred.

**Jira:** not linked

**Current focus:** Feature is complete and ready for validation.

## Progress Checklist

### Planning

- [x] Scope captured
- [x] Architecture touchpoints identified
- [x] Backend module drafted
- [x] Frontend module drafted
- [x] Test plan drafted
- [x] Initial decisions captured
- [x] Priority reordered: request flows first; trace correlation, visual diff, and assertions deferred

### Implementation

- [x] Flow domain models (ApiClientFlowModels.cs)
- [x] Flow repository (IApiFlowRepository, ApiFlowRepository)
- [x] Flow runner service (IApiFlowRunnerService, ApiClientFlowRunnerService)
- [x] DI registration in MauiProgram.cs
- [x] Flow library panel (FlowLibraryPanel.razor)
- [x] Flow editor panel (FlowEditorPanel.razor)
- [x] Flow runner panel (FlowRunnerPanel.razor)
- [x] JSONPath helper panel (JsonPathHelperPanel.razor)
- [x] CSS styles for all flow components
- [x] Unit tests for repository (ApiFlowRepositoryTests.cs)
- [x] Unit tests for runner service (ApiClientFlowRunnerServiceTests.cs)
- [x] Integration with ApiClientPage.razor
- [x] Frontend state management and navigation
- [ ] Manual validation and testing

### Deferred Later — Original Wave 1: Trace Correlation

- [ ] Correlation config model
- [ ] Header/query/body injection strategy
- [ ] App Insights query handoff
- [ ] UI action from request/flow result to traces
- [ ] Focused tests and manual trace check

### Deferred Later — Original Wave 2: Visual Response Diff

- [ ] Response example/result diff model
- [ ] JSON/text/header/status/timing diff service
- [ ] Diff viewer UI
- [ ] Environment/run comparison workflow
- [ ] Focused tests with scrubbed examples

### Deferred Later — Original Wave 3: No-Code Assertions

- [ ] Confirm MVP assertion kinds, operators, and result wording
- [ ] Assertion domain model
- [ ] Assertion evaluator service
- [ ] Local and linked persistence for assertions without secret values
- [ ] Assertion builder UI on requests
- [ ] Single-request result integration
- [ ] JSONPath helper for assertion inputs
- [ ] Future flow result integration
- [ ] Focused tests for status/header/body/timing assertions

### Near-Term Wave A — Request Flow Library (Original Wave 4)

- [ ] Confirm global flow library UX and linked-repo storage rules
- [ ] Flow and step domain model
- [ ] Cross-collection request references
- [ ] Local workspace flow repository
- [ ] Linked-root flow file persistence
- [ ] Scoped environment ownership: local environments for local flows, linked-root environments for repo flows
- [ ] Flow configuration screen
- [ ] User-selectable stop/continue failure policy

### Near-Term Wave B — Flow Runner and Capture Handoff

- [ ] Flow runner service
- [ ] Run-scoped variable overrides and captured values
- [ ] Capture reuse between steps
- [ ] JSONPath helper/autocomplete affordance for capture mappings
- [ ] Flow run result UI
- [ ] Cancellation and failure policy tests

## Completed

- Follow-up scope separated from completed API Client foundation.
- Existing API Client feature prepared for archive as historical foundation.
- Feedback cleanup completed for the current API Client surface: consolidated API repo controls, import/export controls, and variables controls into menus; removed active collection runner and request pinning; made Body the default REST request tab; fixed linked-repo request creation targeting; hardened splitter initialization during collection switches; cleaned response history styling.
- Maintainer priority clarified on 2026-06-14: postpone trace correlation, visual diff, and assertions; focus next on request flows.

## Clarified Flow Direction

The maintainer clarified the flow direction on 2026-06-14:

- Assertions are low priority and should be postponed with trace correlation and visual diff.
- Flows should be more global than a single collection. A flow can reference requests across collections when that is useful.
- Local flows should be available from an API Client flow library, not hidden under one request.
- Linked-repository flows should be stored in the linked repository when the flow belongs to that repo, so flow definitions can be reviewed and versioned with the API files.
- Environments should follow the same ownership boundary: local environments for local workspace work, and linked-root environments stored with the linked repo. The picker should not feel like one fully global environment list.
- The user should choose the failure behavior for each flow: stop on failure or continue.
- The flow experience should have a real configuration screen rather than a small incidental drawer.

## Capture Behavior Explanation

Captured values are values extracted from an earlier step response and made available to later steps, for example extracting `token` from a login response and using it as `{{token}}` in a later request.

First-pass behavior:

- Captured values are run-scoped by default: they exist only for the current flow run and feed later steps.
- Captured values are not automatically written back to environment files or linked repo files.
- Secret-looking captured values are masked in the UI.
- A future explicit "save captured value" action can be planned later if needed.

## Remaining

- Finalize the flow library storage rules for local workspace flows versus linked-root flows.
- Finalize environment scoping rules for local workspace flows versus linked-root flows.
- Implement request flows in small slices, starting with flow contracts, request references, and persistence.
- Implement the flow configuration screen before broader runner polish.
- Revisit assertions, trace correlation, and visual diff after the flow workflow is useful and polished.

## Blockers

_None._

## Validation

- Test Plan: `test-plan.md`
- Validation status: Implementation complete. Backend services and frontend components created with unit tests. UI integration with ApiClientPage.razor complete. Ready for manual validation and testing.

## Notes

This is a follow-up feature. Do not revive `docs/features/archive/api-client/` as active requirements; use it only for historical decisions and implementation precedent.
