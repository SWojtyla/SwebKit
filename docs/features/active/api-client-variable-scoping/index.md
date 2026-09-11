---
status: Review
---

# API Client Variable Visibility & Environment Scoping

## Scope

Two defects and one model gap found while using the Tauri build as the day-to-day
replacement for Bruno/Postman.

1. **The request body editor had no variable awareness at all.** `{{tokens}}` in the
   CodeMirror body were coloured green by the JSON grammar as ordinary *strings* — the
   same green whether the variable existed or not — with no hover and no way to spot a
   typo. Because `VariableSubstitutionService.Substitute` leaves an unknown token as its
   literal text, an undefined variable was sent to the server verbatim as `{{AUTH_SP}}`
   and came back `400 Bad Request` with nothing in the UI having hinted at the cause.
   The URL field already did this correctly via `VariableInput`, so the app contradicted
   itself.
2. **The cURL panel mixed resolved and unresolved text** — the URL came from the executed
   response, the body and headers straight off the request entry — so the command it
   showed would not reproduce the request if pasted into a shell.
3. **Global and project environments could not both apply.** `ApiEnvironment.CollectionId`
   already distinguished global (`null`) from collection-scoped, and
   `ApiClientUiState.ActiveEnvironmentIdByCollection` already existed and was persisted,
   but React read only the single global `ActiveEnvironmentId`. A value shared by a family
   of environments (`DEV (via APIM)` / `DEV (via POD)` / `DEV (via WAAF POD)`) had to be
   duplicated into every one of them.

Also included, as the presentation half of the same complaint: variable rows truncated
ordinary names (`AUTH_API_ADDRESS` rendered as `AUTH_API_ADDRE`) and cost two lines each.

## Outcomes

- An unresolved variable is impossible to miss before sending: red with a wavy underline
  in the body editor, a hover explaining why, and an always-visible banner naming every
  undefined variable across URL, body and headers — not behind the preview toggle.
- The cURL panel reproduces the request. Key Vault and credential-store values stay as
  `{{TOKEN}}` rather than being expanded into a copy-to-clipboard panel.
- A global environment and a collection-scoped one apply together, the project layer
  overriding the global one, so shared values are defined once.
- Each environment picker offers only environments of its own scope; one scoped to a
  different collection is no longer offered.
- Variable names are legible at real lengths and a variable costs one row, not two.

## Non-goals

- `GraphQlPanel` and `WebSocketPanel` message bodies are plain `<textarea>`s whose content
  **is** substituted at send time, so they share the same blind spot. They need the
  `VariableInput` overlay rather than a CodeMirror extension. Deliberately left out.
- `ApiClientWorkflowService.BuildCurlAsync`, `GraphQlSchemaService` and
  `GraphQlSubscriptionService` still take a single environment. They compile against the
  layered API by passing `[activeEnvironment]`; threading both layers through them is a
  follow-up.

## Dependencies

- `web/src/lib/variableHighlight.ts` — existing three-state classifier (`resolved` /
  `deferred` / `unresolved`), reused rather than duplicated.
- `web/src/styles/globals.css:546-563` — existing `.var-tok-*` classes.
- `web/src/components/ui/ResizablePanels.tsx` and the Environment Manager resizing shipped
  in `api-client-ux-improvements` (PR #82); this feature does not re-plan them.
- `EnvironmentRepository.SetActiveEnvironmentForCollectionAsync` — already present.
- `src/SwebKit.App/Components/ApiClient/ApiClientToolbar.razor:162-163` — the Blazor scope
  filtering used as the reference for the React port.

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Architecture context: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls consulted: `docs/pitfalls/react-frontend.md` (CodeMirror, Layout, TanStack Query)
