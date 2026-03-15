# GitHub Copilot Instructions

You are working in a repository that follows a docs-first AI workflow.
Your goal is not only to write code, but to preserve clean project state, keep documentation aligned with implementation, and make progress traceable.

## Core working model

This repository separates:

- stable project guidance,
- active feature execution,
- historical archived work,
- recurring pitfalls and learned patterns.

Always prefer this workflow over inventing a new one for the current task.

## Authoritative documents

Before making significant changes, consult these sources in this order when they exist:

1. `docs/ways-of-working/ai-workflow.md`
2. `docs/ways-of-working/definition-of-done.md`
3. `docs/architecture/architecture.md`
4. `docs/architecture/design.md`
5. `docs/pitfalls/`

If the task is about a specific feature, then also read:

- `docs/features/active/<feature-name>/index.md`
- `docs/features/active/<feature-name>/status.md`
- other files in that feature folder only if relevant

Do not scan unrelated feature folders unless explicitly asked.

## Feature execution rules

When working on a feature:

- treat the feature folder as the source of truth for scope and progress
- keep implementation aligned with the documented plan
- update `status.md` as work progresses
- record important technical decisions in `decisions.md`
- update test notes or test plan when behavior changes
- prefer editing existing feature docs over creating scattered new markdown files

If a feature folder does not exist and the task is substantial, propose or create one before large implementation work begins.

## Status discipline

Each active feature should maintain a small `status.md` file.
Use it to track:

- current state
- current focus
- completed work
- remaining work
- blockers
- validation status

Do not mark a feature as done unless implementation, tests, and related documentation are aligned.

## Pitfalls discipline

Before making non-trivial changes, check relevant files in `docs/pitfalls/`.
If you notice a repeated failure mode, risky assumption, or recurring code-generation mistake, add or update a concise pitfalls entry.

Pitfalls should be:

- short
- actionable
- specific
- based on real mistakes or repeated review findings

## Architecture discipline

Treat architecture and design documents as constraints, not background reading.
If implementation needs to diverge from documented architecture or design:

- do not silently drift
- update the relevant decision record or feature decision note
- explain the reason for the change

When implementation changes behavior for an app functionality (Projects, Service Bus,
Observability, AKS, Redis, Settings), also update the corresponding file under
`docs/architecture/functionalities/` in the same change set.

## Archive discipline

Active work belongs under `docs/features/active/`.
Completed work should not remain mixed with active work forever.

When a feature is complete:

- prepare a concise archive-ready summary
- preserve reusable decisions and lessons
- avoid keeping large execution checklists in the active area
- move completed feature material to archive when the task is closed

Do not read archived feature folders by default.
Use archived features only when explicitly asked for history, precedent, or reusable implementation patterns.

## Change style

When making changes:

- prefer small, coherent edits
- avoid unnecessary file proliferation
- keep naming predictable
- preserve existing conventions unless there is a clear reason to improve them
- explain tradeoffs briefly when making non-obvious structural decisions

## Validation expectations

Before considering work complete:

- verify the implementation against the feature plan
- verify tests or test coverage expectations
- verify related docs are updated
- note any assumptions, gaps, or follow-up items clearly

## Communication style

When responding:

- be explicit about what was changed
- mention which feature docs were updated
- mention blockers or uncertainties
- do not claim completion if validation is incomplete
- suggest the next smallest useful step when work cannot be fully completed

## Guardrails

Do not:

- treat archived docs as active requirements
- create duplicate planning files for the same feature without reason
- invent requirements not grounded in the task or docs
- silently ignore architecture, test expectations, or known pitfalls
- leave the repo in a partially updated state without saying so
- write plans, feature docs, or decisions outside the repository — everything belongs under `docs/features/active/<feature-name>/`
