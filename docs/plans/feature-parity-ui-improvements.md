# Feature Parity & UI Improvements Plan

Comprehensive plan to bring the Tauri/React frontend to full feature parity with the MAUI app, including native OS integration via Tauri bridges and sidecar filesystem access.

## Execution Priority

All four major feature areas are **high priority**. Execute in this order:
1. Service Bus (filtering + composer) — most immediate user pain
2. API Client UI overhaul — user explicitly unhappy with current UI
3. AKS full detail panels — largest gap in component count
4. Redis advanced features — moderate gaps
5. Storage advanced features — moderate gaps
6. Missing pages (Pipelines, Observability, Monitoring, Incident Timeline) — new pages
7. Layout/shell features (command palette, notifications, themes, keyboard shortcuts)

---

## 1. Service Bus: Filtering + Composer

### Current State
- `MessageList.tsx` (84 lines) — flat list, no filtering, no search
- `MessageDetail.tsx` (7,259 bytes) — basic detail with complete/deadletter/resubmit/purge actions
- `EntityTree.tsx` — basic entity tree
- `ServiceBusPage.tsx` — namespace selector + entity tree + message list + detail

### MAUI Reference
- `MessageListView.razor` (1,817 lines, 83KB) — text filter + advanced multi-rule filtering (application property, enqueued time, delivery count, sequence number), column toggle, session pinning, virtualized rendering, keyboard navigation
- `MessageComposer.razor` (526 lines) — compose/replay/edit/schedule modes, template loading, target namespace/entity selection
- `BatchSendPanel.razor` — batch send with CSV import
- `BatchReplayPanel.razor` — batch replay across namespaces
- `TemplatePicker.razor` — message template picker
- `ScheduledMessages.razor` — scheduled messages view
- `EntityCommandPalette.razor` — quick actions command palette
- `MessageDetailPane.razor` (405 lines) — edit & resubmit, replay, schedule, copy body, copy full message, save as template, investigate

### Tasks

#### 1.1 Message List Filtering ✅ DONE
- [x] Add text search bar to `MessageList.tsx` (filter by messageId, correlationId, subject, body)
- [x] Add advanced filter panel with rule builder:
  - Field selector: Application Property, Enqueued Time, Delivery Count, Sequence Number
  - Operator selector: contains/equals/not-equals/regex (text), before/after/on-or-before (date), gt/gte/lt/lte (numeric)
  - Property name input (for application property field)
  - Value input with appropriate placeholder
  - Enable/disable toggle per rule
  - Add/remove rules
- [x] Implement client-side filter logic matching `ApplyFilters` / `MatchesAdvancedRule` from MAUI
- [ ] Add column toggle dropdown (Enqueued, Message ID, Correlation ID, Subject, Delivery, Expires, Content Type, Session, Partition Key, DLQ Reason) — deferred
- [ ] Add session pinning filter (filter by SessionId) — deferred
- [x] Add filter toggle button to show/hide filter bar

#### 1.2 Message Composer ✅ DONE
- [x] Create `MessageComposer.tsx` — modal/panel for composing messages
  - Mode: Compose, Replay, Edit & Resubmit, Schedule
  - Target namespace + entity selectors (for replay cross-namespace)
  - Message body editor (textarea with JSON formatting)
  - Application properties key-value grid
  - Content type selector
  - Subject, CorrelationId, SessionId, PartitionKey inputs
  - Send / Schedule / Cancel buttons
- [x] Add "Compose" button to `ServiceBusPage.tsx` header
- [ ] Add "Edit & Resubmit" button to `MessageDetail.tsx` — deferred (composer supports mode, needs wiring)
- [ ] Add "Replay" button to `MessageDetail.tsx` — deferred (composer supports mode, needs wiring)
- [ ] Add "Schedule" button to `MessageDetail.tsx` — deferred (composer supports mode, needs wiring)
- [x] Add sidecar endpoint: `POST /api/servicebus/{nsId}/entities/{entityPath}/send` (already existed)
- [x] Add sidecar endpoint: `POST /api/servicebus/{nsId}/entities/{entityPath}/schedule`
- [x] Add sidecar endpoint: `DELETE /api/servicebus/{nsId}/entities/{entityPath}/scheduled/{sequenceNumber}`
- [x] Add React Query hook: `useSbSendMessage` (already existed), `useSbScheduleMessage`

#### 1.3 Message Templates ✅ DONE
- [x] Create `TemplatePicker.tsx` — modal for selecting saved message templates
- [x] Add "Save as Template" button to `MessageDetail.tsx`
- [x] Add "Load Template" button to `MessageComposer.tsx`
- [x] Add sidecar endpoints: `GET/POST/DELETE /api/servicebus/templates`
- [x] Add React Query hooks: `useSbTemplates`, `useSbSaveTemplate`, `useSbDeleteTemplate`

#### 1.4 Batch Operations ✅ DONE
- [x] Create `BatchSendPanel.tsx` — CSV/JSON import + batch send with preview
- [ ] Create `BatchReplayPanel.tsx` — select messages + replay to different namespace/entity — deferred (resubmit endpoint exists, UI can be added later)
- [x] Add "Batch Send" button to `ServiceBusPage.tsx` header
- [x] Add sidecar endpoint: `POST /api/servicebus/{nsId}/entities/{entityPath}/batch-send`
- [ ] Add sidecar endpoint: `POST /api/servicebus/{nsId}/entities/{entityPath}/batch-replay` — deferred (resubmit endpoint covers this)

#### 1.5 Scheduled Messages View ✅ DONE
- [x] Create `ScheduledMessages.tsx` — panel showing scheduled messages with cancel option
- [x] Add "Scheduled" button to `ServiceBusPage.tsx`
- [x] Add sidecar endpoint: `GET /api/servicebus/{nsId}/entities/{entityPath}/scheduled`
- [x] Add sidecar endpoint: `DELETE /api/servicebus/{nsId}/entities/{entityPath}/scheduled/{sequenceNumber}`
- [x] Register `ScheduledMessageRepository` in sidecar DI
- [x] Schedule endpoint now saves entry to repository for tracking

#### 1.6 Message Detail Enhancements ✅ DONE
- [x] Add "Copy Body" button with clipboard feedback
- [x] Add "Copy Full Message" button (JSON export of all properties + body)
- [x] Improve message detail layout: tabs for Body / Properties / System / DLQ Info
- [x] Replace `confirm()` with inline purge confirmation dialog
- [ ] Add JSON syntax highlighting for body (use a lightweight highlighter or monaco editor) — deferred

#### 1.7 Entity Command Palette
- [ ] Create `EntityCommandPalette.tsx` — quick search + action palette for entities
- [ ] Add keyboard shortcut (Ctrl+K) to open
- [ ] Actions: peek active, peek DLQ, send message, purge, refresh

---

## 2. API Client UI Overhaul

### Current State
- `ApiClientPage.tsx` (278 lines) — basic 3-pane layout, prompt-based collection/folder/request creation
- `CollectionTree.tsx` (6,879 bytes) — flat list, no drag-drop, no context menu, no rename inline
- `RequestEditor.tsx` (334 lines) — method, URL, headers, query params, body (none/json/xml/text/formdata), auth (none/bearer/basic/apikey)
- `ResponseViewer.tsx` (104 lines) — body + headers tabs, status badge, elapsed, size

### MAUI Reference
- `ApiClientPage.razor` (696 lines) + partials (Collections, Commands, Curl, LinkedSave, Requests, Secrets, Tabs, Tree) — massive feature set
- `CollectionTree.razor` (1,043 lines) — virtualized, drag-drop, context menu, rename inline, linked repos, Git badges, Bruno sync
- `RequestBuilderPanel.razor` (651 lines) — URL bar, method picker, tabs (Params/Headers/Body/Auth/Tests/Capture), variable preview, body formatter, WebSocket/GraphQL modes
- `ResponseViewerPanel.razor` (444 lines) — status badge, history, response examples, subscription messages, pretty-print, copy, capture warnings
- `AuthPanel.razor` + `BasicAuthForm`, `BearerAuthForm`, `ApiKeyAuthForm`, `OAuth2AuthForm` — full auth support
- `GraphQlPanel.razor` — GraphQL query editor, variables, operation selector, subscription support
- `WebSocketPanel.razor` — WebSocket connection panel
- `EnvironmentEditor.razor`, `EnvironmentManagerPanel.razor` — environment management
- `CollectionVariableEditor.razor` — collection-level variables
- `ApiClientGitPanel.razor` — Git branch, commit, diff, push
- `ApiClientToolbar.razor` — toolbar with environment selector, Bruno sync toggle
- `ApiClientOpenTabsStrip.razor` — multi-tab request editing
- `UnifiedBodyEditor.razor` — unified body editor with format detection
- `PostRequestCaptureBuilder.razor` — response capture rules
- `CollectionExportDialog.razor` — export collections
- `RequestQuickNavPanel.razor` — quick navigation

### Tasks

#### 2.1 Collection Tree Overhaul ✅ DONE
- [ ] Add virtualized rendering for large trees (react-window or similar) — deferred (premature optimization)
- [ ] Add drag-and-drop reordering (react-dnd or @dnd-kit) — deferred (requires external dep)
- [x] Add right-click context menu (new sub-folder, new request, rename, delete)
- [x] Add inline rename (double-click)
- [x] Add tree filter/search input
- [x] Add expand/collapse all
- [x] Add method badge colors per HTTP method
- [x] Add folder expand/collapse chevrons
- [x] Add collection-level icons
- [x] Replace `window.prompt`/`window.confirm` with proper modal dialogs (NameDialog + ConfirmDialog)

#### 2.2 Request Editor Overhaul ✅ DONE
- [x] Add tabbed layout: Params | Headers | Body | Auth
- [x] Add URL bar with method picker dropdown (styled per method)
- [ ] Add variable preview (show resolved URL with environment variables substituted) — deferred (needs env management first)
- [x] Add body editor improvements:
  - JSON pretty-print / minify toggle
  - XML format toggle (basic)
  - Form-data key-value grid (uses existing FormData mode)
  - Content type auto-detection (body mode drives content type)
- [x] Add auth panel with all auth types:
  - None, Bearer Token, Basic, API Key (header/query), OAuth2
  - OAuth2: authorization URL, token URL, scopes, client ID/secret, flow type
- [ ] Add capture rules editor — deferred (lower priority)
- [x] Add request name inline edit (input in header)
- [x] Add dirty indicator (unsaved changes: Save*)
- [ ] Add auto-save with debounce (500ms) — deferred (explicit save is safer)
- [x] Add keyboard shortcut: Ctrl+S to save, Ctrl+Enter to send

#### 2.3 Response Viewer Overhaul ✅ DONE
- [ ] Add response history (list of past responses for this request) — deferred
- [ ] Add response examples (saved response examples) — deferred
- [x] Add pretty-print for JSON/XML responses
- [x] Add copy response body button
- [x] Add copy as cURL button
- [x] Add response headers table
- [x] Add status badge with color coding (2xx green, 3xx blue, 4xx yellow, 5xx red)
- [ ] Add response time graph (sparkline of recent response times) — deferred
- [ ] Add capture warnings display — deferred
- [ ] Add GraphQL errors display — deferred (needs GraphQL support first)
- [ ] Add subscription messages panel — deferred (needs WebSocket support first)

#### 2.4 Environment & Variable Management ✅ DONE
- [x] Create `EnvironmentManager.tsx` — manage environments (create, edit, delete)
- [x] Create `EnvironmentEditor.tsx` — inline key-value editor for environment variables (integrated in EnvironmentManager)
- [x] Add environment selector dropdown to API Client toolbar
- [x] Create `CollectionVariableEditor.tsx` — collection-level variables
- [ ] Add variable substitution preview in request editor — deferred (needs variable resolution engine)
- [x] Add sidecar endpoints: `GET/PUT /api/config/environments` (already existed, full store replace)
- [x] Collection variables saved via existing `PUT /api/config/collections` endpoint
- [x] Pass `environmentId` to execute request endpoint
- [x] Add `useUpdateEnvironments` hook

#### 2.5 Git Integration (Tauri Native Bridge)
- [ ] Create Tauri command for filesystem access (read/write collection files)
- [ ] Create `GitPanel.tsx` — branch selector, commit dialog, diff viewer, push/pull
- [ ] Add "Link to Git Repo" dialog (folder picker via Tauri)
- [ ] Add "Re-import from Bruno" action
- [ ] Add "Export to Bruno folder" action
- [ ] Add linked repo badges in collection tree (branch name, dirty count)
- [ ] Add sidecar endpoints for Git operations (using LibGit2Sharp or shell git)

#### 2.6 Multi-Tab Request Editing
- [ ] Create `RequestTabs.tsx` — tab strip for open requests
- [ ] Add tab open/close, tab selection
- [ ] Per-tab dirty state tracking
- [ ] Per-tab response state
- [ ] Add setting toggle for tab mode vs single mode

#### 2.7 GraphQL & WebSocket Support
- [ ] Create `GraphQlPanel.tsx` — query editor, variables editor, operation selector
- [ ] Add GraphQL mode to request editor (when method is GraphQL)
- [ ] Add GraphQL subscription support (WebSocket-based)
- [ ] Create `WebSocketPanel.tsx` — WebSocket connection, send/receive messages
- [ ] Add WebSocket mode to request editor

#### 2.8 Collection Export
- [ ] Create `CollectionExportDialog.tsx` — export to Bruno/Postman format
- [ ] Add "Export Collection" action in context menu

---

## 3. AKS Full Detail Panels

### Current State
- `AksPage.tsx` (3,629 bytes) — namespace selector + tab bar (Pods, Deployments, Services, Secrets, Events, Helm)
- `PodsTab.tsx` (3,098 bytes) — basic pod grid
- `DeploymentsTab.tsx` (4,332 bytes) — basic deployment grid
- `ServicesTab.tsx` (1,669 bytes) — basic service grid
- `SecretsTab.tsx` (1,322 bytes) — basic secret grid
- `EventsTab.tsx` (1,933 bytes) — basic event list
- `HelmTab.tsx` (1,880 bytes) — basic Helm release list

### MAUI Reference (71 items!)
- `AksDetailPanels.razor` (1,145 lines) — host for all detail panels with tab bar
- `AksYamlViewer.razor` (620 lines) — YAML view/edit with syntax highlighting, apply/dry-run
- `PodLogView.razor` (810 lines) — single pod log viewer with follow, filter, container selector
- `MultiPodLogView.razor` — multi-pod log correlation
- `ContainerDetailPanel.razor` — container detail (image, ports, env, resources, probes, volume mounts)
- `AksHelmPanel.razor` — Helm history, values, rollback
- `HpaPanel.razor` — HPA detail with scaling metrics
- `IngressAnalysisPanel.razor` — ingress rules analysis
- `NetworkPolicyAnalysisPanel.razor` — network policy analysis
- `PortForwardSessionsPanel.razor` + `PortForwardStartDialog.razor` — port-forward management
- `ProbeFailurePanel.razor` — probe failure analysis
- `ResourceFilter.razor` — resource filtering
- `AutoRefreshToggle.razor` — auto-refresh toggle
- `NamespaceQuotaPanel.razor` — namespace resource quotas
- `PodDisruptionBudgetPanel.razor` — PDB details
- `PlacementConstraintsPanel.razor` — placement constraints
- `ConfigMapGrid.razor` + `ConfigMapDetailPanel.razor` — config maps
- `SecretGrid.razor` + `SecretDetailPanel.razor` — secrets with decode
- `StatefulSetGrid.razor`, `CronJobGrid.razor`, `JobGrid.razor` — workload types
- `GatewayGrid.razor`, `GatewayClassGrid.razor`, `HttpRouteGrid.razor` — Gateway API
- `IngressGrid.razor` — ingress grid
- `DeploymentGrid.razor` — deployment grid with scale
- `AksConfirmBar.razor` — confirmation bar for mutations
- `AksConnectionBar.razor` — connection status bar
- `AlertHistoryPanel.razor` — alert history
- `NamespaceMonitorSelector.razor` — namespace + monitor selector
- `ResizablePanel.razor` — resizable side panel

### Tasks

#### 3.1 Resource Grids (Missing Types)
- [ ] Add `StatefulSetGrid.tsx` — stateful set list with replicas, images
- [ ] Add `CronJobGrid.tsx` — cron job list with schedule, last run, next run
- [ ] Add `JobGrid.tsx` — job list with status, completion, duration
- [ ] Add `ConfigMapGrid.tsx` — config map list
- [ ] Add `ConfigMapDetailPanel.tsx` — config map data viewer
- [ ] Improve `SecretGrid.tsx` — add secret type, data count
- [ ] Add `SecretDetailPanel.tsx` — secret data viewer with base64 decode toggle
- [ ] Add `IngressGrid.tsx` — ingress list with hosts, paths, TLS
- [ ] Add `GatewayGrid.tsx` + `GatewayClassGrid.tsx` + `HttpRouteGrid.tsx` — Gateway API resources
- [ ] Add namespace resource filter (search bar + label selector)

#### 3.2 Pod Detail & Logs
- [ ] Create `PodDetailPanel.tsx` — pod detail with containers, conditions, events, node info
- [ ] Create `ContainerDetailPanel.tsx` — container detail (image, ports, env vars, resources, probes, volume mounts)
- [ ] Create `PodLogView.tsx` — log viewer with:
  - Container selector
  - Follow/tail toggle
  - Log filter (text search)
  - Timestamp toggle
  - Previous container logs toggle
  - Download logs
  - Auto-scroll
- [ ] Create `MultiPodLogView.tsx` — multi-pod log correlation (select multiple pods, view interleaved logs)
- [ ] Add sidecar endpoint: `GET /api/aks/{ns}/pods/{pod}/logs?container={container}&tail={n}&follow={bool}`
- [ ] Add WebSocket support for log streaming (via Tauri or sidecar WebSocket)

#### 3.3 YAML Viewer/Editor
- [ ] Create `YamlViewer.tsx` — YAML viewer with syntax highlighting
- [ ] Add edit mode with apply/dry-run
- [ ] Add sidecar endpoint: `GET /api/aks/{ns}/{resource}/{name}/yaml`
- [ ] Add sidecar endpoint: `PUT /api/aks/{ns}/{resource}/{name}/yaml` (apply)
- [ ] Add sidecar endpoint: `POST /api/aks/{ns}/{resource}/{name}/dry-run` (server-side dry run)

#### 3.4 Helm Panel
- [ ] Create `HelmPanel.tsx` — Helm release detail with:
  - History (revisions)
  - Values (user-defined + computed)
- [ ] Add rollback action with confirmation
- [ ] Add sidecar endpoint: `GET /api/aks/{ns}/helm/{release}/history`
- [ ] Add sidecar endpoint: `GET /api/aks/{ns}/helm/{release}/values`
- [ ] Add sidecar endpoint: `POST /api/aks/{ns}/helm/{release}/rollback`

#### 3.5 Scale & HPA
- [ ] Create `ScaleDialog.tsx` — scale deployment/statefulset with replica count
- [ ] Create `HpaPanel.tsx` — HPA detail with metrics, min/max replicas, current utilization
- [ ] Add sidecar endpoint: `POST /api/aks/{ns}/deployments/{name}/scale`
- [ ] Add sidecar endpoint: `GET /api/aks/{ns}/hpa`

#### 3.6 Port-Forward (Tauri Native Bridge)
- [ ] Create Tauri command for port-forward (kubectl port-forward subprocess management)
- [ ] Create `PortForwardStartDialog.tsx` — select pod, container, port mapping
- [ ] Create `PortForwardSessionsPanel.tsx` — active port-forward sessions with stop
- [ ] Add sidecar endpoint or Tauri IPC for port-forward management

#### 3.7 Analysis Panels
- [ ] Create `IngressAnalysisPanel.tsx` — ingress rule analysis (backends, TLS, host conflicts)
- [ ] Create `NetworkPolicyAnalysisPanel.razor` — network policy analysis (allowed traffic, denied traffic)
- [ ] Create `ProbeFailurePanel.tsx` — probe failure analysis (liveness/readiness failures)
- [ ] Create `NamespaceQuotaPanel.tsx` — resource quota usage
- [ ] Create `PodDisruptionBudgetPanel.tsx` — PDB details

#### 3.8 Auto-Refresh & UX
- [ ] Create `AutoRefreshToggle.tsx` — toggle with interval selector
- [ ] Add auto-refresh to all resource grids (polling with React Query refetchInterval)
- [ ] Create `AksConfirmBar.tsx` — confirmation bar for destructive actions
- [ ] Add keyboard shortcuts (e.g., 'l' for logs, 'y' for YAML, 'r' for refresh)

---

## 4. Redis Advanced Features

### Current State
- `RedisPage.tsx` (492 lines) — single-file component with scan, key list, key detail, server info, slow log

### MAUI Reference
- `RedisToolbar.razor` — pattern scan, refresh key, delete key, export JSON, auto-refresh, batch select/delete
- `RedisKeyDetail.razor` (501 lines) — key rename, TTL bar visualization, memory display, copy key name, value editing per type
- `RedisNamespaceTree.razor` + `RedisNamespaceTreeNode.razor` — namespace tree (colon-delimited key prefixes)
- `RedisKeyspaceHealthExplorer.razor` — keyspace health analysis
- `RedisPrefixMemory.razor` — prefix memory breakdown
- `RedisPubSubPanel.razor` — pub/sub channel listener
- `RedisSlowLogPanel.razor` — slow log with query details
- `RedisOpsInsightsPanel.razor` — operational insights aggregation
- `RedisConnectionBar.razor` — connection status bar

### Tasks

#### 4.1 Key Detail Enhancements
- [ ] Add key rename (inline edit with confirm)
- [ ] Add TTL bar visualization (color-coded, progress bar)
- [ ] Add memory usage display
- [ ] Add copy key name button
- [ ] Add value editing for all types (string, hash, list, set, zset)
- [ ] Add TTL set/remove controls
- [ ] Add delete key with confirmation

#### 4.2 Namespace Tree
- [ ] Create `NamespaceTree.tsx` — colon-delimited key prefix tree
- [ ] Expand/collapse namespaces
- [ ] Show key count per namespace
- [ ] Filter tree by pattern
- [ ] Select namespace to filter key list

#### 4.3 Batch Operations
- [ ] Add "Select all loaded keys" checkbox
- [ ] Add batch delete with confirmation
- [ ] Add batch selection count display
- [ ] Add "Clear selection" button
- [ ] Add export JSON (download all selected keys as JSON)

#### 4.4 Advanced Panels
- [ ] Create `KeyspaceHealthExplorer.tsx` — keyspace health (hit rate, eviction, memory)
- [ ] Create `PrefixMemory.tsx` — memory usage by key prefix
- [ ] Create `PubSubPanel.tsx` — subscribe to channels, view messages, publish
- [ ] Improve `SlowLogPanel` — show full command, client, duration with sorting
- [ ] Create `OpsInsightsPanel.tsx` — operational insights summary

#### 4.5 Connection Bar & Auto-Refresh
- [ ] Create `ConnectionBar.tsx` — connection status indicator
- [ ] Add `AutoRefreshToggle.tsx` — auto-refresh with interval
- [ ] Add cache selector dropdown (for multi-cache configs)

#### 4.6 Sidecar Endpoints
- [ ] `POST /api/redis/{cacheId}/keys/{key}/rename` — rename key
- [ ] `POST /api/redis/{cacheId}/keys/{key}/ttl` — set TTL
- [ ] `DELETE /api/redis/{cacheId}/keys/{key}/ttl` — remove TTL
- [ ] `POST /api/redis/{cacheId}/keys/batch-delete` — batch delete
- [ ] `GET /api/redis/{cacheId}/export` — export keys as JSON
- [ ] `POST /api/redis/{cacheId}/pubsub/subscribe` — subscribe to channel
- [ ] `POST /api/redis/{cacheId}/pubsub/publish` — publish message
- [ ] `GET /api/redis/{cacheId}/keyspace-health` — keyspace health
- [ ] `GET /api/redis/{cacheId}/prefix-memory` — prefix memory breakdown

---

## 5. Storage Advanced Features

### Current State
- `StoragePage.tsx` (304 lines) — container list, blob browser with folder navigation, blob detail with properties + content

### MAUI Reference
- `BlobDetailPane.razor` (1,088 lines) — download with progress, copy URL, copy SAS URL, content/properties/versions tabs, metadata editor, version diff
- `StorageBlobList.razor` (697 lines) — breadcrumb, blob filter, multi-select, bulk download as ZIP, download progress
- `BlobMetadataEditor.razor` — metadata key-value editor
- `BlobUploadDialog.razor` — upload blobs
- `BlobCopyDialog.razor` — copy blob
- `BlobRecoveryPanel.razor` — recover soft-deleted blob
- `BlobVersionDiffPane.razor` — version diff viewer
- `StorageMutationBanner.razor` — mutation permission banner

### Tasks

#### 5.1 Blob Detail Enhancements
- [ ] Add download button with progress indicator
- [ ] Add "Copy URL" button
- [ ] Add "Copy SAS URL" button (generate SAS token via sidecar)
- [ ] Add "Versions" tab — list blob versions, view version content, restore version
- [ ] Add "Metadata" tab — metadata key-value editor with add/edit/delete
- [ ] Add content syntax highlighting (JSON, XML, CSV, images)
- [ ] Add sidecar endpoint: `GET /api/storage/{accountId}/containers/{container}/blobs/{blob}/versions`
- [ ] Add sidecar endpoint: `POST /api/storage/{accountId}/containers/{container}/blobs/{blob}/metadata`
- [ ] Add sidecar endpoint: `POST /api/storage/{accountId}/containers/{container}/blobs/{blob}/download-url`

#### 5.2 Blob List Enhancements
- [ ] Add blob filter input (client-side filter by name)
- [ ] Add multi-select mode with checkboxes
- [ ] Add bulk download as ZIP
- [ ] Add download progress card
- [ ] Add upload button (Tauri file picker)
- [ ] Add blob copy dialog
- [ ] Add sidecar endpoint: `POST /api/storage/{accountId}/containers/{container}/blobs/upload`
- [ ] Add sidecar endpoint: `POST /api/storage/{accountId}/containers/{container}/blobs/copy`

#### 5.3 Blob Recovery
- [ ] Create `BlobRecoveryPanel.tsx` — recover soft-deleted blobs
- [ ] Add sidecar endpoint: `GET /api/storage/{accountId}/containers/{container}/deleted-blobs`
- [ ] Add sidecar endpoint: `POST /api/storage/{accountId}/containers/{container}/blobs/{blob}/undelete`

---

## 6. Missing Pages

### 6.1 Pipelines Page (DevOps)
- [ ] Create `PipelinesPage.tsx` — project picker, pipeline tree, pipeline detail, pipeline activity
- [ ] Create `PipelineDetail.tsx` — pipeline runs, stage details, logs
- [ ] Create `PipelineActivity.tsx` — recent activity across all pipelines
- [ ] Create `ApprovalCenter.tsx` — pending approval requests
- [ ] Create `PipelineGroupEditor.tsx` — pipeline groups
- [ ] Add sidecar endpoints for DevOps operations (using existing `SwebKit.DevOps` project)
- [ ] Add route: `/pipelines`

### 6.2 Observability Page
- [ ] Create `ObservabilityPage.tsx` — overview, logs, failures, performance, availability tabs
- [ ] Create `ObservabilityOverview.tsx` — summary cards (requests, failure rate, P50/P95, exceptions, availability)
- [ ] Create `ObservabilityLogs.tsx` — KQL query editor (guided + advanced mode), results table
- [ ] Create `ObservabilityFailures.tsx` — failure analysis with exception grouping
- [ ] Create `ObservabilityPerformance.tsx` — performance metrics with charts
- [ ] Create `ObservabilityAvailability.tsx` — availability trends
- [ ] Create `TimeRangePicker.tsx` — time range selector
- [ ] Create `ResourceSelectorDialog.tsx` — Application Insights resource selector
- [ ] Add sidecar endpoints for observability (using existing `SwebKit.Observability` project)
- [ ] Add route: `/observability`

### 6.3 Monitoring Page
- [ ] Create `MonitoringPage.tsx` — alert rule management, alert history
- [ ] Create `AlertRuleEditor.tsx` — alert rule create/edit
- [ ] Create `AlertHistoryPanel.tsx` — recent alert history
- [ ] Add sidecar endpoints for alert rules
- [ ] Add route: `/monitoring`

### 6.4 Incident Timeline Page
- [ ] Create `IncidentTimelinePage.tsx` — incident timeline with scope toolbar, timeline list, detail panel
- [ ] Create `IncidentScopeToolbar.tsx` — scope selection (time range, resources)
- [ ] Create `IncidentTimelineList.tsx` — timeline events list
- [ ] Create `IncidentTimelineDetailPanel.tsx` — event detail
- [ ] Create `IncidentSnapshotExportDialog.tsx` — export incident snapshot
- [ ] Add sidecar endpoints for incident timeline
- [ ] Add route: `/incident-timeline`

---

## 7. Layout & Shell Features

### Current State
- `AppLayout.tsx` — basic sidebar nav + outlet
- `DashboardPage.tsx` — service cards + demo toggle

### MAUI Reference
- `MainLayout.razor` (26,767 bytes) — full shell with top bar, nav toggle, command palette, notifications, status bar
- `TopBar.razor` (20,715 bytes) — context title, command palette button, demo toggle, notification bell, theme picker
- `StatusBar.razor` (8,823 bytes) — connection status, resource counts
- `CommandPalette.razor` (11,078 bytes) — Ctrl+K command palette with fuzzy search
- `NotificationToast.razor` + `NotificationHistory.razor` — toast notifications + history
- `LeftNav.razor` — collapsible nav with area icons

### Tasks

#### 7.1 Top Bar
- [ ] Add context title (shows current page name)
- [ ] Add command palette button (Ctrl+K)
- [ ] Add demo mode toggle with confirmation popover
- [ ] Add notification bell with badge count
- [ ] Add theme picker (light/dark + custom themes)
- [ ] Add nav collapse/expand toggle

#### 7.2 Command Palette
- [ ] Create `CommandPalette.tsx` — Ctrl+K palette with fuzzy search
- [ ] Commands: navigate to page, toggle demo mode, refresh data, open settings
- [ ] Keyboard navigation (arrow keys, enter, escape)

#### 7.3 Notifications
- [ ] Create `NotificationToast.tsx` — toast notifications (success, error, warning, info)
- [ ] Create `NotificationHistory.tsx` — notification history drawer
- [ ] Add notification context provider
- [ ] Wire notifications into all mutation success/error callbacks

#### 7.4 Status Bar
- [ ] Create `StatusBar.tsx` — bottom bar with connection status, resource counts
- [ ] Show sidecar connection status (connected/disconnected)
- [ ] Show demo mode indicator
- [ ] Show active resource counts (pods, queues, keys, etc.)

#### 7.5 Keyboard Shortcuts
- [ ] Create `KeyboardShortcutsPanel.tsx` — help panel showing all shortcuts
- [ ] Add global shortcuts: Ctrl+K (command palette), Ctrl+/ (shortcuts help)
- [ ] Add per-page shortcuts (e.g., 'r' to refresh, 'n' for new)

#### 7.6 Settings Page Expansion
- [ ] Add "DevOps" settings tab (ADO organization, PAT, project selection)
- [ ] Add "Observability" settings tab (Application Insights resource selection)
- [ ] Add "Incident Timeline" settings tab (mapping configuration)
- [ ] Add "API Client" settings tab (request tabs toggle, default auth)
- [ ] Add "Diagnostics" settings tab (log viewer, config health)
- [ ] Add "Appearance" settings tab (theme selection, font size)

#### 7.7 Dashboard Enhancement
- [ ] Add health tiles per service area
- [ ] Add watch tiles (configured deployments, queues, caches)
- [ ] Add metric tiles (pod count, queue depth, cache hit rate)
- [ ] Add configuration readiness dashboard
- [ ] Add pending approvals count
- [ ] Add recent pipeline failures

---

## 8. Native OS Integration (Tauri Bridges)

### Tasks

#### 8.1 File System Access
- [ ] Create Tauri commands for file/directory picker dialogs
- [ ] Create Tauri commands for reading/writing files (collection files, Bruno sync)
- [ ] Replace `window.prompt`/`window.confirm` with Tauri dialogs

#### 8.2 Git Operations
- [ ] Create Tauri commands wrapping git CLI or LibGit2Sharp
- [ ] Branch listing, commit, push, pull, diff, revert
- [ ] Status (changed files count)

#### 8.3 Port-Forward Management
- [ ] Create Tauri command wrapping kubectl port-forward
- [ ] Session management (start, stop, list active sessions)

#### 8.4 Clipboard Integration
- [ ] Use Tauri clipboard plugin for copy/paste operations

#### 8.5 Notification System
- [ ] Use Tauri notification plugin for OS-level notifications

---

## Acceptance Criteria

- [ ] All MAUI features have a React equivalent (or documented Tauri native equivalent)
- [ ] No `window.prompt` or `window.confirm` calls remain — all replaced with proper dialogs
- [ ] All sidecar endpoints have corresponding TypeScript types and React Query hooks
- [ ] All new pages have E2E tests
- [ ] All new components have `data-testid` attributes for testing
- [ ] Demo mode works for all features (demo data where real connections aren't configured)
- [ ] Keyboard shortcuts work consistently
- [ ] No console errors in production build

## Scope

### In Scope
- All 7 feature areas listed above
- Sidecar API endpoint additions
- Tauri native bridges for OS-level features
- New pages (Pipelines, Observability, Monitoring, Incident Timeline)
- Layout/shell improvements
- Settings page expansion

### Out of Scope
- MAUI app removal (keep both running during transition)
- Backend library changes (SwebKit.Core, SwebKit.Azure, etc. — use as-is)
- Database/persistence changes
- Authentication/authorization system
- CI/CD pipeline setup

## Constraints

- Must maintain backward compatibility with existing sidecar API endpoints
- Demo mode must work for all features (no real Azure/K8s connection required for testing)
- All E2E tests must pass in CI (Playwright with sidecar running)
- TypeScript strict mode must pass
- Production build must succeed with no errors
