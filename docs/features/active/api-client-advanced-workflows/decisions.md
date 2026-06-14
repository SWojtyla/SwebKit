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

**Rationale:** Saved examples and runner results are useful documentation artifacts, but diffs often encourage sharing. Secret safety should not depend on the user remembering to scrub values manually.

**Implication:** Diff services should either accept already-scrubbed examples or call the same scrubbing helper used by response examples.

---

## DEC-5: Flow runner builds on collection runner semantics

**Decision:** Flow execution should reuse the existing request execution path and collection runner result patterns wherever possible.

**Rationale:** The completed collection runner already handles sequential execution, cancellation, per-request results, and skipped WebSocket requests. Flows add step dependencies and variable propagation, not a new HTTP engine.

**Implication:** Any divergence from collection runner behavior must be explicit in the flow failure policy.
