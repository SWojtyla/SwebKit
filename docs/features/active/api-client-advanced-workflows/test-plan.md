# Test Plan — API Client Advanced Workflows

## Goal

Validate that advanced API Client workflows are safe, deterministic, script-free, and useful for chained request execution. The near-term validation priority is request flows. Trace correlation, visual diffs, and no-code assertions are deferred.

## Scope

- In scope for the first implementation pass: flow domain/runner/UI, cross-collection request references, local and linked-root flow persistence, JSONPath helper for capture mappings, request execution reuse, cancellation, and user-selected stop/continue failure policy.
- Deferred scope: assertions, App Insights trace handoff, and diff rendering.
- Out of scope: hosted collaboration, arbitrary scripts, OpenAPI import/export, cookie jar behavior, PR creation.

## Main Scenarios

1. Flow library — Local workspace flows and linked-root flows load into one flow library with clear storage labels.
2. Linked flow persistence — A flow created for a linked root is stored under that linked repository and can appear in scoped Git status.
3. Cross-collection flow — A flow can reference requests from more than one collection, with clear source labels.
4. Scoped environments — Local flows default to local environments; linked-root flows default to environments stored in that linked root.
5. External environment warning — A linked-root flow can explicitly use an external/local environment, but the UI warns that the flow is less portable.
6. Flow chaining — Step 1 captures a JSONPath value and Step 2 uses it through existing variables.
7. Run-scoped captures — Captured values feed later steps without being persisted by default.
8. JSONPath helper — User can test or select a JSONPath against a saved/latest response body.
9. Failure policy — User-selected stop/continue policy behaves predictably for request failures.
10. Cancellation — Cancelling a flow stops remaining steps and preserves completed result state.
11. Unresolved references — Missing collections, linked roots, environments, or requests render clear warnings and do not crash the flow screen.

## Automated Coverage

### Unit Tests — `SwebKit.Core.Tests`

| Area                | Coverage                                                                                                          |
| ------------------- | ----------------------------------------------------------------------------------------------------------------- |
| Flow references     | local request reference, linked-root request reference, cross-collection reference, unresolved reference warnings |
| Environment scope   | local environment reference, linked-root environment reference, external environment warning, missing environment warning |
| Flow runner         | ordered execution, capture propagation, variable override precedence, failure policy, cancellation                |
| Persistence         | local and linked serialization for flows, no secret or captured runtime values persisted                          |
| Deferred assertions | status code, header, JSONPath, contains, response time, failure messages, invalid JSONPath warnings               |
| Deferred trace      | correlation config, generated value, KQL query construction, missing App Insights target handling                 |
| Deferred diff       | JSON object diff, text diff fallback, header/status/timing diffs, large body cap behavior                         |

### Component Tests — `SwebKit.App.Tests`

| Area                  | Coverage                                                                                                    |
| --------------------- | ----------------------------------------------------------------------------------------------------------- |
| Flow library          | local/linked grouping, storage labels, create/edit/delete/rename, unresolved reference warnings             |
| Flow UI               | step list, cross-collection request picker, scoped environment picker, capture mapping, run results, cancellation state |
| JSONPath helper       | path suggestions/test result states render without destroying editor state                                  |
| Deferred assertion UI | adding/removing/editing assertions updates request state and validates required fields                      |
| Deferred trace UI     | correlation action appears only when request/result has enough context; generated query is visible/editable |
| Deferred diff UI      | examples/results selectable; empty and large-diff states render clearly                                     |

## Test Data and Setup

- Use synthetic HTTP responses for unit/component tests.
- Use flow samples with secret-looking fields (`token`, `password`, `Authorization`) to validate masking.
- Use linked-root flow samples that reference requests in the same linked root and at least one external/local collection to validate portability warnings.
- Use simple JSON payloads for JSONPath coverage, plus malformed JSON for warning paths.
- Manual App Insights trace checks are deferred until trace correlation is reprioritized.

## Manual Checks

- Flow library: create a local flow and a linked-root flow, then verify the storage location and Git status are clear.
- Cross-collection flow: create a flow that uses requests from two collections and verify source labels are understandable.
- Scoped environments: create local and linked-root environments and verify each flow type defaults to the correct owner group.
- Flow chaining: run login/get-details style flow where captured token/id from Step 1 feeds Step 2.
- Failure policy: verify stop and continue modes with a failing step.
- Cancellation: cancel a long flow and verify completed steps remain visible while later steps are skipped.
- Deferred assertions: create one passing and one failing assertion and verify request/flow output is understandable.
- Deferred trace correlation: send a request with correlation header and verify the generated App Insights query opens with the correlation value.
- Deferred visual diff: compare dev vs prod examples and verify changed status/header/body sections are readable.

## Regression Risks & Mitigations

| Risk                                          | Mitigation                                                                                                              |
| --------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Existing request execution breaks             | Reuse the existing request execution path and add regression coverage for single request execution.                     |
| Capture rules and flow outputs diverge        | Use one capture/extraction service path for post-request captures and flow variable propagation.                        |
| Secrets leak into flow logs or linked files   | Mask secret-looking captured values and assert no secret or captured runtime values persist in flow definitions.        |
| Cross-collection references break portability | Show source labels and unresolved/external-reference warnings, especially for linked-root flow files.                   |
| Environments feel fully global                | Group and filter environments by local/linked-root owner, and warn when a flow uses an external environment.            |
| Deferred work distracts from MVP              | Keep assertion, trace, and diff tests documented but out of the first implementation acceptance gate.                   |
| Blazor state resets in panels                 | Follow BL-4/BL-5: lift state to parent, guard parameter refreshes, avoid destructive `@if` toggles where state matters. |

## Acceptance Criteria

- Flows can be stored locally or in a linked root, with clear storage location and no hidden persistence.
- Flows can reference requests across collections with stable references and clear unresolved-reference warnings.
- Environments are scoped by owner: local for local flows, linked-root environments for repo flows, with warnings for explicit external use.
- Flow steps can pass captured values through variables without scripts.
- Users can choose stop or continue behavior for failed steps.
- Cancellation is reliable and does not corrupt later request state.
- All new persistence writes keep secret values out of local/linkable files.
- Assertion, trace query handoff, and response diff acceptance are deferred until those waves are reprioritized.

## Validation Status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by: _pending_
- Date: _pending_
- Conditions: _pending_
