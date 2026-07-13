# Test Plan — API Client Page Decomposition

## Validation Strategy

This is a structure-only refactor with an explicit non-goal of changing behavior. Validation is
therefore weighted toward proving "nothing changed" rather than new functional coverage.

- **Build after every slice.** Each concern extraction must produce a clean `dotnet build` before
  moving to the next slice.
- **Full existing test suite after every slice.** Run `SwebKit.App.Tests` and `SwebKit.Core.Tests`
  (`dotnet test`) after each partial-file extraction. Any existing failure not on the known flaky
  list (`/memories/repo/editing-notes.md`) blocks moving to the next slice.
- **No regression in the "mirrored" command tests.** `ApiClientSaveCommandTests.cs` and
  `ApiClientQuickNavCommandTests.cs` must keep passing unchanged — they assert the exact
  `CommandRegistry` registration contract, which must not shift as part of this refactor.
- **New xunit coverage only for genuinely extracted pure logic** (DEC-PD-3). If a slice does not
  produce any pure extraction, no new test file is expected for that slice.
- **Manual smoke check** after the full decomposition (not required per-slice): open API Client,
  create/select/save a request, close a dirty tab, import a curl command, and confirm nothing
  regressed. This mirrors the existing manual verification approach used elsewhere in this repo.

## Regression Risks

- Moving fields into the wrong partial file could shadow/duplicate a field name — caught
  immediately by the compiler (CS0102 duplicate field) if it happens.
- Moving a method without also moving fields it privately closes over breaks compilation
  immediately — same safety net.
- Silent behavior risk is low because this is a pure file-boundary move with no logic rewritten;
  the main watch-item is accidentally changing method visibility/signatures while moving them.

## Acceptance Criteria

- `ApiClientPage.razor`'s own `@code` block shrinks to lifecycle methods, computed properties, and
  concerns not yet assigned to a partial file.
- Each extracted concern lives in its own `ApiClientPage.<Concern>.cs` file, compiles as part of
  the same `ApiClientPage` class, and preserves all existing behavior.
- `dotnet build` and `dotnet test` are clean after the final slice.
- No change to `docs/architecture/functionalities/api-client.md` user-facing behavior description
  is required (structure-only change).
