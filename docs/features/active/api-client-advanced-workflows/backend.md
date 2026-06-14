# Backend — API Client Advanced Workflows

## Scope

Backend work covers new workflow/domain services for trace correlation, response diffs, assertions, and request flows. It must build on the completed API Client foundation rather than introducing a parallel request execution or scripting model.

## Architecture Touchpoints

- Project: `src/SwebKit.Core/`
  - `Domain/ApiClientModels.cs` or new focused models file for assertions, flow definitions, diff results, and trace correlation config.
  - `Services/ApiClientWorkflowService.cs` for request helper reuse where appropriate.
  - Existing request execution services for single-request reuse.
  - New services likely needed: `ApiClientAssertionEvaluator`, `ApiClientResponseDiffService`, `ApiClientFlowRunnerService`, `ApiClientTraceCorrelationService`.
- Project: `src/SwebKit.Observability/`
  - App Insights/KQL query handoff support if existing provider abstractions need extension.
- Project: `src/SwebKit.App/`
  - DI registration in `MauiProgram.cs`.
- Persistence:
  - Local `collections.json` and linked `.swebreq.json` request files should carry assertion definitions and saved flow references where appropriate.
  - Flows may need collection-level storage rather than per-request storage.

## Proposed Domain Shapes

| Model                       | Purpose                                                                                         |
| --------------------------- | ----------------------------------------------------------------------------------------------- |
| `ApiRequestAssertion`       | Data-only assertion attached to a request: kind, target, operator, expected value, enabled flag |
| `ApiAssertionResult`        | Result of evaluating one assertion: pass/fail/warning, actual value, message                    |
| `ApiResponseDiff`           | Sectioned diff: status, headers, timing, body summary, body details                             |
| `ApiTraceCorrelationConfig` | Header/query/body token name, generated variable name, App Insights target/query template       |
| `ApiFlowDefinition`         | Named sequence of steps, default environment, failure policy                                    |
| `ApiFlowStep`               | Request reference, variable overrides, capture mappings, assertions, continue-on-failure flag   |
| `ApiFlowRunResult`          | Per-step results, captured variables, assertion summaries, cancellation state                   |

## Design Decisions

| #   | Decision                                      | Rationale                                      | Alternative considered                                        |
| --- | --------------------------------------------- | ---------------------------------------------- | ------------------------------------------------------------- |
| 1   | Assertions are data-only, not scripts         | Keeps API Client safe and portable             | JavaScript/Postman-style scripts rejected                     |
| 2   | Flow outputs reuse capture/variable semantics | Avoids a second data-passing model             | Dedicated flow-only output store rejected initially           |
| 3   | Trace correlation emits editable KQL          | Users can understand and adapt the query       | Hidden one-click telemetry lookup rejected                    |
| 4   | Diff service scrubs/masks before rendering    | Prevents examples/results from leaking secrets | Trusting response examples as already scrubbed is too fragile |

## Implementation Tasks

### Wave 1 — Trace Correlation

- [ ] Add correlation config model.
- [ ] Add helper to generate/inject correlation values into headers/query/body using existing variables.
- [ ] Build App Insights KQL query from correlation value, time window, and selected resource.
- [ ] Integrate with Observability route/query handoff.
- [ ] Add tests for query construction and missing-resource behavior.

### Wave 2 — Visual Response Diff

- [ ] Add response/example/result diff service.
- [ ] Support JSON structural diff and text fallback.
- [ ] Include status, header, body, content type, elapsed time, and environment/run metadata.
- [ ] Scrub secret-looking fields before returning diff payloads.
- [ ] Add tests for JSON/text/header/status diffs and secret scrubbing.

### Wave 3 — No-Code Assertions

- [ ] Add assertion model and operators.
- [ ] Implement evaluator for status code, header, body contains, JSONPath, response time.
- [ ] Return pass/fail/warning results with user-readable messages.
- [ ] Attach assertion results to single request and future flow results.
- [ ] Serialize assertions in local and linked formats without secrets.

### Wave 4 — Request Flows

- [ ] Add flow definition and step models.
- [ ] Implement flow runner using existing request execution path.
- [ ] Reuse post-request capture extraction for step output propagation.
- [ ] Support flow failure policy: stop on failure, continue, or continue only on assertion failure.
- [ ] Add cancellation and per-step progress callbacks.
- [ ] Persist flows locally and in linked roots if linked collection owns them.

## Validation Notes

- Unit tests should cover every assertion operator and failure message.
- Flow runner tests must prove captures from earlier steps feed later steps through variables.
- Trace correlation tests should not require live App Insights; live validation is manual.
- Diff service tests must include secret-looking fields and large bodies.
- Linked serialization tests must prove no secret values are persisted.
