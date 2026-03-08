---
description: Rules for active feature folders
applyTo: docs/features/active/**
---

# Active Feature Instructions

Files under `docs/features/active/` represent live execution context.

## Purpose

These folders are for active work only.

They should help a human or AI quickly understand:

- what is being built
- why it matters
- what remains
- what decisions were made
- how the work should be validated

## Templates

When creating a new active feature folder or adding a missing durable file, use the templates under:
[feature templates](../../docs/features/_templates/)

Use these files as the canonical starting point:

- [index template](../../docs/features/_templates/index.md)
- [status template](../../docs/features/_templates/status.md)
- [test plan template](../../docs/features/_templates/test-plan.md)
- [implementation module template](../../docs/features/_templates/implementation-module.md)
- [decisions template](../../docs/features/_templates/decisions.md)
- [archive summary template](../../docs/features/_templates/archive-summary.md)

Do not copy templates blindly.
Adapt them to the actual feature, and create only the durable files that are needed.

## Core files

For any substantial feature, maintain these core files:

- `index.md`
- `status.md`
- `test-plan.md`

These are the minimum durable documents for active feature work.

## Optional modules

Add only the implementation modules the feature actually needs.

Examples:

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

When a concern needs its own durable file, start from the
[implementation module template](../../docs/features/_templates/implementation-module.md)
and rename the file to match the concern.

Do not create empty placeholder files for concerns that do not exist in the feature.

## Module design

Prefer modules with one clear responsibility.

Good module names describe the concern directly, for example:

- `api-contract.md`
- `domain.md`
- `observability.md`
- `playwright.md`

If a file becomes too broad, split it by concern instead of letting it grow into a catch-all document.

Examples:

- split `backend.md` into `api-contract.md`, `domain.md`, and `persistence.md`
- split `frontend.md` into `ui.md`, `state.md`, and `playwright.md`

## Required behavior

When updating an active feature:

- keep `index.md` concise and decision-oriented
- keep `status.md` current
- keep `test-plan.md` aligned with real validation expectations
- update only the modules relevant to the current work
- record important technical decisions in `decisions.md` when appropriate
- keep implementation docs aligned with the actual code and plan
- prefer editing existing docs over creating scattered new markdown files

## File responsibilities

### `index.md`

Use for:

- goal
- value
- scope
- non-goals
- risks
- dependencies
- related documents
- suggested modules

Use the
[index template](../../docs/features/_templates/index.md)
when creating or refreshing this file.

Do not use it as a scratchpad.

### `status.md`

Use for:

- state
- current focus
- completed work
- remaining work
- blockers
- validation status

Use the
[status template](../../docs/features/_templates/status.md)
when creating or refreshing this file.

This file should stay short and easy to scan.

### `test-plan.md`

Use for:

- validation strategy
- main scenarios
- regression risks
- automated coverage
- manual checks
- acceptance criteria

Use the
[test plan template](../../docs/features/_templates/test-plan.md)
when creating or refreshing this file.

### Implementation modules

Use implementation modules for:

- technical design
- file or area impact
- contracts
- work decomposition
- concern-specific risks
- validation notes for that concern

When creating a new implementation module, start from the
[implementation module template](../../docs/features/_templates/implementation-module.md).

### `decisions.md`

Use for:

- important tradeoffs
- chosen direction
- rejected alternatives worth remembering
- temporary compromises that need future review

Use the
[decisions template](../../docs/features/_templates/decisions.md)
when creating or refreshing this file.

## File hygiene

Prefer a small number of durable, purposeful files.

Do not create throwaway markdown files such as:

- `notes2.md`
- `temp-plan.md`
- `ideas.md`
- `misc.md`

If information matters, merge it into an existing file or create a clearly named durable module.

## Status discipline

Each active feature should reflect real progress.

Do not mark work as done unless:

- implementation is aligned with the intended scope
- validation is complete or clearly described
- documentation matches the current reality
- blockers or follow-up items are visible

## Completion behavior

When a feature is effectively complete:

- reduce temporary execution noise
- preserve reusable decisions and lessons
- ensure the final active state is accurate
- prepare the feature for archive

A completed feature should normally move out of `docs/features/active/` and into `docs/features/archive/` when the task is closed.

When preparing a feature for archive, create or update `summary.md` using the
[archive summary template](../../docs/features/_templates/archive-summary.md).

## Archive boundary

Do not treat active feature folders as permanent storage.

Active folders are for execution.
Archived folders are for historical reference.

Once a feature is closed, prefer moving stable learnings into archive rather than leaving outdated progress tracking in the active area.
