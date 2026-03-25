# QOL Improvements — Status

**Status:** In Progress
**Last updated:** 2026-03-25

## Current state

Waves 0–5 implemented (29 items). Cross-cutting improvements across UI Shell, Service Bus, AKS, Redis, Observability.

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
- [x] **UI-12** — Consistent copy feedback (`ShowSuccess("Copied!")` across 6 components)
- [x] **UI-20** — System dark/light preference auto-detect (OS `prefers-color-scheme` on first launch)
- [x] **UI-22** — Color-blind safe status indicators (✓/✗/? symbols in connection dots)
- [x] **UI-23** — Visible focus rings (`:focus-visible` with `--color-accent` outline)
- [x] **UI-24** — Demo banner CSS variable (`#d97706` → `var(--color-warning)`)
- [x] **SB-15** — Detail pane flash fix (already had null guard — verified)
- [x] **OBS-14** — Copy feedback on stack trace copy (`ShowSuccess("Copied!")`)
- [x] **UI-1** — Observability command palette commands (5 commands: refresh, run query, switch to logs/failures/overview)
- [x] **UI-5** — Grid keyboard nav completeness (Redis and Storage already had ↑↓/Enter/Escape — verified)
- [x] **UI-6** — Focus restoration on modal close (`Modal.razor` + `ConfirmDialog.razor` save/restore via JS)
- [x] **UI-15** — Unsaved changes detection + navigation guard (snapshot-based dirty tracking on `SettingsPage`)
- [x] **RDS-5** — Copy key name button (📋 icon in `RedisKeyDetail` header with clipboard + toast)
- [x] **OBS-5** — MaxRowsPerQuery in Settings UI (already present in `SettingsPage` observability section — verified)
- [x] **SB-8** — Production safeguard (`AppConfig.IsProduction` wired to DLQ confirm dialogs + Settings toggle)
- [x] **SB-16** — CSV escaping hardening (added `\r`, `\t`, and Unicode control character quoting)
- [x] **UI-14** — Action progress percentage in status bar (progress/total + progress bar for running tasks)
- [x] **UI-16** — Form validation field highlighting (`.field-invalid` / `.field-error-msg` CSS + `RedisConfigForm` validation)
- [x] **RDS-10** — Connection string masking (password toggle with 👁/🙈 on `RedisConfigForm`)
- [x] **SB-17** — Service Bus connection string masking (password/textarea toggle on `ServiceBusPage` add-namespace form)
- [x] **AKS-2** — Log scroll-to-bottom (Tail toggle + JS `scrollToBottom` helper in `PodLogView`)
- [x] **UI-13** — Persistent notification history (FIFO 50 in `UiState` + history section in `NotificationHistory`)

## Promoted items

- **UI-8, UI-9, UI-10, UI-11** — Referenced as dependencies / cross-cuts by [performance-improvements](../performance-improvements/index.md) (PERF-13, PERF-14, PERF-15)

## Blockers

- All UI-1/UI-2 prerequisites landed — command palette coverage complete for all existing pages
- `pipelines-revamp` active feature takes precedence over REL items for Releases
- Storage bulk download (STG-3) has no blockers; upload/delete are explicitly out of scope
- ReleasesPage does not exist yet — REL items and UI-1 for Releases deferred
