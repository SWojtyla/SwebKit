# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

- `docs/features/active/` — features currently being implemented or awaiting a final pass.
- `docs/features/archive/` — durable summaries and historical feature records.

There are currently no active feature plans. Completed implementation details belong in architecture and pitfall documentation; historical planning records remain under `archive/` where retained.

## Folder Contract

An active feature folder contains:

- `index.md` — scope, outcomes, dependencies, and source traceability
- `technical-plan.md` — detailed technical plan with implementation tasks
- `test-plan.md` — feature-level test scope and scenarios
- `status.md` — lifecycle state and validation results

## Status Values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`.

## Traceability Contract

- Links must resolve inside `docs/features/` or current supporting architecture documentation.
- Feature documents must not depend on retired phase-era or global plan-era files.
- New implementation updates are recorded in the relevant feature folder first.
