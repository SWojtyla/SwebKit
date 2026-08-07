---
status: Proposed
---

# API Client UX Improvements

## Scope

A focused pass over the React/Tauri API client to remove small but persistent friction points and unify the variable-management UI.

1. **Resizable, remembered Environment Manager** — the current fixed `800x600` dialog is too small on some screens; users should be able to resize both the outer dialog and the internal env-list/editor split, with sizes persisted.
2. **Fix Key Vault variable row overflow** — selecting `AzureKeyVault` currently introduces a horizontal scrollbar; the row should wrap cleanly.
3. **Generated variables with kind/configuration** — environment variables can be generated, but the UI only exposes a source label without letting the user pick `Guid`, `Integer`, `Faker`, etc. and their parameters.
4. **Unify collection and environment variable UI** — `CollectionVariableEditor` and `EnvironmentManager` use different layouts for essentially the same job; share a single `VariableList` component.
5. **Clear collection import flow** — Postman/Bruno/SwebKit import exists in Core and MAUI but is missing from the React app; expose it with a visible button and import dialog.
6. **Pre/post request actions** — allow users to attach lightweight actions that run before and after a request, starting with `CopyToClipboard`, `Delay`, and `LogMessage`.
7. **JSONPath selector/helper for capture rules** — provide a tree-style picker so users can click a response JSON sample and generate/validate a JSONPath expression instead of typing blind.

## Outcomes

- Users can resize and persist the Environment Manager.
- Key Vault rows no longer scroll horizontally.
- Generated environment variables offer the same controls as collection variables.
- Collection and environment variable editors look and behave the same.
- Users can import Postman (`v2.1`), Bruno (folder), and SwebKit (`.sweb.json`) collections without leaving the API client page.
- Users can configure pre/post actions on a request and see feedback when they run.
- Capture-rule JSONPath has a guided picker with live preview.

## Dependencies

- `web/src/components/ui/ResizablePanels.tsx` (existing, persisted panel widths).
- `web/src/lib/tauri-bridge.ts` file/folder dialogs and clipboard.
- `src/SwebKit.Core/Services/CollectionImportService.cs` (existing, not yet exposed in sidecar).
- `src/SwebKit.Core/Services/PostRequestCaptureExecutor.cs` JSONPath evaluation (`Json.Path`).
- `src/SwebKit.Core/Services/VariableGeneratorService.cs` (already supports all generator kinds for environment variables).

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Architecture context: `docs/architecture/architecture.md`, `docs/architecture/design.md`
