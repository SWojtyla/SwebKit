# Test Plan — API Client Advanced Workflows

## Goal

Validate that advanced API Client workflows are safe, deterministic, script-free, and useful for investigation: trace correlation, visual diffs, no-code assertions, and chained request flows.

## Scope

- In scope: workflow domain services, UI state, linked/local persistence, request execution reuse, App Insights handoff, diff rendering, assertions, flow execution.
- Out of scope: hosted collaboration, arbitrary scripts, OpenAPI import/export, cookie jar behavior, PR creation.

## Main Scenarios

1. Trace correlation — Generated correlation ID is injected into a request and available for App Insights query handoff.
2. Visual diff — Two saved response examples can be compared without exposing secret-looking values.
3. Assertions — A request with no-code assertions reports pass/fail details after execution.
4. Runner assertions — Collection or flow run displays assertion results per request/step.
5. Flow chaining — Step 1 captures a JSONPath value and Step 2 uses it through existing variables.
6. JSONPath helper — User can test or select a JSONPath against a saved/latest response body.
7. Cancellation — Cancelling a flow stops remaining steps and preserves completed result state.

## Automated Coverage

### Unit Tests — `SwebKit.Core.Tests`

| Area | Coverage |
| ---- | -------- |
| Trace correlation | Correlation config, generated value, KQL query construction, missing App Insights target handling |
| Diff service | JSON object diff, text diff fallback, header/status/timing diffs, large body cap behavior |
| Assertion evaluator | status code, header, JSONPath, contains, response time, failure messages, invalid JSONPath warnings |
| Flow runner | ordered execution, capture propagation, variable override precedence, failure policy, cancellation |
| Persistence | local and linked serialization for assertions/flows, no secret values persisted |

### Component Tests — `SwebKit.App.Tests`

| Area | Coverage |
| ---- | -------- |
| Trace UI | correlation action appears only when request/result has enough context; generated query is visible/editable |
| Diff UI | examples/results selectable; empty and large-diff states render clearly |
| Assertion UI | adding/removing/editing assertions updates request state and validates required fields |
| Flow UI | step list, request picker, capture mapping, run results, cancellation state |
| JSONPath helper | path suggestions/test result states render without destroying editor state |

## Test Data and Setup

- Use synthetic HTTP responses for unit/component tests.
- Use saved examples with secret-looking fields (`token`, `password`, `Authorization`) to validate scrubbing.
- Use simple JSON payloads for JSONPath coverage, plus malformed JSON for warning paths.
- Manual App Insights trace checks require a configured Observability resource and a test API that emits the correlation ID.

## Manual Checks

- Trace correlation: send a request with correlation header and verify the generated App Insights query opens with the correlation value.
- Visual diff: compare dev vs prod examples and verify changed status/header/body sections are readable.
- Assertions: create one passing and one failing assertion and verify runner output is understandable.
- Flow chaining: run login/get-details style flow where captured token/id from Step 1 feeds Step 2.
- Cancellation: cancel a long flow and verify completed steps remain visible while later steps are skipped.

## Regression Risks & Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Existing collection runner breaks | Reuse `ApiClientCollectionRunnerService` and add regression coverage for plain collection runs. |
| Capture rules and flow outputs diverge | Use one capture/extraction service path for post-request captures and flow variable propagation. |
| Secrets leak into diffs or flow logs | Centralize response/example scrubbing and assert no secret-looking fields persist. |
| Blazor state resets in panels | Follow BL-4/BL-5: lift state to parent, guard parameter refreshes, avoid destructive `@if` toggles where state matters. |

## Acceptance Criteria

- Trace query handoff works with a configured App Insights resource and degrades clearly when not configured.
- Response diffs are readable for JSON and text and do not show secret values.
- Assertions are data-only and run for single requests, collections, and flows.
- Flow steps can pass captured values through variables without scripts.
- Cancellation is reliable and does not corrupt later request state.
- All new persistence writes keep secret values out of local/linkable files.

## Validation Status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by: _pending_
- Date: _pending_
- Conditions: _pending_
