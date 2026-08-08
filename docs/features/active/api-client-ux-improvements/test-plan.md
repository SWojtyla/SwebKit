# API Client UX Improvements — Test Plan

## Unit / integration tests

### `web`

- `VariableList` renders collection and environment variables identically.
- `GeneratorConfig` updates `kind` and per-kind parameters correctly.
- `RequestActionsPanel` adds, edits, and removes pre/post actions.
- `JsonPathPicker` generates a path when a JSON tree node is clicked and calls `onSelect`.
- `request-action-runner` extracts the correct text from `request`/`response` for `CopyToClipboard`.

### `src-sidecar`

- `POST /api/config/collections/import` with base64 Postman v2.1 payload returns imported collection and environment count.
- Same endpoint with a Bruno folder path invokes `CollectionImportService.ImportBrunoFolderAsync`.
- `POST /api/api-client/evaluate-jsonpath` returns `value` for a valid sample/path and `error` for invalid JSONPath/JSON.

### `tests/SwebKit.Core.Tests`

- No changes expected; existing `CollectionImportService` and `VariableGeneratorService` tests already cover backend logic.

## End-to-end tests (`web/e2e/api-client.spec.ts`)

| Scenario | Steps | Expected |
|----------|-------|----------|
| Environment manager resizes and remembers | Open env manager, resize, close, reopen | Dialog width/height match the resized values |
| Key Vault row fits | Add env var, source = AzureKeyVault | No `scrollWidth > clientWidth` on the row |
| Generated env var config | Add env var, source = Generated, set kind = Integer with min/max | Value persisted and shown after reopen |
| Collection variable parity | Open collection variables | Same row layout as env manager |
| Import SwebKit JSON | Click collection import, choose fixture, import | New collection appears in tree |
| Import Postman v2.1 | Click collection import, choose Postman fixture | New collection appears with requests |
| Pre-request copy action | Add CopyToClipboard action with source = requestUrl, send request | Clipboard contains the resolved URL |
| JSONPath picker | Open capture rules, click picker, pick a node | Capture rule path updates and preview shows value |

## Accessibility

- All new dialogs close with `Escape`.
- New buttons have visible focus rings and `aria-label`s.
- `VariableList` checkboxes are linked to their inputs.

## Demo mode

- Import flow is disabled or shows a relevant notice in demo mode because local storage is synthetic; verify with `setDemoMode` helper.
- JSONPath picker works with demo response examples if present.
