# AI Workflow

This repository uses a docs-first workflow for both human and AI contributors.

The purpose of this workflow is to keep implementation, validation, decisions, and lessons aligned while staying lightweight and adaptable to different project types.

## Principles

- Stable rules belong in shared docs.
- Active work belongs in the active feature folder.
- Historical work belongs in archive.
- Repeated mistakes belong in pitfalls.
- Important choices belong in decision notes.
- Only create files that the feature actually needs.
- Prefer a few clear files over many shallow ones.

## Repository structure

Use this structure as the default model:

- `docs/architecture/` for stable architecture and design context
- `docs/ways-of-working/` for process and quality rules
- `docs/pitfalls/` for recurring AI or implementation mistakes
- `docs/features/active/<feature-name>/` for active feature work
- `docs/features/archive/<year>/<feature-name>/` for completed feature history

## Feature model

For substantial features, always create the core files:

- `index.md`
- `status.md`
- `test-plan.md`

Then add only the implementation modules the feature needs, for example:

- `backend.md`
- `frontend.md`
- `infra.md`
- `api-contract.md`
- `domain.md`
- `persistence.md`
- `messaging.md`
- `security.md`
- `migration.md`
- `playwright.md`
- `decisions.md`

Do not create empty placeholder files for concerns that do not exist in the feature.

## When to split a module

If one implementation file becomes too broad, split it by concern.

Good reasons to split:

- multiple subsystems are affected
- different specialists could work in parallel
- one file is becoming long and hard to scan
- validation differs by concern
- the file mixes unrelated responsibilities

Examples:

- split `backend.md` into `api-contract.md`, `domain.md`, and `persistence.md`
- split `frontend.md` into `ui.md`, `state.md`, and `playwright.md`
- split operational work into `infra.md`, `deployment.md`, and `observability.md`

## Working modes

### Small changes

A small change is:

- isolated,
- low-risk,
- easy to validate,
- does not need a multi-step technical plan.

For small changes:

- use existing docs if relevant
- do not create a feature folder unless needed
- still respect architecture, pitfalls, and definition of done

### Feature work

A feature requires a dedicated folder when it:

- spans multiple files or layers
- requires technical planning
- has non-trivial validation needs
- may be implemented incrementally
- benefits from explicit progress tracking

## Standard execution flow

When working on a feature, follow this sequence:

1. Understand the request.
2. Read the stable context:
   - `docs/ways-of-working/definition-of-done.md`
   - `docs/architecture/architecture.md`
   - `docs/architecture/design.md`
   - relevant files in `docs/pitfalls/`
3. Create or update the feature folder under `docs/features/active/<feature-name>/`.
4. Clarify scope in `index.md`.
5. Track progress in `status.md`.
6. Add only the implementation modules needed for the feature.
7. Record meaningful tradeoffs in `decisions.md` when appropriate.
8. Update `test-plan.md` as validation expectations evolve.
9. Implement in code.
10. Validate.
11. Update docs to reflect reality.
12. Archive the feature when it is truly complete.

## File responsibilities

### `index.md`

Use for:

- feature goal
- value
- scope
- non-goals
- dependencies
- risks
- links to related docs

Do not use it as a scratchpad.

### `status.md`

Use for:

- current state
- current focus
- completed work
- remaining work
- blockers
- validation state

This file should be short and fast to read.

### Implementation modules

Use implementation modules for:

- technical design
- affected files or areas
- contracts
- decomposition of work
- concern-specific checklists
- validation notes tied to that concern

Each module should have one clear responsibility.

### `test-plan.md`

Use for:

- validation strategy
- main scenarios
- regression risks
- manual and automated checks
- acceptance criteria

### `decisions.md`

Use for:

- important tradeoffs
- chosen direction
- rejected alternatives worth remembering
- temporary compromises that need revisit

## Progress rules

Status should be explicit.
Use one of these values:

- Proposed
- Planned
- In Progress
- Review
- Done
- Archived

Do not mark a feature as `Done` if:

- implementation is partial
- tests are missing or knowingly broken
- docs do not match the code
- unresolved blockers remain hidden

## Pitfalls rules

When a repeated mistake appears:

- add a concise note to the relevant pitfalls file
- describe the failure pattern
- describe how to avoid it
- keep it short and actionable

## Decision rules

A decision should be recorded when:

- multiple reasonable options exist
- the chosen path affects future work
- architecture is being refined
- a workaround is introduced
- a limitation is accepted intentionally

Minor obvious choices do not need a decision entry.

## Archive rules

When a feature is complete:

- remove active-only execution noise if no longer useful
- keep reusable decisions and lessons
- create an archive summary
- move the feature under `docs/features/archive/<year>/<feature-name>/`
- stop treating it as
