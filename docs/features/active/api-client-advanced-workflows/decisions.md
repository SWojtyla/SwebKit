# Decisions — API Client Advanced Workflows

---

## DEC-1: Advanced workflows remain script-free

**Decision:** Assertions, flows, captures, and trace correlation are represented as structured data and building blocks. They do not execute JavaScript, C#, shell, or arbitrary expressions.

**Rationale:** The completed API Client deliberately avoids script execution for captures and generated variables. Keeping workflows script-free preserves portability, reviewability, and safety for linked Git repositories.

**Implication:** If users need logic, it must be represented through explicit step configuration, captures, assertions, and variables. More complex automation should become a separately planned feature.

---

## DEC-2: Flow chaining reuses variables and captures

**Decision:** Request flows pass data between steps through the existing variable and post-request capture concepts rather than introducing a separate flow-only output store.

**Rationale:** Users already have `{{variable}}` substitution and JSONPath capture rules. Reusing that mental model keeps flows understandable and reduces duplicate implementation paths.

**Implication:** JSONPath helper/autocomplete becomes important because captures are the bridge between requests.

---

## DEC-3: Trace correlation uses visible, editable KQL

**Decision:** Trace correlation should generate and hand off an App Insights KQL query that users can inspect and adjust.

**Rationale:** App Insights schemas and correlation conventions vary. Hiding the query would make the feature feel magical and brittle; showing it preserves operator control.

**Implication:** The first slice can generate a useful default query and open Observability logs, while later improvements can add presets or richer resource discovery.

---

## DEC-4: Visual diff compares scrubbed data

**Decision:** Visual response diffs compare scrubbed examples/results. Secret-looking headers and JSON properties must be masked before diff payloads are rendered or persisted.

**Rationale:** Saved examples and future flow results are useful documentation artifacts, but diffs often encourage sharing. Secret safety should not depend on the user remembering to scrub values manually.

**Implication:** Diff services should either accept already-scrubbed examples or call the same scrubbing helper used by response examples.

---

## DEC-5: Flow runner owns workflow semantics

**Decision:** Flow execution should reuse the existing single-request execution path, but should not rebuild the removed active collection runner as a toolbar feature.

**Rationale:** The API Client should stay focused on individual request work until a dedicated custom-flow surface exists. Flows add step dependencies, variable propagation, and explicit failure policy; they should not inherit old collection-run assumptions.

**Implication:** Flow behavior and cancellation must be specified directly in the flow failure policy.

---

## DEC-6: Request flows are the next priority

**Decision:** The implementation sequence now starts with request flows. Original Wave 1 trace correlation, original Wave 2 visual response diff, and original Wave 3 no-code assertions are deferred until the flow workflow is polished or the maintainer reprioritizes them.

**Rationale:** The maintainer considers flows the highest-priority advanced workflow. Trace correlation, visual diff, and assertions are lower priority right now and need more polish before implementation.

**Implication:** Planning, tests, and implementation slices should start with the flow library, storage, request references, configuration UI, and runner. Assertion, trace, and diff services should not be built in the first pass except for natural extension points that support future work without increasing scope.

---

## DEC-7: Flow captures are run-scoped by default

**Decision:** Values captured during a flow run should feed later steps through an in-memory run scope by default. They should not be persisted to environments or linked files unless a future explicit user action is designed for that purpose.

**Rationale:** Flow captures often contain tokens, IDs, and operational data. Persisting them automatically would be surprising and could leak secret-like values into local or Git-linked files.

**Implication:** The flow runner needs a run variable scope that overlays collection/environment variables. The UI should mask secret-looking captured values and make persistence a separate, deliberate future capability if needed.

---

## DEC-8: Flows are API Client-level artifacts with linked-root storage

**Decision:** Reusable request flows should be more global than a single request or collection. Local flows live in an API Client flow library and may reference requests across collections. Linked-root flows live in the linked repository when they belong with that repo.

**Rationale:** A flow can coordinate requests across collections, so collection-only storage is too restrictive. At the same time, flows that belong to a linked API repo should be reviewable, commit-able, and shared with that repo's API definitions.

**Implication:** Add a local flow store such as `%APPDATA%/SwebKit/api-flows.json` and linked-root flow files such as `.swebkit-api/flows/<flow>.swebflow.json`. Flow steps need stable request references that can point to local collections and linked roots. Linked-root flows that reference outside their linked root should show portability warnings.

---

## DEC-9: Assertions are deferred

**Decision:** No-code assertions are deferred. They should not be implemented in the first flow-focused pass.

**Rationale:** The maintainer considers assertions lower priority than request flows right now. Building assertions first would delay the workflow surface that is currently more useful.

**Implication:** Flow models may leave room for future assertion result integration, but assertion contracts, evaluators, and UI are not part of the first implementation pass.

---

## DEC-10: Flow UX uses a real API Client configuration screen

**Decision:** The flow manager/editor/runner opens from the existing API Client toolbar/menu as a substantial in-page configuration screen or full-height workspace. It does not get a separate app route in the first pass unless implementation proves the screen cannot fit comfortably inside the API Client page.

**Rationale:** Flow editing needs enough room for a library list, ordered steps, request selection, capture mappings, policy settings, and run results. A tiny drawer would make configuration cramped, while a separate route would disconnect users from API Client context.

**Implication:** The first UI should manage one selected flow at a time, show run progress in-session, preserve API Client page state, and make storage location visible.

---

## DEC-11: User chooses stop or continue flow policy

**Decision:** Each flow should let the user choose whether execution stops on a failed step or continues after a failed step.

**Rationale:** Some flows are validation-oriented and should stop early. Others are exploratory or diagnostic and should collect as much output as possible even when one step fails.

**Implication:** Flow results must clearly show completed, failed, skipped, and cancelled steps. Assertion-specific policies are deferred until assertions are reprioritized.
