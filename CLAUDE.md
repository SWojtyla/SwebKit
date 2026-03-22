# Claude Code Instructions

## Read first

Before doing any substantial work, read these files:

1. `.github/copilot-instructions.md` — workflow rules, feature execution model, and guardrails
2. `docs/ways-of-working/ai-workflow.md` — repository structure, feature model, and execution flow
3. `docs/ways-of-working/definition-of-done.md` — conditions a task must meet before it is done
4. Relevant files in `docs/pitfalls/` — check before making non-trivial changes

## Where things live

| What                 | Where                                   |
| -------------------- | --------------------------------------- |
| Active feature plans | `docs/features/active/<feature-name>/`  |
| Archived features    | `docs/features/archive/<feature-name>/` |
| Architecture         | `docs/architecture/`                    |
| Process rules        | `docs/ways-of-working/`                 |
| Pitfalls             | `docs/pitfalls/`                        |

**Never write plans, feature docs, or decisions outside the repository.** Everything belongs under `docs/`.

## Standard feature files

Every substantial feature folder contains:

- `index.md` — goal, scope, non-goals, dependencies, risks, quick links
- `status.md` — current state, progress checklist, blockers
- `backend.md` — backend design, contracts, implementation notes, tasks
- `frontend.md` — UI design, components, UX notes, tasks
- `test-plan.md` — scenarios, automated coverage, manual checks, acceptance criteria
- `decisions.md` — key tradeoffs recorded as numbered decision entries

Add only the files the feature actually needs. Do not create empty placeholders.

## Status values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`

## Architecture maintenance

When behavior changes in a supported functionality (Service Bus, AKS, Redis, Storage, Releases, Observability, Settings), update the matching file under `docs/architecture/functionalities/` in the same change set.
