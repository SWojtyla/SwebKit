# Pitfalls — Agent Workflow

---

## AW-1 — Agent forgot to update `status.md` after implementation

**Symptom:** Feature plan shows tasks as "not started" even though code is already merged.

**Cause:** Implementing agents completed their work but did not update `docs/features/active/<feature-name>/status.md`. Progress tracking diverges from reality.

**Fix:** Every implementing agent has a "Before starting work" section that includes updating `status.md` after completing work. If you notice stale status, update it immediately.

---

## AW-2 — Plan created without reading architecture docs

**Symptom:** Implementation conflicts with existing patterns (wrong abstraction layer, duplicate service, incorrect data flow).

**Cause:** `plan-expert` created a plan without reading `docs/architecture/architecture.md` and `docs/architecture/design.md` first. The plan diverges from established constraints.

**Fix:** Always read architecture docs before planning. They are constraints, not background reading.

---

## AW-3 — Pitfall not written after debugging session

**Symptom:** Same bug is hit again in a later task. No pitfall entry exists to prevent it.

**Cause:** The agent fixed the bug but did not add an entry to the relevant `docs/pitfalls/` file. Knowledge was lost between sessions.

**Fix:** After resolving any bug that cost more than one debugging cycle, add a concise pitfall entry: symptom, cause, fix.

---

## AW-4 — Feature archived without summary

**Symptom:** Archived feature folder has no `summary.md`. Future readers cannot understand what was built or learned without reading every file.

**Cause:** Feature was moved from `active/` to `archive/` without creating an archive summary from the template at `docs/features/_templates/archive-summary.md`.

**Fix:** Always create `summary.md` before archiving. A new reader should understand the feature in under 2 minutes.

---

## AW-5 — Architecture drift without decision record

**Symptom:** Code no longer matches `docs/architecture/` docs. No `decisions.md` entry explains why.

**Cause:** Implementation diverged from documented architecture for a valid reason, but the agent did not record the decision or update the architecture docs.

**Fix:** If implementation must diverge from architecture, create a decision entry in the feature's `decisions.md` and update the relevant architecture file in the same change set.

---

## AW-6 — Active feature folder not fully deleted after archiving

**Symptom:** `docs/features/active/<feature-name>/` still exists with one or more files after archiving. The feature appears active in directory listings even though it is archived.

**Cause:** The archive procedure moves `summary.md` and then calls `Remove-Item` on the folder in the same chained command. If a file was edited between the move and the delete (e.g. `status.md` was updated to Done just before the move), it can be left behind. Using two separate commands (`Move-Item` then `Remove-Item`) is fragile if the shell reports success on the first even when the item was not fully flushed.

**Fix:** After moving `summary.md`, verify the folder is empty before deleting it — or use a single `Remove-Item -Recurse -Force` on the folder _first_, accepting that `summary.md` was already moved out. Always confirm with `Test-Path` or `Get-ChildItem` that the active folder is gone before declaring the archive complete.

---

_See also: [blazor-maui.md](blazor-maui.md) · [azure-sdk.md](azure-sdk.md) · [dotnet-csharp.md](dotnet-csharp.md)_
