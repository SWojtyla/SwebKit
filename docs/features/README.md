# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

- `docs/features/active/` — features currently being implemented or awaiting a final pass.
- `docs/features/archive/` — durable summaries and historical feature records.

Active features:

- `ux-polish/` — UX polish umbrella: AKS context switching, startup warm-up & resume, page restore parity, consistency sweep.
- `settings-profiles-aks-shell/` — settings profile lists + AKS shell/port-forward fixes.
- `workspace-map-overhaul/` — workspace topology map overhaul.

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
