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

**Cause:** Feature was moved from `active/` to `archive/` without creating an archive summary from the template at `ai-setup/templates/archive-summary.md`.

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

## AW-7 — Pitfall files not reviewed before touching a known subsystem

**Symptom:** A known bug is hit again, or an existing pitfall becomes stale and no longer reflects reality.

**Cause:** Pitfall files are only consulted reactively (when something breaks), not proactively at the start of a feature that touches a known subsystem.

**Fix:** At the start of any feature or task that touches Blazor/MAUI, Azure SDK, or general .NET code, explicitly re-read the relevant pitfall file before writing any code — even if you believe you remember it. Remove or update entries that no longer apply when you encounter them.

---

## AW-8 — Subagent produces empty output

**Symptom:** A subagent (e.g. `blazor-expert`, `dotnet-expert`) returns no output at all when called by the orchestrator.

**Causes — two patterns observed:**

1. **"Wait for a decision" deadlock.** The design health check instructs the agent to "wait for a decision" if it finds a design concern. When running as a subagent, there is no input channel to wait on. The agent parks waiting for a reply that never comes and produces no output.

2. **Context window exhaustion from redundant loading.** Both the orchestrator and the expert agents were instructed to load `project-context` (architecture.md, design.md, codebase-guide.md, pitfall files). When the agent is invoked with a large delegation payload _and_ tries to re-read the same files, the context window fills before any output is generated.

3. **Oversized one-shot delegation.** A specialist agent receives a task that spans shell primitives, multiple routed pages, tests, docs, and cross-cutting polish in one prompt. Even with good context hygiene, the task is too broad for a one-shot subagent run and can terminate without returning structured output.

**Fix:**

- Expert agents under the orchestrator now skip `project-context` re-loading and use the context already provided in the delegation payload.
- The design health check now has two branches: **standalone** → wait; **under orchestrator** → include a `Design concern:` note in the response and proceed.
- The orchestrator delegation payload explicitly requires architecture constraints and pitfalls to be inlined so subagents don't need to reload them.
- The orchestrator now decomposes large implementation work into smaller slices instead of delegating a whole multi-wave feature in one shot.
- The expert agents now explicitly require a non-empty fallback for oversized tasks: complete one coherent slice or return `BLOCKED` with a recommended decomposition.

See `blazor-expert.agent.md` and `dotnet-expert.agent.md` → "Before starting work".

---

## AW-9 — Repo without workspace instructions drifts back to generic delegation behavior

**Symptom:** Shared global agent rules exist, but repo-specific execution still regresses toward oversized delegations or feature-blind implementation because the repository itself does not provide a local workspace instruction file.

**Cause:** The repo relies only on global or toolkit-level agent configuration. Without a local `.github/copilot-instructions.md`, the agent has less project-specific guidance about how to split work in this codebase.

**Fix:** Add a concise repo-level workspace instruction file that describes the local docs-first workflow, feature-folder expectations, and any repository-specific delegation constraints such as slicing large shell/UI work.

---

_See also: [blazor-maui.md](blazor-maui.md) · [azure-sdk.md](azure-sdk.md) · [dotnet-csharp.md](dotnet-csharp.md)_
