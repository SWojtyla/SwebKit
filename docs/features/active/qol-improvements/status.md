# QOL Improvements — Status

**Status:** In Progress
**Last updated:** 2026-03-25

## Current state

UI Shell Wave 0–1 items implemented. Area-specific plans ready for implementation.

## Implementation documents

| Area               | Document                                                       | Items                             |
| ------------------ | -------------------------------------------------------------- | --------------------------------- |
| AKS                | [aks.md](aks.md)                                               | AKS-1 – AKS-21                    |
| Observability      | [observability.md](observability.md)                           | OBS-1 – OBS-15                    |
| Redis              | [redis.md](redis.md)                                           | RDS-1 – RDS-12                    |
| Storage            | [storage.md](storage.md)                                       | STG-3 – STG-12 (no upload/delete) |
| Releases           | [releases.md](releases.md)                                     | REL-1 – REL-6                     |
| UI Shell           | [ui-shell.md](ui-shell.md)                                     | UI-1 – UI-24                      |
| Service Bus Revamp | [../service-bus-ui-revamp/](../service-bus-ui-revamp/index.md) | Full layout redesign              |

## How to progress items

1. Pick one or more items from a plan document.
2. If the item is small and isolated → implement directly as a small change.
3. If the item spans multiple files or layers → create a dedicated feature folder under `docs/features/active/` and reference this catalog.
4. Mark items as promoted below once work begins.

## Completed items

- [x] **UI-7** — `ISelectionContext` service (interface, `SelectionContext` implementation, DI singleton)
- [x] **UI-8** — Generic error boundary (`AppErrorBoundary.razor` wrapping `@Body` in `MainLayout`)
- [x] **UI-9** — Skeleton loaders (`SkeletonRows.razor` + CSS shimmer animation)
- [x] **UI-3** — Prefix-boosted fuzzy search (+3 bonus for label-start matches in `CommandPalette`)
- [x] **UI-10** — Retry with exponential backoff on `ErrorCallout` (0→1s→2s→4s, attempt counter)
- [x] **UI-11** — Error message expansion ("Show more" toggle on `NotificationToast` detail)
- [x] **UI-23** — Visible focus rings (`:focus-visible` with `--color-accent` outline)
- [x] **UI-24** — Demo banner CSS variable (`#d97706` → `var(--color-warning)`)

## Promoted items

- **UI-8, UI-9, UI-10, UI-11** — Referenced as dependencies / cross-cuts by [performance-improvements](../performance-improvements/index.md) (PERF-13, PERF-14, PERF-15)

## Blockers

- `ISelectionContext` (UI-7) landed — UI-1 and UI-2 are now unblocked
- `pipelines-revamp` active feature takes precedence over REL items for Releases
- Storage bulk download (STG-3) has no blockers; upload/delete are explicitly out of scope
