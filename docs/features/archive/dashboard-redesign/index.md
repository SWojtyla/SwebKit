# Dashboard Redesign (recreated stub)

## Status

`Archived` (superseded)

## Why this stub exists

Cited from `docs/architecture/functionalities/dashboard.md` and
`docs/features/active/post-migration-ux-review/*` as the plan behind the dashboard's current
"calm, minimal" layout (single full-width board, typography-first tiles, no KPI ribbon/insight
dock). The original document is missing from this repository state. Recreated as a minimal stub so
those links resolve — the design decision it documents is already reflected in the shipped
`DashboardPage.tsx`, so this is historical record, not open work.

## What this doc covered (inferred from citing docs)

A redesign away from a dense "command-center" dashboard toward a compact header (view
title/saved-view switcher/refresh/customize) plus a single full-width board — already implemented.
`post-migration-ux-review` notes the full tile-builder (add/remove/reorder/resize, saved views,
custom watch tiles) was deliberately *not* rebuilt in favor of a lighter "pin to dashboard"
affordance — see
`docs/features/active/tauri-react-primary-tool/production-readiness-review.md` §4, which confirms
this substitution and treats it as a legitimate design choice, not a gap.

## See also

- `docs/architecture/functionalities/dashboard.md`
- `docs/features/active/tauri-react-primary-tool/production-readiness-review.md`
