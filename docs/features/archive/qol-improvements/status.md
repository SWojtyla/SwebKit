# QOL Improvements — Status

**Status:** Done
**Last updated:** 2025-07-28

## Current state

All implementable items complete. Build passes, 74/74 tests pass. Cross-cutting improvements across UI Shell, AKS, Redis, Observability, Storage. REL items deferred (no ReleasesPage), SB items superseded by service-bus-ui-revamp, AKS-10 skipped (low priority).

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
- [x] **RDS-1** — Key scan pagination (cursor-based, "Load more" button, ScanPageSize = 1000)
- [x] **RDS-2** — Binary content detection (`IsBinaryContent` badge in `RedisKeyDetail`)
- [x] **RDS-3** — Sorted set score editing (inline click-to-edit, `UpdateSortedSetScoreAsync`)
- [x] **RDS-4** — List/set/zset pagination (`LoadMoreItemsAsync`, `GetSetMembersPageAsync`, ItemPageSize = 100)
- [x] **RDS-6** — Key rename (inline input, Enter/Escape, `RenameKeyAsync`)
- [x] **RDS-7** — Preserve TTL countdown across separator change (verified + invariant comment)
- [x] **RDS-8** — TTL set dialog pre-populate with current TTL (auto-set in `OnParametersSet`)
- [x] **RDS-9** — Multi-key delete (Select toggle, tree checkboxes, batch `DeleteKeysAsync`)
- [x] **RDS-11** — Hash field add/delete (− button per row, + add row, `DeleteHashFieldAsync`)
- [x] **RDS-12** — Export keys to JSON (full key dump to Downloads folder)
- [x] **SB-17** — Service Bus connection string masking (password/textarea toggle on `ServiceBusPage` add-namespace form)
- [x] **AKS-2** — Log scroll-to-bottom (Tail toggle + JS `scrollToBottom` helper in `PodLogView`)
- [x] **UI-13** — Persistent notification history (FIFO 50 in `UiState` + history section in `NotificationHistory`)
- [x] **STG-5** — Copy blob relative path (context menu item copies `item.Name` to clipboard)
- [x] **STG-10** — Search/filter blobs (client-side filter input with match count in `StorageBlobList`)
- [x] **AKS-14** — Copy localhost URL button (clipboard copy in `PortForwardSessionsPanel` session rows)
- [x] **OBS-4** — Export query results to file (Download CSV + Download JSON buttons in `ObservabilityLogs`)
- [x] **AKS-1** — Log search & highlight (regex search bar in `PodLogView`)
- [x] **AKS-3** — Pod log filter presets (Error/Warning/Info level filter buttons)
- [x] **AKS-4** — Multi-pod log merge in `MultiPodLogView`
- [x] **AKS-5** — Node filter dropdown for `PodGrid`
- [x] **AKS-6** — HPA threshold visualization in `HpaPanel`
- [x] **AKS-7** — Pod restart sparkline inline in `PodGrid`
- [x] **AKS-8** — YAML validation before apply (YamlDotNet parse + error display)
- [x] **AKS-9** — Quick edit YAML with context menu replace
- [x] **AKS-11** — Secret base64 auto-decode in `SecretDetailPanel`
- [x] **AKS-12** — Configurable CPU/Memory bar ceilings
- [x] **AKS-13** — Port-forward start dialog (namespace pre-fill, port validation)
- [x] **AKS-15** — Configurable log buffer size
- [x] **AKS-16** — Auto-refresh toggle for pod view
- [x] **AKS-17** — Better error summary for failed pods
- [x] **AKS-18** — Copy name in context menus (all 8 grid context menus)
- [x] **AKS-19** — Namespace search filter
- [x] **AKS-20** — Pod count badges per namespace
- [x] **AKS-21** — Collapsed detail pane memory
- [x] **OBS-1** — Latency trend mini-chart
- [x] **OBS-2** — Exception group drill-through
- [x] **OBS-3** — Click-to-filter from any table cell
- [x] **OBS-6** — Saved custom queries
- [x] **OBS-7** — KQL syntax shortcuts (Ctrl+Enter run, auto-complete brackets)
- [x] **OBS-8** — Timezone normalization (browser offset passed to `FormatTimestamp`)
- [x] **OBS-9** — Configurable performance thresholds
- [x] **OBS-10** — One-click drill from exception to trace
- [x] **OBS-11** — Availability heatmap
- [x] **OBS-12** — Resource picker dialog
- [x] **OBS-13** — Auto-detect workspace vs component AI
- [x] **OBS-15** — Multi-resource tab support
- [x] **STG-3** — Bulk download blobs (ZIP download)
- [x] **STG-4** — Container-level SAS URL generation
- [x] **STG-6** — Blob version history listing
- [x] **STG-7** — Blob property detail pane
- [x] **STG-8** — Container search filter
- [x] **STG-9** — Lazy-load blob list per container
- [x] **STG-11** — Blob size display
- [x] **STG-12** — Last modified display
- [x] **UI-2** — Keyboard shortcut help panel (Ctrl+/ overlay)
- [x] **UI-4** — "Go to resource" in command palette ("go " prefix)
- [x] **UI-17** — Resizable left nav panel (drag handle)
- [x] **UI-18** — Collapsible sidebar sections
- [x] **UI-19** — Connection string masking (`PasswordField` component for config forms)
- [x] **UI-21** — Config export/import (JSON export/import button on `SettingsPage`)

## Promoted items

- **UI-8, UI-9, UI-10, UI-11** — Referenced as dependencies / cross-cuts by [performance-improvements](../performance-improvements/index.md) (PERF-13, PERF-14, PERF-15)

## Blockers

- None — all implementable items complete.
- REL-1 through REL-6 remain deferred (ReleasesPage does not exist yet).
- SB items superseded by the `service-bus-ui-revamp` active feature.
- AKS-10 (diff view before apply) skipped — low priority.

## Deferred / Out of scope

| Item | Reason |
| ---- | ------ |
| REL-1 – REL-6 | Deferred — `ReleasesPage` does not exist yet |
| SB items | Superseded by `service-bus-ui-revamp` active feature |
| AKS-10 | Skipped — low priority diff view |
| STG-1 (upload) | Explicitly out of scope |
| STG-2 (delete) | Explicitly out of scope |
