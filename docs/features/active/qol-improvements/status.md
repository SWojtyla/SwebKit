# QOL Improvements — Status

**Status:** Planned
**Last updated:** 2026-03-22

## Current state

Full detailed technical plans written for all areas. Ready for implementation.

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

## Promoted items

- **UI-8, UI-9, UI-10, UI-11** — Referenced as dependencies / cross-cuts by [performance-improvements](../performance-improvements/index.md) (PERF-13, PERF-14, PERF-15)

## Blockers

- `ISelectionContext` (UI-7) must land before area-specific command palette commands (UI-1, UI-2)
- `pipelines-revamp` active feature takes precedence over REL items for Releases
- Storage bulk download (STG-3) has no blockers; upload/delete are explicitly out of scope
