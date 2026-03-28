# Claude Code Instructions

The full workflow is defined in `.github/copilot-instructions.md`. Read it first — everything below is Claude-specific context only.

## Read first

1. `.github/copilot-instructions.md` — authoritative workflow rules, feature model, skills, and guardrails
2. `ai-setup/ways-of-working/ai-workflow.md` — repository structure and feature execution flow
3. `ai-setup/ways-of-working/definition-of-done.md` — conditions a task must meet before it is done
4. Relevant files in `docs/pitfalls/` — check before making non-trivial changes

## Where things live

| What                 | Where                                   |
| -------------------- | --------------------------------------- |
| Active feature plans | `docs/features/active/<feature-name>/`  |
| Archived features    | `docs/features/archive/<feature-name>/` |
| Architecture         | `docs/architecture/`                    |
| Process rules        | `ai-setup/ways-of-working/`             |
| Pitfalls             | `docs/pitfalls/`                        |
| Feature templates    | `ai-setup/templates/`                   |

**Never write plans, feature docs, or decisions outside the repository.** Everything belongs under `docs/`.

## Delivery paths

- **Jira-driven (autonomous):** `swebify` — ticket key → full feature end-to-end
- **General (manual control):** `swebiplan` → implement via orchestrator → `pre-ship-review` → `azure-devops` → `swebifix` → `feature-archive`

## Status values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`
