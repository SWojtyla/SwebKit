# Backend — API Client Advanced Workflows

## Scope

Backend work now prioritizes request flows. Trace correlation, response diffs, and assertions remain planned later, but the first implementation should not build their full services or UI until the flow experience is polished.

The backend must build on the completed API Client foundation rather than introducing a parallel request execution or scripting model.

## Architecture Touchpoints

- Project: `src/SwebKit.Core/`
  - `Domain/ApiClientModels.cs` or a new focused models file for flow definitions, request references, and flow run results.
  - `Services/ApiClientWorkflowService.cs` for request helper reuse where appropriate.
  - Existing request execution services for single-request reuse.
  - New services likely needed: `ApiClientFlowRunnerService`, `ApiClientFlowRepository`, and a small capture-evaluation helper if current capture execution is too coupled to persistence.
- Project: `src/SwebKit.App/`
  - DI registration in `MauiProgram.cs`.
- Persistence:
  - Local workspace flows should live in a dedicated API Client flow store, for example `%APPDATA%/SwebKit/api-flows.json`, because flows can reference requests across collections.
  - Linked-root flows should live under the linked API root, for example `.swebkit-api/flows/<flow>.swebflow.json`, when the flow belongs to that repo.
  - Repo-stored flows should prefer references to requests inside the same linked root. Cross-root or local references are allowed only with explicit unresolved/external-reference warnings because they reduce portability.
- Environment ownership:
  - Local environments remain app-local and are used by local flows and local requests by default.
  - Linked-root environments remain under `.swebkit-api/environments/*.swebenv.json` and are used by flows/requests owned by that linked root by default.
  - Cross-root or local-environment usage from a linked-root flow should be explicit and warning-backed because it reduces portability.

## Proposed Domain Shapes

| Model                     | Purpose                                                                                                                    |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `ApiFlowDefinition`       | API Client-level or linked-root flow: name, description, storage scope, ordered steps, default environment, failure policy |
| `ApiFlowStorageScope`     | Local workspace or linked root storage metadata                                                                            |
| `ApiRequestReference`     | Stable reference to a request in a local collection or linked root                                                         |
| `ApiEnvironmentReference` | Stable reference to a local environment or linked-root environment used by a flow or step                                  |
| `ApiFlowStep`             | Request reference, variable overrides, capture mappings, and optional per-step display metadata                            |
| `ApiFlowVariableOverride` | Per-flow or per-step variable value that participates in existing substitution scope                                       |
| `ApiFlowCaptureMapping`   | Data-only capture mapping from a response source to a run-scoped variable                                                  |
| `ApiFlowFailurePolicy`    | User-selected stop or continue behavior for failed requests                                                                |
| `ApiFlowRunResult`        | Per-step results, captured variables, failed/skipped/cancelled state, and warnings                                         |

## Design Decisions

| #   | Decision                                      | Rationale                                              | Alternative considered                                      |
| --- | --------------------------------------------- | ------------------------------------------------------ | ----------------------------------------------------------- |
| 1   | Flows are first priority                      | They provide immediate workflow value                  | Starting with assertions rejected as lower priority for now |
| 2   | Flows are API Client-level artifacts          | They may reference requests across collections         | Collection-only flows rejected as too restrictive           |
| 3   | Linked-root flows are stored in the repo      | Repo-owned flows should be reviewable/versioned        | Storing all flows only in app-local state rejected          |
| 4   | Flow outputs reuse capture/variable semantics | Avoids a second data-passing model                     | Dedicated flow-only output store rejected initially         |
| 5   | Captured values are run-scoped by default     | Prevents accidental secret persistence                 | Auto-writing every flow capture to an environment rejected  |
| 6   | User chooses stop or continue policy          | Different flows are validation-oriented or exploratory | One hard-coded default rejected                             |
| 7   | Environments are scoped by local/repo owner   | Prevents repo-backed requests from using the wrong env | One fully global environment picker rejected                |

## Implementation Tasks

### Near-Term Wave A — Request Flow Library

#### A1 — Flow contracts and request references

- [ ] Add `ApiFlowDefinition`, `ApiFlowStorageScope`, `ApiRequestReference`, `ApiFlowStep`, variable override, capture mapping, failure policy, and run-result models.
- [ ] Add `ApiEnvironmentReference` or equivalent ownership metadata for default flow/step environments.
- [ ] Support request references across local collections and linked roots.
- [ ] Include enough metadata to show unresolved request references clearly when a linked root or local collection is unavailable.
- [ ] Do not copy request definitions into flow files.

#### A2 — Flow persistence

- [ ] Add a local workspace flow repository, likely backed by `%APPDATA%/SwebKit/api-flows.json`.
- [ ] Add linked-root flow read/write support under `.swebkit-api/flows/`.
- [ ] Add collision-safe create/rename behavior for flow files.
- [ ] Add serialization tests for local flows, linked flows, cross-collection references, and unresolved references.
- [ ] Add tests proving linked flow files do not persist secret values or captured runtime values.

#### A3 — Flow discovery and ownership

- [ ] Load app-local flows and linked-root flows into one flow library.
- [ ] Clearly mark where each flow is stored: local workspace or linked root.
- [ ] Filter environment choices by flow storage owner by default: local environments for local flows, linked-root environments for repo flows.
- [ ] Warn when a flow step uses an environment outside the flow's storage owner.
- [ ] Warn when a linked-root flow references requests outside its linked root because that flow may not be portable for other users.
- [ ] Include linked-root Git status for changed flow files through the existing linked Git status/action path if feasible.

### Near-Term Wave B — Flow Runner and Capture Handoff

#### B1 — Flow runner service

- [ ] Implement `ApiClientFlowRunnerService` using the existing `IHttpRequestExecutor` path for each request step.
- [ ] Resolve each `ApiRequestReference` to the current local or linked request before execution.
- [ ] Resolve the selected `ApiEnvironmentReference` to a local or linked-root environment before execution.
- [ ] Build per-step variable scope from collection/environment variables, flow overrides, previous captured values, and step overrides.
- [ ] Default captured flow values to run-scoped values that feed later steps but are not persisted.
- [ ] Extract or reuse capture evaluation logic so flow captures and post-request captures do not diverge.
- [ ] Support cancellation tokens and progress callbacks per step.

#### B2 — Failure policy

- [ ] Support user-selected failure policy: stop on failed step or continue after failed step.
- [ ] Treat transport failures, invalid request references, and cancellation as distinct result states.
- [ ] Defer assertion-specific failure policy until assertions are reprioritized.
- [ ] Preserve completed step results when later steps are cancelled or skipped.

#### B3 — Capture handoff

- [ ] Let a step define capture mappings from status, header, body text, or JSONPath result into a run-scoped variable name.
- [ ] Mask secret-looking captured variable names and values in run results.
- [ ] Make capture warnings visible when a mapping fails or returns no value.
- [ ] Do not persist captured values unless a future explicit save action is designed.

#### B4 — Backend validation

- [ ] Unit-test ordered execution, request reference resolution, capture propagation, variable override precedence, failure policy, and cancellation.
- [ ] Add regression tests proving single-request execution still works when no assertions are configured.
- [ ] Add persistence tests for local and linked flow definitions, including cross-collection references.

### Deferred Later — No-Code Assertions

- [ ] Add assertion model and operators.
- [ ] Implement evaluator for status code, header, body contains, JSONPath, response time.
- [ ] Return pass/fail/warning results with user-readable messages.
- [ ] Attach assertion results to single request and future flow results.
- [ ] Serialize assertions in local and linked formats without secrets.

### Deferred Later — Trace Correlation

- [ ] Add correlation config model.
- [ ] Add helper to generate/inject correlation values into headers/query/body using existing variables.
- [ ] Build App Insights KQL query from correlation value, time window, and selected resource.
- [ ] Integrate with Observability route/query handoff.
- [ ] Add tests for query construction and missing-resource behavior.

### Deferred Later — Visual Response Diff

- [ ] Add response/example/result diff service.
- [ ] Support JSON structural diff and text fallback.
- [ ] Include status, header, body, content type, elapsed time, and environment/run metadata.
- [ ] Scrub secret-looking fields before returning diff payloads.
- [ ] Add tests for JSON/text/header/status diffs and secret scrubbing.

## Validation Notes

- Flow runner tests must prove captures from earlier steps feed later steps through variables.
- Flow persistence tests must cover app-local and linked-root storage.
- Request-reference tests must cover cross-collection and missing-reference behavior.
- Assertion tests should cover every assertion operator and failure message when deferred work resumes.
- Trace correlation tests should not require live App Insights when deferred work resumes; live validation is manual.
- Diff service tests must include secret-looking fields and large bodies when deferred work resumes.
- Linked serialization tests must prove no secret values are persisted.
