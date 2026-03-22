# QOL Improvements Catalog

**Status:** In Progress
**Type:** Backlog / Planning reference
**Scope:** Quality-of-life improvements across AKS, Observability, Redis, Storage, Releases, and the global UI shell, derived from a full codebase audit (March 2026).
**Out of scope here:** Service Bus feature-level QOL (superseded by the [Service Bus UI Revamp](../service-bus-ui-revamp/index.md)), Storage blob upload/delete (separate feature).

---

## Goal

Systematically improve the polish, reliability, and usability of SwebKit's six feature areas (Service Bus, AKS, Observability, Redis, Storage, Releases/Pipelines) plus the global UI shell. Each improvement is grounded in observed gaps, rough edges, or missing interactions found during exploration of the current implementation.

## Non-goals

- New top-level features (covered in dedicated feature folders)
- Performance tuning requiring infra changes
- Multi-user / cloud sync / remote configuration

## How to use this document

This is a catalog, not a sprint plan. Each section is independent. Pick items by priority (🔴 High / 🟡 Medium / 🟢 Low) and promote them to their own small-change task or feature folder as appropriate.

---

## 1. Service Bus

### 1.1 Message Operations

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| SB-1 | 🔴 | **Bulk import from file** — Allow sending messages from a JSON/NDJSON file via file picker. Map to `SendBatchAsync`. | `MessageComposer.razor` |
| SB-2 | 🔴 | **Replay history / audit log** — Persist replay actions (timestamp, source entity, target entity, message ID) in `profiles.json`. Show a "Replay History" panel. | `DemoServiceBusClient` also needs seeded entries |
| SB-3 | 🟡 | **Persist remap rules per entity** — Currently remap rules in the composer are session-only. Save them to `AppConfig` keyed by `NamespaceId:EntityPath` like saved filters. | `MessageComposer.razor`, `AppConfig.cs` |
| SB-4 | 🟡 | **Partition key / Session ID editor** — The composer does not expose `PartitionKey` or `SessionId`. Add them as optional fields under an "Advanced" collapsible section. | `ServiceBusModels.cs`, `SbMessage` |
| SB-5 | 🟡 | **DLQ batch feedback** — Show per-message success/failure during batch resubmit/complete instead of a single pass/fail toast. | `DlqView.razor` |
| SB-6 | 🟢 | **Connection test in Settings** — Add a "Test connection" button to `ServiceBusConfigForm.razor` consistent with AKS/Redis/Storage patterns. | `ServiceBusConfigForm.razor` |
| SB-7 | 🟢 | **Service Bus emulator / sovereign cloud** — Allow custom endpoint override for emulator (`localhost:5672`) or sovereign clouds. Add an `Endpoint` field to `ServiceBusNamespace`. | `ServiceBusNamespace.cs` |

### 1.2 Entity Browser

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| SB-8 | 🔴 | **Production safeguard** — Warn when the active environment is tagged as production before any destructive action (DLQ complete, bulk resubmit). Currently `IsProduction=false` is hardcoded. Wire to `AppContext.Environment.IsProduction`. | `ServiceBusPage.razor:201`, `AppContext` |
| SB-9 | 🟡 | **Transfer queue visibility** — Transfer DLQ stats are listed in `SbEntityStats` but not browsable. Add a "Transfer DLQ" tab alongside the standard DLQ tab. | `EntityTree.razor`, `AzureServiceBusClient.cs` |
| SB-10 | 🟡 | **Auto-refresh badge polling** — Entity message counts in the tree update only on manual refresh. Add configurable auto-poll for the tree (separate from `MessageListView` auto-refresh). | `EntityTree.razor` |
| SB-11 | 🟢 | **Namespace alias from custom input** — Currently alias is auto-derived by splitting the FQDN on `.`. Allow user to override the alias in the Add Namespace dialog. | `ServiceBusPage.razor:405` |

### 1.3 Scheduled Messages

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| SB-12 | 🟡 | **Broker-sync for scheduled messages** — Reconcile local `ScheduledMessageRepository` with broker on tab open: mark externally-cancelled messages as "Cancelled" instead of leaving them as "Pending". | `ScheduledMessages.razor`, `AzureServiceBusClient.cs` |
| SB-13 | 🟢 | **Cancel-and-remove race guard** — When cancellation fails (message already enqueued), keep the entry in the list with status "Enqueued" rather than removing it silently. | `ScheduledMessages.razor:132` |

### 1.4 UX Polish

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| SB-14 | 🟡 | **Toast on clipboard copy** — Body copy, MessageId copy, and sequence number copy are all silent. Fire a short "Copied!" notification via `INotificationService`. | `MessageDetailPane.razor` |
| SB-15 | 🟡 | **Detail pane flash fix** — When selection is cleared, briefly stale content flashes. Add a `null`-guard render gate `@if (Message != null)` that fades out cleanly. | `MessageDetailPane.razor` |
| SB-16 | 🟢 | **CSV escaping hardening** — Current CSV export only escapes `,`, `"`, `\n`. Add `\r`, tab, and Unicode control-character escaping. | `MessageListView.razor:523` |
| SB-17 | 🟢 | **Connection string masking** — In namespace list and tooltip, mask the connection string after `Endpoint=sb://...;` to avoid accidental credential exposure. | `ServiceBusPage.razor` |

---

## 2. AKS

### 2.1 Log Streaming

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-1 | 🔴 | **Container selector in PodLogView** — Pods with multiple containers always stream the default container. Add a container dropdown populated from `PodInfo.Containers`. | `PodLogView.razor`, `KubernetesAksClient.cs` |
| AKS-2 | 🔴 | **Scroll-to-bottom on new lines** — New log lines arrive but the view does not auto-scroll. Add a "tail" toggle that pins scroll to bottom; disable when user scrolls up. | `PodLogView.razor`, `MultiPodLogView.razor` |
| AKS-3 | 🟡 | **Multi-pod log ordering** — Aggregated logs arrive in arrival order across pods. Add a timestamp prefix and offer a "Merge & sort by time" toggle. | `MultiPodLogView.razor`, `AggregatedLogLine` model |
| AKS-4 | 🟡 | **Log buffer increase / configurable** — 10 K line buffer is hardcoded. Make it configurable in `AksConfig` (e.g., 5 K / 10 K / 50 K). | `PodLogView.razor`, `AksConfig.cs` |
| AKS-5 | 🟢 | **Pause log tail** — "Pause" button that stops streaming but keeps buffer. "Resume" restarts stream. Distinct from the live on/off toggle. | `PodLogView.razor` |

### 2.2 Multi-namespace Mode

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-6 | 🔴 | **Show Ingresses and Events in `*` mode** — Multi-namespace currently clears Ingresses, Events, ConfigMaps, Secrets, Helm, Metrics, CronJobs. At minimum show Ingresses and Events with a namespace column. | `AksPage.razor:1086`, `LoadAsync` |
| AKS-7 | 🟡 | **Visible signal for hidden resource types** — When `*` is selected, display a notice in each empty tab explaining "Not available in all-namespaces view" rather than just showing empty grids. | `AksPage.razor` tab rendering |

### 2.3 YAML Editor

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-8 | 🔴 | **YAML validation before apply** — Currently edited YAML is sent to the cluster as-is; errors come back raw. Pre-validate YAML structure client-side (parse to `Dictionary<string,object>`) and surface friendly messages before the API call. | `AksPage.razor` apply logic, `KubernetesAksClient.cs` |
| AKS-9 | 🟡 | **Find/replace in YAML editor** — Inline search highlights matches but offers no replace. Add basic find/replace for quick image tag bumps. | `AksPage.razor` YAML overlay |
| AKS-10 | 🟢 | **Diff view on YAML edit** — Before applying, show a side-by-side diff (original vs. edited). Reduces risk of accidental overwrites. | New component or Monaco diff view |

### 2.4 Metrics & HPA

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-11 | 🟡 | **Metrics unavailable explanation** — When Metrics Server is absent, CPU/Memory columns show "—" with no explanation. Display a tooltip or info banner "Metrics Server not found on this cluster". | `PodGrid.razor`, `AksPage.razor` |
| AKS-12 | 🟡 | **HPA real-time refresh** — HPA detail panel loads once and does not refresh. Add a refresh button and optionally auto-refresh alongside the main auto-refresh cycle. | `HpaPanel.razor` |
| AKS-13 | 🟢 | **Configurable metric bar scale** — CPU bar is scaled to ~500 m, memory to ~512 Mi, both hardcoded. Derive scale from node capacity or let user configure thresholds in `AksConfig`. | `PodGrid.razor` |

### 2.5 Port-Forward

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-14 | 🟡 | **Copy localhost URL in grid row** — Currently only available in the sessions panel. Add a "Copy URL" button directly in the port-forward session row. | `PortForwardSessionsPanel.razor` |
| AKS-15 | 🟡 | **Port availability check** — Before spawning `kubectl port-forward`, check if the local port is free (TCP connect test). Show a "Port in use" warning with a suggested alternative. | `PortForwardStartDialog.razor`, `KubernetesAksClient.cs` |
| AKS-16 | 🟢 | **Session error detail expansion** — `LastError` in `PortForwardSession` may be long; currently truncated. Add an expandable error detail card. | `PortForwardSessionsPanel.razor` |

### 2.6 Keyboard & Navigation

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-17 | 🔴 | **Grid keyboard nav for Pods and StatefulSets** — Only Deployments have ↑↓/shortcut keys. Extend the same pattern to all AKS resource grids. | `AksPage.razor:1190`, `HandleGridKeyDown` |
| AKS-18 | 🟡 | **"Copy name" context menu item** — All context menus lack "Copy name/namespace". Add it as the first item across all resource types. | All `*Grid.razor` components |
| AKS-19 | 🟡 | **Configurable auto-refresh interval** — Interval is hardcoded. Expose it as a dropdown in the toolbar (e.g., 15 s / 30 s / 60 s / Off). | `AutoRefreshToggle.razor`, `AksConfig.cs` |

### 2.7 Secrets

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| AKS-20 | 🟡 | **Bulk secret reveal with confirmation** — Current per-key reveal is safe but tedious. Add a "Reveal all" button gated behind a single confirmation. | `SecretDetailPanel.razor` |
| AKS-21 | 🟢 | **Secret view audit hint** — Display a subtle "Values viewed at HH:MM" timestamp after a reveal to help users track exposure during debugging sessions. | `SecretDetailPanel.razor` |

---

## 3. Observability (App Insights)

### 3.1 Logs Tab

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| OBS-1 | 🔴 | **Monaco editor for KQL** — The logs tab uses a plain `<textarea>`. Replace with the `BlazorMonaco` component already in the tech stack for syntax highlighting, bracket matching, and Ctrl+Z undo. | `ObservabilityLogs.razor:40`, `BlazorMonaco` dependency |
| OBS-2 | 🟡 | **Query validation feedback** — Surface Azure Monitor query parse errors inline below the editor instead of just showing an empty result. | `AzureAppInsightsProvider.cs`, `ObservabilityLogs.razor` |
| OBS-3 | 🟡 | **Saved query folders** — Let users organise saved queries into named folders (e.g., "Performance", "Errors"). Simple string prefix `"folder/name"` in the query name. | `ObservabilityConfig.cs`, `ObservabilityLogs.razor` |
| OBS-4 | 🟢 | **Export to JSON / Excel** — Current "Copy CSV" copies to clipboard. Add a "Download" button for JSON and CSV file export. | `ObservabilityLogs.razor` |
| OBS-5 | 🟢 | **MaxRowsPerQuery in Settings UI** — Currently only editable in `profiles.json`. Expose as a numeric input in the Observability Settings card. | `SettingsPage.razor:113`, `ObservabilityConfig.cs` |

### 3.2 Overview & Charts

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| OBS-6 | 🔴 | **Performance tab trend charts** — Backend queries P99 but the detail pane only shows static stat cards. Render the ApexCharts latency trend that the frontend doc specifies. | `ObservabilityPerformance.razor:56–62`, `AzureAppInsightsProvider.cs` |
| OBS-7 | 🟡 | **Auto-refresh toggle** — Azure Monitor has ingestion lag; a 1–5 min auto-refresh toggle (with a "last updated" timestamp) avoids constant manual F5. | `ObservabilityPage.razor` |
| OBS-8 | 🟡 | **Local timezone display** — All chart timestamps are UTC. Detect browser timezone via `IJSRuntime` and convert display values. | All chart components, `TimeRange` model |
| OBS-9 | 🟢 | **User-configurable metric thresholds** — Failure rate and latency colour thresholds are hardcoded (e.g., red > 5% failure). Allow user to override in `ObservabilityConfig`. | `ObservabilityOverview.razor`, `ObservabilityPerformance.razor` |

### 3.3 Discovery & Resource Selection

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| OBS-10 | 🟡 | **Subscription scan progress counter** — The discovery dialog shows a spinner but no "Scanning 3 / 12 subscriptions" counter. Yield a progress record from `DiscoverResourcesAsync` alongside resource records. | `AppInsightsDiscoveryService.cs`, `ResourceSelectorDialog.razor` |
| OBS-11 | 🟡 | **Resource type badge** — `ObservabilityResourceInfo` only shows name and resource group. Add a "workspace type" badge (App Insights Classic vs. Workspace-based) derived from the ARM resource type. | `AppInsightsDiscoveryService.cs`, `ResourceSelectorDialog.razor` |
| OBS-12 | 🟢 | **Demo mode isolated resources** — All demo presets share the same seed data. Add 2–3 distinct demo resource "profiles" (e.g., "high-traffic app", "quiet service") with different metric shapes. | `DemoObservabilityProvider.cs` |

### 3.4 Failures & Traces

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| OBS-13 | 🟡 | **Trace ID drill-down link** — Exception detail shows stack trace but no correlation. If a `operationId` / `traceId` is present in the exception record, add a "View trace" button that queries the Logs tab with a pre-built KQL. | `ObservabilityFailures.razor`, `KqlPresets.cs` |
| OBS-14 | 🟡 | **Copy feedback on stack trace copy** — The copy button is silent. Show a "Copied!" toast via `INotificationService`. | `ObservabilityFailures.razor:67` |
| OBS-15 | 🟢 | **Availability heatmap** — Frontend doc specifies a heatmap (time × test/location). Current implementation is a flat list. Implement an ApexCharts heatmap. | `ObservabilityAvailability.razor` |

---

## 4. Redis

### 4.1 Key Browser

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| RDS-1 | 🔴 | **Key scan pagination** — All keys are loaded in a single SCAN sweep. For large databases this blocks the UI. Implement cursor-based paging: load the first 1 000 keys, show a "Load more" button, or scan lazily on tree node expand. | `RedisPage.razor:236–246`, `IRedisClient` |
| RDS-2 | 🟡 | **Binary content detection** — Redis strings can hold binary blobs. Check for non-printable bytes and show a "Binary content — cannot display" badge instead of garbled text. | `RedisKeyDetail.razor`, `RedisClient.cs` |
| RDS-3 | 🟡 | **Sorted set score editing** — Sorted set display is read-only (scores shown but not editable). Add inline score update via `ZADD XX` command. | `RedisKeyDetail.razor` |
| RDS-4 | 🟡 | **List / set pagination** — Items capped at 100 (hardcoded). Replace with a "Load more" pattern using `LRANGE`/`SSCAN` offset. | `RedisPage.razor:313, 316` |
| RDS-5 | 🟢 | **Copy key name button** — No one-click way to copy a full Redis key to clipboard. Add a copy icon next to the key in the detail header. | `RedisKeyDetail.razor` |
| RDS-6 | 🟢 | **Key rename** — Allow renaming a key via `RENAME` command. Show inline input, confirm with Enter. | `RedisPage.razor`, `IRedisClient` |

### 4.2 TTL & Expiry

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| RDS-7 | 🟡 | **Preserve TTL countdown across separator change** — Changing the namespace separator rebuilds the entire tree, restarting all TTL countdown loops and losing precision. Rebuild only the grouping layer; keep key nodes stable. | `RedisPage.razor`, `RedisKeyGrouper.cs` |
| RDS-8 | 🟢 | **TTL copy to clipboard** — "Set TTL" dialog should pre-populate with the current remaining TTL for easy extension (e.g., double the current TTL). | `RedisKeyDetail.razor` |

### 4.3 Operations

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| RDS-9 | 🟡 | **Multi-key delete** — Currently only single key delete is supported. Add checkbox multi-select in the tree and a batch delete action. | `RedisNamespaceTree.razor`, `RedisPage.razor`, `IRedisClient` |
| RDS-10 | 🟡 | **Connection string masking** — Connection string is shown in plain text in `RedisConfigForm`. Mask after the first 20 chars (or offer a "show" toggle). | `RedisConfigForm.razor` |
| RDS-11 | 🟢 | **Hash field add / delete** — Hash editing supports per-field value updates but not adding new fields or deleting existing ones. Add `+` / `–` row actions. | `RedisKeyDetail.razor` |
| RDS-12 | 🟢 | **Export keys to JSON** — Add an "Export" button in the prefix-memory or key-tree view to download visible keys + values as JSON. | `RedisPage.razor` |

---

## 5. Storage

### 5.1 Blob Operations

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| STG-1 | 🔴 | **Blob upload** — The current MVP is read-only. Add a drag-and-drop / file picker upload targeting the current virtual folder prefix. Gate behind an "Allow mutations" toggle per environment. | `StoragePage.razor`, `StorageBlobList.razor`, `IStorageClient` |
| STG-2 | 🔴 | **Blob delete** — Allow deleting a selected blob (with confirmation). Required for any real debugging workflow. Same mutations gate. | `BlobDetailPane.razor`, `IStorageClient` |
| STG-3 | 🟡 | **Bulk operations** — Add checkbox multi-select on blob rows for bulk delete and bulk download (zip). | `StorageBlobList.razor` |
| STG-4 | 🟡 | **SAS expiry customisation** — SAS URLs have a hardcoded 24-hour expiry. Add a duration picker (1h / 8h / 24h / 7d / 30d / custom) to the copy SAS action. | `StorageBlobList.razor`, `AzureStorageClient.cs` |
| STG-5 | 🟡 | **Copy blob path / relative path** — Add "Copy relative path" (just the blob name without account/container) as a context menu option alongside Copy URL and Copy SAS. | `StorageBlobList.razor` |
| STG-6 | 🟢 | **Blob versioning support** — If versioning is enabled on the container, add a "Versions" tab in `BlobDetailPane` listing historical versions with their timestamps and size. | `BlobDetailPane.razor`, `IStorageClient` |
| STG-7 | 🟢 | **Binary detection via magic bytes** — Currently binary detection uses only Content-Type header. Add a magic-byte sniff of the first 512 bytes as a fallback for blobs with missing/wrong content type. | `AzureStorageClient.cs`, `BlobDetailPane.razor` |

### 5.2 Container Browser

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| STG-8 | 🟡 | **Container-level properties** — The container tree shows name only. Display `Last-Modified`, `LeaseStatus`, and `PublicAccess` as tooltip or inline badges on hover. | `StorageContainerTree.razor` |
| STG-9 | 🟡 | **Sorting options for blob list** — Blobs appear in server order. Add sortable columns (name, size, last modified) via `IQueryable` sort in `StorageBlobList`. | `StorageBlobList.razor` |
| STG-10 | 🟢 | **Search across blobs in container** — Add a filter input above the blob list that calls `ListBlobsByPrefixAsync` with a combined prefix/filter pattern. | `StorageBlobList.razor`, `IStorageClient` |

### 5.3 Config & Auth

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| STG-11 | 🟡 | **Connection string masking** — Connection string ref key is visible in plain text in `StorageConfigForm`. Mask after first 20 chars with a "show" toggle. | `StorageConfigForm.razor` |
| STG-12 | 🟢 | **Container-level SAS** — In addition to blob-level SAS, offer a "Copy container SAS" action from the container tree for time-scoped access tokens. | `StorageContainerTree.razor`, `AzureStorageClient.cs` |

---

## 6. Releases & Pipelines

### 6.1 Pipeline Hub (pipelines-revamp)

> The `pipelines-revamp` feature is already Planned (see `docs/features/active/pipelines-revamp/`). The items below are additions to that plan.

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| REL-1 | 🔴 | **Release selector search/filter** — The current `<select>` dropdown has no search. Replace with a Fluent UI `FluentCombobox` or filter input, especially important once many releases are defined. | `ReleasesPage.razor` |
| REL-2 | 🟡 | **Unsaved changes warning** — Navigating away from the Release editor without saving loses edits silently. Implement a `NavigationManager.RegisterLocationChangingHandler` guard. | `ReleaseEditor.razor` |
| REL-3 | 🟡 | **Approval comment history** — After approving/rejecting, comments are lost from the UI. Persist them in the approval result and show them in a collapsed "History" section per stage. | `ApprovalCenter.razor`, `DevOpsModels.cs` |
| REL-4 | 🟡 | **Tag confirmation shortcut** — Readiness gate requires manual "confirm tag" toggle. Add a keyboard shortcut (e.g., `Ctrl+K`) and a visual "Confirm All" button in the readiness summary row. | `ReadinessGate.razor` |
| REL-5 | 🟢 | **Pipeline run duration column** — Activity feed and Release Board show status but not run duration. Add elapsed/total time per run stage. | `ReleaseBoard.razor`, `PipelineTriggerHub.razor` |
| REL-6 | 🟢 | **Delete release confirmation consistency** — Release deletion uses inline styles instead of the `ConfirmDialog` component. Migrate to `ConfirmDialog` for visual consistency. | `ReleasesPage.razor` delete handler |

---

## 7. Global UI Shell & Cross-cutting

### 7.1 Command Palette

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-1 | 🔴 | **Register area-specific commands** — Service Bus, AKS, Redis, Storage, Releases pages all have zero commands in the palette. Wire up their common actions (Peek, DLQ, Restart, Scale, Flush DB, etc.) via `ICommandPaletteService` registration in each page's `OnInitialized`. | All feature pages |
| UI-2 | 🟡 | **Context-aware command filtering** — `CurrentArea` cascading param exists but availability predicates are not wired. Implement `IAvailabilityContext` checks so destructive commands only appear when a resource is selected. | `CommandPalette.razor`, `ICommandPaletteService` |
| UI-3 | 🟡 | **Prefix-boosted fuzzy search** — Scoring treats all character matches equally. Boost +3 for matches at the start of the command label to rank "Peek" above "Open Peek Dialog". | `CommandPalette.razor:184–198` |
| UI-4 | 🟢 | **Go-to resource sub-commands** — After typing "Go to", show a filtered list of open tabs or known namespaces/clusters. Useful for projects with many environments. | `CommandPalette.razor` |

### 7.2 Keyboard Navigation

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-5 | 🔴 | **Grid keyboard nav completeness** — Redis key tree, Storage blob list, and Releases board have no keyboard selection. Implement the same ↑↓/Enter/Escape pattern used in AKS Deployments. | `RedisPage.razor`, `StorageBlobList.razor`, `ReleaseBoard.razor` |
| UI-6 | 🟡 | **Focus restoration on modal close** — After any modal (composer, YAML editor, confirm dialog) is dismissed, return focus to the triggering element. Store a `_triggerRef` before opening. | `Modal.razor`, `ConfirmDialog.razor` |
| UI-7 | 🟡 | **ISelectionContext service** — Push selected resource (deployment, message, blob, key) to a shared `ISelectionContext` service so the command palette and keyboard shortcuts can act on it without prop-drilling. | New `ISelectionContext` abstraction in `SwebKit.Core` |

### 7.3 Error Handling & Loading

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-8 | 🔴 | **Generic error boundary** — Unhandled async errors in `OnInitializedAsync` can crash the page to a blank state. Add a `<CascadingErrorBoundary>` wrapper in `MainLayout` that renders a recovery UI. | `MainLayout.razor`, new `ErrorBoundary.razor` |
| UI-9 | 🟡 | **Skeleton loaders** — Pages render blank while loading. Add skeleton placeholder rows to the main data grids (Service Bus message list, AKS grids, Redis key tree) using CSS shimmer animations. | All main page components |
| UI-10 | 🟡 | **Retry with backoff on ErrorCallout** — Retry buttons fire immediately on click with no attempt counter or delay. Add exponential backoff (1 s / 2 s / 4 s) and a "Retry #2" label. | `ErrorCallout.razor` |
| UI-11 | 🟢 | **Error message expansion** — Long error messages in `ErrorCallout` and `NotificationToast` are truncated with no way to see full text. Add a "Show more" toggle or a detail flyout. | `ErrorCallout.razor`, `NotificationToast.razor` |

### 7.4 Notifications & Feedback

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-12 | 🟡 | **Consistent copy feedback** — At least 12 copy-to-clipboard actions across the app fire silently. Standardise on a 2-second `INotificationService.ShowSuccess("Copied!")` notification after every clipboard write. | All `JSInterop` clipboard calls |
| UI-13 | 🟡 | **Persistent notification history** — Bell icon in TopBar shows unread count but the history dropdown is not yet persisted between sessions. Store the last 50 notifications in `UiStateRepository`. | `TopBar.razor`, `INotificationService` |
| UI-14 | 🟢 | **Action progress in status bar** — Background tasks (batch resubmit, AKS restart, tag creation) show a spinner count in the status bar but no progress percentage. Add an optional `Progress` (0–100) field to the background task model. | `StatusBar.razor`, task model |

### 7.5 Settings & Configuration

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-15 | 🔴 | **Unsaved changes detection** — Settings forms mutate `AppConfig` directly. If user navigates away without saving, changes are lost silently. Track dirty state and show a navigation guard. | `SettingsPage.razor`, all config forms |
| UI-16 | 🟡 | **Form validation highlighting** — Validation errors appear as text banners but don't highlight the invalid field. Add red border + aria-invalid on required fields that are empty. | All config form components |
| UI-17 | 🟡 | **Environment clone** — No way to duplicate an environment configuration. Add a "Clone environment" action that copies the full `AppConfig` of one environment into a new one. | `SettingsPage.razor`, `AppState` |
| UI-18 | 🟡 | **Config export / import** — No cross-machine migration path. Add "Export config" (downloads sanitised `profiles.json` without credential values) and "Import config" (merges config, prompts for secrets). | `AppState`, `ProfileRepository`, new `ConfigExportService` |
| UI-19 | 🟢 | **Connection string masking globally** — Three separate config forms show connection strings in plain text. Centralise masking in a `<PasswordField>` component used everywhere. | `RedisConfigForm`, `StorageConfigForm`, `ServiceBusPage` |

### 7.6 Theme & Accessibility

| # | Priority | Improvement | Notes |
|---|----------|-------------|-------|
| UI-20 | 🟡 | **System dark/light preference auto-detect** — Default theme selection ignores OS `prefers-color-scheme`. On first launch (no localStorage value), apply dark or light based on system preference. | `MainLayout.razor:82–95` |
| UI-21 | 🟡 | **ARIA labels on interactive elements** — Nav items, toolbar buttons, context menus, and data grid action buttons lack `aria-label`. Add descriptive labels to all icon-only controls. | Pervasive; prioritise TopBar, LeftNav, StatusBar |
| UI-22 | 🟡 | **Color-blind safe status indicators** — Status dots (connected/error) and severity badges rely solely on color. Add shape or text cues (e.g., ✓ / ✗ icons or abbreviated text). | `StatusBar.razor`, severity badges throughout |
| UI-23 | 🟢 | **Visible focus rings** — Default browser focus styles are subtle on the dark theme. Add a custom `outline: 2px solid var(--color-accent)` focus ring across all interactive elements in `app.css`. | `app.css` |
| UI-24 | 🟢 | **Demo banner CSS variable** — Demo mode banner uses hardcoded `#d97706`. Replace with a CSS token (e.g., `--color-warning`) from the theme system. | `TopBar.razor` or `MainLayout.razor` |

---

## Priority summary

| Priority | Count |
|----------|-------|
| 🔴 High  | 19    |
| 🟡 Medium| 46    |
| 🟢 Low   | 24    |
| **Total**| **89**|

## Dependencies

- `ISelectionContext` (UI-7) unblocks `ICommandPaletteService` area commands (UI-1, UI-2)
- Monaco editor (OBS-1) requires no new NuGet; `BlazorMonaco` is already in the tech stack
- Blob mutations (STG-1, STG-2) require an "Allow mutations" environment flag — design that flag first
- `pipelines-revamp` active feature takes precedence over REL items for Releases

## Links

- [Service Bus architecture](../../../architecture/functionalities/service-bus.md)
- [AKS architecture](../../../architecture/functionalities/aks.md)
- [Observability architecture](../../../architecture/functionalities/observability.md)
- [Redis architecture](../../../architecture/functionalities/redis.md)
- [Storage architecture](../../../architecture/functionalities/storage.md)
- [Releases architecture](../../../architecture/functionalities/releases.md)
- [Pipelines revamp feature](../pipelines-revamp/index.md)
- [Command palette feature](../command-palette-keyboard-first/index.md)
- [UI/UX revamp feature](../ui-ux-revamp/index.md)
