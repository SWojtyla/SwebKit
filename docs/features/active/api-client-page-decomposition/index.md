# API Client Page Decomposition

## Goal

Reduce `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor` from one 1,754-line file with 72
private handler methods spanning ~10 unrelated concerns into a set of smaller, concern-scoped
partial class files, and extract genuinely pure/stateless logic into independently testable
helpers where it can be done without changing behavior.

## Value

- Faster navigation and lower merge-conflict risk when working on any single concern (collections,
  tabs, linked-repo Git conflicts, curl import/export, tree mutations, request lifecycle/autosave,
  secrets, commands/shortcuts).
- Any logic that can be safely pulled out of the page becomes unit-testable with xunit, closing
  part of the gap documented in `ApiClientSaveCommandTests.cs` / `ApiClientQuickNavCommandTests.cs`
  (which currently "mirror" page logic in test files instead of exercising the real code, because
  `ApiClientPage` cannot be instantiated in bUnit due to the MAUI `FilePicker` dependency wall).
- No behavior change for users; this is an internal structure-only rework.

## Scope

- Split `ApiClientPage.razor`'s `@code` block into concern-scoped partial class files
  (`ApiClientPage.<Concern>.cs`) living alongside `ApiClientPage.razor`, using the standard
  C# `partial class` mechanism. Same class, same namespace, same DI/state access — purely
  organizational, zero behavior change per slice.
- Identify and extract any logic within those concerns that is genuinely pure/stateless (no
  `_state` mutation, no dependency on other page methods like `SaveActiveCollectionAsync`) into
  small standalone classes that can carry real xunit coverage.
- Do this incrementally, one concern per slice, verifying a full build (and existing test suite)
  after each slice before moving to the next.

## Non-Goals

- Do not change any user-facing behavior of the API Client.
- Do not attempt to make `ApiClientPage` itself instantiable in bUnit — the MAUI `FilePicker`
  dependency wall is a separate, larger problem and out of scope here.
- Do not revisit or reverse DEC-UX-3 (from the prior, now-archived `api-client-ux-refactor`
  feature): page-level state stays owned by `ApiClientPage`/`ApiClientState`, and presentational
  child `.razor` components (`ApiClientToolbar`, `ApiClientWorkspace`, `ApiClientTreePanel`,
  `ApiClientRequestWorkspace`, etc.) keep holding no method bodies and no page-level truth. Partial
  `.cs` files introduced here are not Blazor components and do not change that rule — they are the
  same class, so they carry no `@if`-destroy/recreate risk (BL-4).
- Do not block or get bundled into the `api-client-advanced-workflows` feature. That feature's new
  UI must land in its own new components (`FlowLibraryPanel.razor`, etc.) regardless of how far
  this decomposition has progressed.

## Dependencies

- `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`
- `src/SwebKit.App/Components/ApiClient/ApiClientState.cs`
- `src/SwebKit.Core/Services/ApiClientWorkflowService.cs` (already-extracted, testable business
  logic that page methods delegate to — the model this decomposition follows)
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md` (BL-2, BL-3, BL-4, BL-5, BL-7)

## Risks & Mitigations

| Risk                                                                          | Mitigation                                                                                                                                                                                                                          |
| ----------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Partial-file split accidentally changes behavior                              | Pure mechanical moves only: cut methods/fields as-is into a new file, same class/namespace, build after each slice.                                                                                                                 |
| Extracting "pure" logic turns out to be more entangled than expected          | Read the full method body before extracting; if it touches `_state` mutation or calls other page methods (e.g. `SaveActiveCollectionAsync`), keep it in the partial file as page-owned glue rather than forcing a risky extraction. |
| Effort balloons into a full rewrite                                           | Strictly one concern per slice, each independently buildable/shippable; stop after each slice and reassess.                                                                                                                         |
| New pattern (`.razor.cs`-style partials) not previously used in this codebase | Document the convention here and in `decisions.md` so future contributors follow it consistently.                                                                                                                                   |

## Related Documents

- Status: `status.md`
- Test plan: `test-plan.md`
- Decisions: `decisions.md`
- Extraction plan: `extraction-plan.md`
- Architecture: `docs/architecture/functionalities/api-client.md`
