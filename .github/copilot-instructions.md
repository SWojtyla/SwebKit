# GitHub Copilot Instructions

This repository follows a docs-first workflow.

Before non-trivial work:

- Read `docs/architecture/architecture.md`, `docs/architecture/design.md`, and `docs/architecture/codebase-guide.md`.
- Read relevant files in `docs/pitfalls/`.
- If the task belongs to an active feature, treat `docs/features/active/<feature-name>/` as the source of truth and keep `status.md` current.

## Delegation rules for this repo

- Do not delegate a multi-wave feature or a multi-page shell refactor as one oversized subagent task.
- For Blazor/MAUI work that spans shell primitives plus multiple pages, split the work into slices such as:
  - shell context and navigation
  - shared page-header and state primitives
  - per-page adoption
  - tests and docs alignment
- For backend work that spans multiple layers, split the work into contracts, services, integrations, and tests/docs slices.
- If a specialist agent judges a delegated task too broad, it must either complete one coherent slice or return `BLOCKED` with a recommended decomposition. Silent failure is not acceptable.

## Feature execution rules

- Prefer updating the existing active feature docs over creating ad hoc markdown files.
- Keep implementation aligned with the feature plan.
- Update `status.md` when implementation meaningfully progresses.
- If implementation changes behavior for a documented functionality, update the corresponding file under `docs/architecture/functionalities/` in the same change set.

## SwebKit-specific notes

- The shell and route structure in `src/SwebKit.App` are shared foundations. Avoid one-off page behavior when a shared shell primitive is the better fit.
- Production safety cues should be consistent across the shell and destructive workflows.
- Empty, loading, and error states should prefer actionable guidance over passive placeholders.