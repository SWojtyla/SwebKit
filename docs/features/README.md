# Feature Catalog

This folder is the canonical feature-first map for implementation work.

## Current Structure

- `docs/features/active/` — features currently being implemented or awaiting a final pass.
- `docs/features/archive/` — durable summaries and historical feature records.

Active features:

- `agent-workspace-awareness/` — screen-state snapshots for the agent (pull-via-tool) + multi-step proactive investigation depth.
- `api-client-fixes/` — secret-store variable resolution and bounded faker dates.
- `service-bus-ux-overhaul/` — batch resend with regenerated MessageIds, composer as resizable side panel, templates manager, toolbar/overview declutter.

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
