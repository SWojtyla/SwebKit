# AI Workflow

This repository uses a docs-first workflow for both human and AI contributors.

The goal is to keep implementation, plans, decisions, testing, and lessons aligned without creating unnecessary documentation noise.

## Principles

- Stable guidance belongs in shared docs.
- Active work belongs in the active feature folder.
- Historical work belongs in archive.
- Repeated mistakes belong in pitfalls.
- Important technical choices belong in decision notes.
- Completion means code, tests, and docs are aligned.

## Repository structure

Use this structure as the default model:

- `docs/architecture/` for stable architecture and design context
- `docs/ways-of-working/` for process rules and quality expectations
- `docs/pitfalls/` for recurring AI or implementation mistakes
- `docs/features/active/<feature-name>/` for active feature work
- `docs/features/archive/<year>/<feature-name>/` for completed feature history

## Working modes

### Small changes

A small change is:

- isolated,
- low-risk,
- easy to validate,
- does not need a multi-step technical plan.

For small changes:

- use the existing docs if relevant
- do not create a new feature folder unless needed
- still respect architecture, pitfalls, and definition of done

Examples:

- fix a small bug
- rename a field
- adjust a simple UI label
- add a focused test

### Feature work

A feature requires a dedicated folder when it:

- spans multiple files or layers
- requires design choices
- has non-trivial validation needs
- benefits from explicit progress tracking
- may be implemented incrementally

For feature work, create:

- `index.md`
- `status.md`
- `backend.md` if backend work exists
- `frontend.md` if frontend work exists
- `test-plan.md`
- `decisions.md` if meaningful technical decisions arise

Add other files only when clearly justified.

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
5. Track current progress in `status.md`.
6. Document implementation work in `backend.md`, `frontend.md`, or equivalent files.
7. Record meaningful choices in `decisions.md`.
8. Update `test-plan.md` as behavior or validation expectations evolve.
9. Implement in code.
10. Validate.
11. Update docs to reflect reality.
12. Move the feature to archive when it is truly complete.

## File responsibilities

### `index.md`

Use for:

- feature goal
- user or business value
- scope
- non-goals
- dependencies
- risks
- links to related docs

Do not use it as a scratchpad.

### `status.md`

Use for:

- current state
- completed work
- remaining work
- blockers
- validation state
- current focus

This file should be fast to read.

### `backend.md` / `frontend.md`

Use for:

- technical design
- file-level implementation plan
- API or UI contracts
- task checklists
- important validation notes

These files should be execution-oriented.

### `test-plan.md`

Use for:

- test strategy
- scenarios
- acceptance checks
- regression risks
- manual and automated validation notes

### `decisions.md`

Use for:

- important tradeoffs
- accepted implementation direction
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

Good pitfalls are reusable.
Bad pitfalls are long postmortems with no operational guidance.

## Decision rules

A decision should be recorded when:

- multiple reasonable options exist
- the chosen path affects future work
- the architecture is being refined
- a workaround is introduced
- a limitation is accepted intentionally

Minor obvious choices do not need a decision entry.

## Archive rules

When a feature is complete:

- remove active-only execution noise if it is no longer useful
- keep reusable decisions and lessons
- create an archive summary
- move the feature under `docs/features/archive/<year>/<feature-name>/`
- stop treating it as live context

Archived work is reference material, not active instruction.

## AI-specific expectations

AI contributors should:

- prefer updating existing docs over creating new scattered notes
- avoid silent divergence between docs and code
- mention uncertainty instead of inventing confidence
- preserve traceability for meaningful work
- keep the active area clean and current
- avoid reading unrelated archived material by default

## Human review expectations

Human reviewers should be able to answer these quickly:

- What is being built?
- What changed?
- What remains?
- What decisions were made?
- How was it validated?
- Is this ready to archive?
