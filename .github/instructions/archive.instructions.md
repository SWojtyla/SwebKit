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

## Usage rules

When consulting archive:

- extract only the relevant lesson or precedent
- do not revive outdated assumptions blindly
- prefer current architecture and current active feature docs over old implementation details

## Archive quality

Archived feature folders should favor:

- concise summaries,
- stable decisions,
- reusable lessons,
- links to PRs, commits, or related docs

Archived folders should not remain bloated with transient execution notes unless those notes still have clear future value.
