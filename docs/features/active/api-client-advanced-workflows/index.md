# API Client Advanced Workflows

## Goal

Extend the completed API Client with higher-level workflow depth, starting with a reusable request flow library. Trace correlation into App Insights, visual response/result diffs, and no-code assertions remain in scope, but are deferred until the flow experience is useful and polished.

## Value

The API Client now covers individual request authoring, execution, Git-linked storage, variables, examples, and linked repository workflows. The next user value is orchestration: compose useful request chains, pass captured values between steps, and store those flows locally or with linked API repos when they should travel with repo-backed API definitions.

## Clarified Direction — 2026-06-14

The maintainer wants to postpone the original Wave 1 trace-correlation work, original Wave 2 visual-diff work, and original Wave 3 no-code assertion work. Those ideas need more polish and are lower priority right now.

The near-term plan should focus on the original Wave 4 request-flow capability:

- Reusable request flows that can run ordered steps, reuse captures, and stop or continue based on explicit user-selected failure policy.
- A flow library that is more global than a single collection and can support cross-collection request references.
- Linked-repository flow files when a flow belongs with a linked API root, so those flows can be reviewed, committed, and shared with the API definitions.
- JSONPath helper support because it is the shared usability bridge for capture mappings and future assertions.
- Trace correlation, visual diff, and assertions should only receive extension points that are cheap and natural while building flows; their full UX is deferred.

## Scope

### Near-Term Wave A — Request Flow Library (Original Wave 4)

- Add reusable flows made of ordered request steps.
- Let flows reference requests across collections when the referenced requests are available in the current API Client workspace.
- Store local workspace flows in app-local API Client state.
- Store linked-repo flows under the linked root when the flow belongs to that repo, so flow definitions are versioned with the linked API files.
- Use stable request references that identify local collection requests and linked-root requests without copying request definitions into the flow.
- Provide a dedicated flow configuration screen for editing, ordering, and running flows.

### Near-Term Wave B — Flow Runner and Capture Handoff

- Allow each step to run an existing request with optional environment and variable overrides.
- Reuse post-request capture concepts for passing values from one step to later steps.
- Add JSONPath helper/autocomplete affordances so users can build capture mappings without guessing paths.
- Let the user choose flow failure behavior: stop on failed step or continue after failed step.
- Run flows with cancellation, per-step result state, captured-value preview, and clear skipped/completed/failed states.

### Deferred Later — No-Code Assertions (Original Wave 3)

- Add assertion definitions to requests without scripting.
- Support status code, header presence/value, JSONPath body checks, response time, and body contains checks in the MVP.
- Show pass/fail/warning results after a single request send.
- Make assertion results reusable by future flow step results.
- Keep assertions data-only and portable in local and linked collection files.

### Deferred Later — Trace Correlation (Original Wave 1)

- Add a correlation ID strategy for API requests.
- Let users inject or generate a correlation value into headers/query/body through existing variables.
- Provide a jump from a request or future flow result into App Insights logs filtered by that correlation value.
- Keep the trace query advisory and editable rather than hiding KQL from the user.

### Deferred Later — Visual Response Diff (Original Wave 2)

- Compare saved response examples against each other.
- Compare saved examples and future flow step results across environments or runs.
- Render structured JSON/text differences with status, headers, timing, and body sections.
- Preserve existing response-size and secret-scrubbing safeguards.

## Non-Goals

- No JavaScript, C#, shell, or arbitrary pre-request scripts.
- No hosted collaboration or cloud sync.
- No full OpenAPI import/export in this feature.
- No PR creation or remote Git workflow expansion.
- No automatic cookie jar unless it is planned as a separate feature.
- Do not revive the removed active collection runner. Custom request flows should be planned as a dedicated workflow surface.
- Do not implement trace-correlation or visual-diff UI in the first implementation pass unless the maintainer explicitly reprioritizes it.
- Do not implement no-code assertion UI in the first implementation pass unless the maintainer explicitly reprioritizes it.
- Do not persist flow run history in the MVP; keep run results in-session unless a later requirement asks for saved runs.
- Do not automatically persist captured runtime values to environments or linked files.

## Dependencies

- Existing API Client architecture: `docs/architecture/functionalities/api-client.md`
- Existing Observability/App Insights behavior: `docs/architecture/functionalities/observability.md`
- Existing API Client files under `src/SwebKit.App/Components/ApiClient/`
- Existing API Client services under `src/SwebKit.Core/Services/`
- `JsonPath.Net` already used for capture rules
- App Insights/KQL query support through `SwebKit.Observability`
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md`

## Risks & Mitigations

| Risk                                                                | Mitigation                                                                                                                                 |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| Flow chaining becomes scripting by another name                     | Keep flow steps declarative: request reference, variable overrides, captures, and failure policy.                                          |
| Cross-collection references become brittle                          | Use stable request references with source kind, linked-root identity when applicable, collection identity, and request identity.           |
| Linked flow definitions accidentally reference private local assets | Show unresolved/external reference warnings and prefer linked-root-local references for repo-stored flows.                                 |
| JSONPath capture UX is hard to use                                  | Add helper from latest response/example body, path suggestions, and test-against-response affordance.                                      |
| Captured values accidentally persist secrets                        | Default flow captures to run-scoped values; do not write captures to environments or linked files automatically.                           |
| Flow cancellation leaves stale UI state                             | Follow BL-7 cancellation and per-run result ownership; cancel active execution on dispose/navigation.                                      |
| Trace correlation, diff, and assertion scope distract from priority | Keep trace/diff/assertions as deferred work; add only natural extension points while building flow definitions and flow run result models. |

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
