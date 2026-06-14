# API Client Advanced Workflows

## Goal

Extend the completed API Client with higher-level request workflows: trace correlation into App Insights, visual response/result diffs, no-code assertions, and request flows that chain outputs into later requests.

## Value

The API Client now covers individual request authoring, execution, Git-linked storage, variables, examples, and collection runs. The next user value is investigative and workflow depth: send a request, correlate it with telemetry, compare behavior across environments, validate expected outcomes, and run dependent request chains without writing scripts.

## Scope

### Wave 1 — Trace Correlation

- Add a correlation ID strategy for API requests.
- Let users inject or generate a correlation value into headers/query/body through existing variables.
- Provide a jump from a request or runner result into App Insights logs filtered by that correlation value.
- Keep the trace query advisory and editable rather than hiding KQL from the user.

### Wave 2 — Visual Response Diff

- Compare saved response examples against each other.
- Compare runner results across environments or runs.
- Render structured JSON/text differences with status, headers, timing, and body sections.
- Preserve existing response-size and secret-scrubbing safeguards.

### Wave 3 — No-Code Assertions

- Add assertion definitions to requests without scripting.
- Support status code, header presence/value, JSONPath body checks, response time, and body contains checks.
- Show pass/fail results in single request execution and collection/flow runner results.
- Keep assertions data-only and portable in local and linked collection files.

### Wave 4 — Request Flows

- Add reusable flows made of ordered request steps.
- Allow a step to run an existing request and capture values for later steps.
- Reuse post-request capture rules and variables as the primary data handoff mechanism.
- Add JSONPath helper/autocomplete affordances so users can build captures and assertions without guessing paths.
- Run flows with cancellation, per-step result state, and clear failure policy.

## Non-Goals

- No JavaScript, C#, shell, or arbitrary pre-request scripts.
- No hosted collaboration or cloud sync.
- No full OpenAPI import/export in this feature.
- No PR creation or remote Git workflow expansion.
- No automatic cookie jar unless it is planned as a separate feature.
- No replacement for the existing collection runner; flows build on it where possible.

## Dependencies

- Existing API Client architecture: `docs/architecture/functionalities/api-client.md`
- Existing Observability/App Insights behavior: `docs/architecture/functionalities/observability.md`
- Existing API Client files under `src/SwebKit.App/Components/ApiClient/`
- Existing API Client services under `src/SwebKit.Core/Services/`
- `JsonPath.Net` already used for capture rules
- App Insights/KQL query support through `SwebKit.Observability`
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md`

## Risks & Mitigations

| Risk                                            | Mitigation                                                                                                |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Flow chaining becomes scripting by another name | Keep flow steps declarative: request reference, variable overrides, captures, assertions, failure policy. |
| Trace correlation differs by API convention     | Support configurable header/query/body token names and show generated KQL before running.                 |
| JSONPath capture/assertion UX is hard to use    | Add helper from latest response/example body, path suggestions, and test-against-response affordance.     |
| Diffing large responses hurts UI performance    | Reuse response caps, lazy expansion, and structured section-level diff before full body diff.             |
| Secrets leak through examples/diffs/flow logs   | Reuse response example scrubbing and mask secret-backed variables in all workflow surfaces.               |
| Runner/flow cancellation leaves stale UI state  | Follow BL-7 cancellation and per-run result ownership; cancel active execution on dispose/navigation.     |

## Related Documents

- Architecture: `docs/architecture/functionalities/api-client.md`
- Observability: `docs/architecture/functionalities/observability.md`
- Archived foundation: `docs/features/archive/api-client/summary.md`
- Backend module: `backend.md`
- Frontend module: `frontend.md`
- Decisions: `decisions.md`
- Test plan: `test-plan.md`

## Quick Links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Backend: `backend.md`
- Frontend: `frontend.md`
- Decisions: `decisions.md`
