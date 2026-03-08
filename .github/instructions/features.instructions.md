---
description: Rules for active feature folders
applyTo: docs/features/active/**
---

# Active Feature Instructions

Files under `docs/features/active/` represent live execution context.

## Purpose

These folders are for active work only.
They should help a human or AI quickly understand:

- what the feature is,
- what remains,
- what decisions were made,
- how to validate it.

## Required behavior

When updating an active feature:

- keep `index.md` concise and decision-oriented
- keep `status.md` current
- keep technical details in the appropriate file such as `backend.md`, `frontend.md`, `infra.md`, or `test-plan.md`
- add `decisions.md` when implementation choices matter
- remove ambiguity instead of adding loose notes

## File hygiene

Prefer a small number of purposeful files.
Do not create throwaway markdown files like:

- `notes2.md`
- `temp-plan.md`
- `new-thoughts.md`

If new information matters long term, merge it into an existing file or create a clearly named durable file.

## Completion behavior

When the feature is effectively complete:

- reduce temporary execution noise
- preserve decisions and lessons
- prepare the folder to move into archive
