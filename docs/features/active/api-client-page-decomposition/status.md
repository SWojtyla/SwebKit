# Status — API Client Page Decomposition

## Current State

`In Progress`

## Quick Summary

Follow-up cleanup identified while scoping `api-client-advanced-workflows`: `ApiClientPage.razor`
is a 1,754-line, 72-method, 18-dependency file mixing ~10 unrelated concerns, and cannot be
instantiated in bUnit at all. Decomposing it into concern-scoped partial class files
(`ApiClientPage.<Concern>.cs`) before adding more UI wiring for the flow feature on top of it.

**Jira:** not linked

**Current focus:** Slice 1 done and verified. Next up: Slice 2 — secrets.

## Progress Checklist

- [x] Planning docs created (`index.md`, `decisions.md`, `test-plan.md`, `extraction-plan.md`)
- [x] Slice 1 — Curl import/export (`ApiClientPage.Curl.cs`)
- [ ] Slice 2 — Secrets (`ApiClientPage.Secrets.cs`)
- [ ] Slice 3 — Tab lifecycle (`ApiClientPage.Tabs.cs`)
- [ ] Slice 4 — Collection tree mutations (`ApiClientPage.Tree.cs`)
- [ ] Slice 5 — Collections/environments/linked roots (`ApiClientPage.Collections.cs`)
- [ ] Slice 6 — Linked-repo Git save conflicts (`ApiClientPage.LinkedSave.cs`)
- [ ] Slice 7 — Request lifecycle/autosave/results (`ApiClientPage.Requests.cs`)
- [ ] Slice 8 — Shortcuts and commands (`ApiClientPage.Commands.cs`)

## Completed

- Planning docs created.
- Slice 1 (curl import/export) extracted into `ApiClientPage.Curl.cs`: `dotnet build` clean (0
  warnings/errors), full `ApiClient`-scoped test filter in `SwebKit.App.Tests` passes (9/9), and
  Aikido SAST/secrets scan on the new file returned zero issues.

## Remaining

- Execute slices 2–8 in order, verifying a build (and full test run) after each slice.
- Re-check `extraction-plan.md`'s method/field lists against the actual file before each slice —
  the file may shift slightly between slices.

## Blockers

_None._

## Validation

- Test Plan: `test-plan.md`
- Validation status: Slice 1 validated (build clean, `SwebKit.App.Tests` ApiClient filter 9/9
  passing, Aikido scan clean). Slices 2–8 not started.

## Notes

Not a prerequisite for `api-client-advanced-workflows`'s backend/domain work (already
well-factored in `SwebKit.Core`). It reduces risk for that feature's frontend wiring, which should
still land in its own new components regardless of how many slices here are complete.
