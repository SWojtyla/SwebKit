# Status — API Client Page Decomposition

## Current State

`Done`

## Quick Summary

Follow-up cleanup identified while scoping `api-client-advanced-workflows`: `ApiClientPage.razor`
was a 1,754-line, 72-method, 18-dependency file mixing ~10 unrelated concerns, and could not be
instantiated in bUnit at all. Decomposed it into 8 concern-scoped partial class files
(`ApiClientPage.<Concern>.cs`) before adding more UI wiring for the flow feature on top of it.

**Jira:** not linked

**Current focus:** All 8 slices complete. Feature ready to archive.

## Progress Checklist

- [x] Planning docs created (`index.md`, `decisions.md`, `test-plan.md`, `extraction-plan.md`)
- [x] Slice 1 — Curl import/export (`ApiClientPage.Curl.cs`)
- [x] Slice 2 — Secrets (`ApiClientPage.Secrets.cs`)
- [x] Slice 3 — Tab lifecycle (`ApiClientPage.Tabs.cs`)
- [x] Slice 4 — Collection tree mutations (`ApiClientPage.Tree.cs`)
- [x] Slice 5 — Collections/environments/linked roots (`ApiClientPage.Collections.cs`)
- [x] Slice 6 — Linked-repo Git save conflicts (`ApiClientPage.LinkedSave.cs`)
- [x] Slice 7 — Request lifecycle/autosave/results (`ApiClientPage.Requests.cs`)
- [x] Slice 8 — Shortcuts and commands (`ApiClientPage.Commands.cs`)

## Completed

- Planning docs created.
- Slice 1 (curl import/export) extracted into `ApiClientPage.Curl.cs`: `dotnet build` clean (0
  warnings/errors), full `ApiClient`-scoped test filter in `SwebKit.App.Tests` passes (9/9), and
  Aikido SAST/secrets scan on the new file returned zero issues.
- Slice 2 (secrets) extracted into `ApiClientPage.Secrets.cs`: fields
  (`_showConfigureSecretDialog`, `_secretNameToConfigure`, `_secretValueToConfigure`,
  `_secretConfigError`), the `MissingSecretNames` computed property, and methods
  (`OpenConfigureSecretDialog`, `SaveConfiguredSecretAsync`, `GetMissingSecretNames`,
  `IsSecretConfigured`, `ExtractSecretNames`) moved as a pure mechanical file-boundary move (no
  behavior change, per DEC-PD-1). `dotnet build` clean (0 warnings/errors introduced), full
  `ApiClient`-scoped test filter in `SwebKit.App.Tests` passes (9/9).
- Slices 3–8 (tab lifecycle, tree mutations, collections/environments/linked roots, linked-repo
  Git save conflicts, request lifecycle/autosave/results, shortcuts and commands) extracted into
  `ApiClientPage.Tabs.cs`, `ApiClientPage.Tree.cs`, `ApiClientPage.Collections.cs`,
  `ApiClientPage.LinkedSave.cs`, `ApiClientPage.Requests.cs`, and `ApiClientPage.Commands.cs`
  respectively — same pure mechanical file-boundary moves, one concern per file, per DEC-PD-1.
  `dotnet build` clean after each slice (0 warnings/errors). Full `SwebKit.App.Tests` suite run
  after the final slice: 499 total, 490 passing, 9 failing — all 9 failures are in files
  untouched by this feature (`ShellFoundationTests`/`TopBar` UserSettings DI registration,
  `AlertMonitorServiceTests`, `MessageListViewTests` file-replace IO, `ComponentTests`
  `ServiceBusConfigForm`/`TopBar`, `AksPageBatchTests` button-count assertion) and are pre-existing,
  unrelated to `ApiClientPage`. The `ApiClient`-scoped filter alone remained 9/9 passing throughout.
- `ApiClientPage.razor`'s `@code` block now holds only lifecycle methods, computed properties that
  span multiple concerns, shared cross-concern helpers (tree helpers, `Find*` lookups,
  `BuildCombined*`), and comment pointers to each concern's new home file, per the extraction
  plan's "what stays" list.

## Remaining

_None._ All 8 slices complete. Feature ready for `feature-archive`.

## Blockers

_None._ Aikido MCP scan tool was not available in this session for slices 2–8's new files — run
`aikido_full_scan` on `ApiClientPage.Secrets.cs`, `ApiClientPage.Tabs.cs`, `ApiClientPage.Tree.cs`,
`ApiClientPage.Collections.cs`, `ApiClientPage.LinkedSave.cs`, `ApiClientPage.Requests.cs`, and
`ApiClientPage.Commands.cs` once the Aikido MCP server is available (see
`.github/instructions/aikido_rules.instructions.md`).

## Validation

- Test Plan: `test-plan.md`
- Validation status: All 8 slices validated (build clean after each slice; `SwebKit.App.Tests`
  ApiClient filter 9/9 passing after every slice; full suite 490/499 passing with 9 pre-existing,
  unrelated failures after the final slice). Aikido scan only completed for slice 1 — tool
  unavailable for slices 2–8 this session.

## Notes

Not a prerequisite for `api-client-advanced-workflows`'s backend/domain work (already
well-factored in `SwebKit.Core`). It reduces risk for that feature's frontend wiring, which should
still land in its own new components regardless of how many slices here are complete.
