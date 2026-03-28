---
description: Rules for archived feature folders
applyTo: docs/features/archive/**
---

# Archive Instructions

Files under `docs/features/archive/` are historical reference, not active implementation context.

## Default behavior

Do not use archived files as active requirements unless the user explicitly asks for:

- prior art,
- implementation precedent,
- historical decisions,
- reusable patterns.

## Primary archive artifact

Each archived feature folder should normally contain a `summary.md` file.

Treat `summary.md` as the primary entry point for understanding an archived feature.
When creating or updating archived feature documentation, use the archive summary template:
[archive summary template](ai-setup/templates/archive-summary.md)

Prefer reading `summary.md` before opening other archived files.

## Usage rules

When consulting archive:

- extract only the relevant lesson or precedent
- do not revive outdated assumptions blindly
- prefer current architecture and current active feature docs over old implementation details
- use `summary.md` first, then read deeper only if needed

## Archive quality

Archived feature folders should favor:

- concise summaries,
- stable decisions,
- reusable lessons,
- links to PRs, commits, or related docs

Archived folders should not remain bloated with transient execution notes unless those notes still have clear future value.

## Typical archived contents

A typical archived feature folder should favor a small set of durable files, such as:

- `summary.md`
- `decisions.md`
- `outcome.md` or another concise implementation/result note when needed

Do not preserve large active-work checklists unless they still offer future reuse value.
