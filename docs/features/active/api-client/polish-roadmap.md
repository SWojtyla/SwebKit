# Post-Phase-10 Polish Roadmap — API Client

## Intent

The API Client has a solid feature base after Phases 1–10. The next work should make everyday use feel safer, clearer, and faster before adding another large capability surface.

This roadmap prioritizes workflow trust over breadth: users should always know where changes will be saved, what will be committed, what variables resolved to, and what changed on disk.

## Principles

- Keep Git actions scoped to SwebKit API files under the linked root.
- Prefer in-app review before external navigation.
- Keep secrets masked in previews, cURL export, response examples, and Git diffs.
- Use no-code building blocks; do not add arbitrary script execution.
- Preserve the single-request model until pinned request state has a clear ownership model.

## Phase 11 — Workflow Trust and Git Review

**Status:** Implemented.

### Goal

Make linked-root workflows obvious and reviewable inside SwebKit.

### Scope

- Add a toolbar target chip showing where new collections and requests will be created: local collection or selected linked repo.
- Add an in-app Git diff preview for changed API files, preferably using Monaco diff when available.
- Split Git panel files into staged and unstaged sections.
- Add a commit preview summarizing staged API files, branch, and target remote before commit/push.
- Replace conflict save errors with explicit actions: Reload from disk, Keep mine, Save as copy.
- Keep Open remote compare as a secondary action, not the primary review path.

### Acceptance

- A user can tell the current save target without opening the tree.
- A user can review API file diffs before committing without leaving SwebKit.
- External edit conflicts never end at a passive error banner.
- No Git command can affect paths outside the linked API root.

## Phase 12 — Request Portability and Variable Clarity

**Status:** Implemented.

### Goal

Make requests easy to move between docs, terminals, tickets, and SwebKit while reducing variable-resolution guesswork.

### Scope

- Add Copy as cURL for REST and GraphQL requests.
- Mask or omit secret-backed header/query/body values in cURL by default; offer explicit local-only unmasked copy with confirmation if needed.
- Add Import from cURL to create a request from a pasted command.
- Add a variable inspector panel for the active request.
- Show variable source: environment, collection, generated, credential store, Key Vault, or unresolved.
- Show generated-variable sample values with refresh, but never persist samples.

### Acceptance

- cURL export never leaks secrets by default.
- cURL import creates method, URL, headers, query, and body consistently.
- Variable inspector explains every token used by the selected request.

## Phase 13 — Workspace Depth and Documentation Value

**Status:** Implemented.

### Goal

Make the API Client better for repeated day-to-day work and API documentation.

### Scope

- Add pinned request tabs for a small number of open requests.
- Keep per-request dirty, response, subscription, and WebSocket lifecycle state isolated.
- Persist pinned request IDs per session or UI state scope after a clear restore model is chosen.
- Add saved response examples beside requests.
- Support response example naming, status, headers, body, captured-at timestamp, and source environment name.
- Include response examples in linked roots only when the user explicitly saves them.

### Acceptance

- Switching between pinned requests does not lose unsaved edits or active response context.
- Response examples are useful in Git review and do not contain secret values.
- Large examples follow existing response-size display caps.

## Phase 14 — Collection Runner Later

**Status:** Implemented.

### Goal

Add batch execution only after individual request, variable, and Git workflows are trustworthy.

### Scope

- Run a folder or collection sequentially.
- Show per-request status, elapsed time, response size, and capture warnings.
- Reuse existing auth, variable substitution, capture rules, and response caps.
- Add simple no-code assertions only if the runner needs pass/fail semantics.
- Do not add pre-request scripts or arbitrary code execution.

### Acceptance

- Runner cancellation is reliable.
- One failed request does not corrupt later request state.
- Results are reviewable without storing secret values.

## Recommended Delivery Order

1. Target chip and conflict actions. (done)
2. In-app Git diff preview and staged/unstaged Git layout. (done)
3. Copy as cURL and variable inspector. (done)
4. Import from cURL. (done)
5. Pinned request tabs. (done)
6. Saved response examples. (done)
7. Collection runner. (done)

## Validation Strategy

- Unit tests for cURL parsing/export masking, Git path scoping, conflict action state, and response example serialization.
- bUnit tests for target chip, variable inspector, diff panel states, pinned request state, and conflict prompts.
- Manual checks with at least one local collection and two linked Git roots open at once.
- Manual checks that secret-backed values remain masked in cURL, diffs, examples, and variable inspector surfaces.

## Open Questions

- Should pinned request tabs persist across app restarts or remain session-only initially?
- Should saved response examples live in the same request directory or under a sibling `examples/` folder in linked roots?
- Should Import from cURL support file uploads in the first slice or defer binary/form-data edge cases?
- Should unmasked cURL copy exist at all, or should SwebKit always emit placeholders for secret-backed values?
