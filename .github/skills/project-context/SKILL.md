---
name: project-context
description: 'Load project context before starting any non-trivial implementation, planning, or infrastructure task. Reads architecture constraints, pitfall files, and active feature status to prevent context-blind changes. Use when: before starting work, before implementing any non-trivial change, before delegating tasks, load project context, gather project constraints.'
---

# Project Context

Load before starting any non-trivial implementation, planning, or infrastructure task.

## Procedure

1. **Architecture** — Read `docs/architecture/architecture.md` (system-wide map) and `docs/architecture/design.md` (component flows) if they exist. These are **hard constraints**, not background reading.
2. **Codebase navigation** — Read `docs/architecture/codebase-guide.md`. This tells you where to start in the code: entry points, key folders, naming conventions, and cross-cutting concerns. **Always read this before touching any code**, not just when you think you need it.
3. **Pitfalls** — Read relevant files in `docs/pitfalls/`. Forward applicable traps as constraints in your work or delegations.
4. **Active feature** — If the task belongs to a named feature, read `docs/features/active/<feature-name>/index.md` and `status.md`.

## After Work Completes

- Update `docs/features/active/<feature-name>/status.md` if the task belongs to an active feature.
- If you encounter a recurring issue not already documented, add an entry to the relevant `docs/pitfalls/` file.
