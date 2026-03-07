# SwebKit — Design & Implementation Plan

**Context:** Greenfield .NET MAUI desktop "Swiss army knife" debugging tool for .NET developers
working daily with Azure Service Bus, Application Insights / OpenTelemetry, and AKS. The repository
(`d:\Projects\SwebKit`) is currently empty (git init only). This document is the complete
design blueprint — from domain model through implementation roadmap.

---

## Table of Contents

1. [Overall Architecture & Domain Model](#1-overall-architecture--domain-model)
2. [Solution Structure & Tech Stack](#2-solution-structure--tech-stack)
3. [Information Architecture & Navigation](#3-information-architecture--navigation)
4. [Layout & Pane System](#4-layout--pane-system)
5. [Service Bus Feature Design](#5-service-bus-feature-design)
6. [Observability Feature Design](#6-observability-feature-design)
7. [AKS Feature Design](#7-aks-feature-design)
8. [Cross-Cutting UX Decisions](#8-cross-cutting-ux-decisions)
9. [Implementation Roadmap](#9-implementation-roadmap)
10. [Risks & Trade-offs](#10-risks--trade-offs)

---

## 1. Overall Architecture & Domain Model

### 1.1 Core Concept

Everything hangs off **Project + Environment**. A `Project` is a logical grouping (e.g.,
"OrderPlatform"). Each project has one or more `ProjectEnvironment` instances (Dev, Test, Acc,
Prod). Each environment carries independent configuration for each feature pillar. Switching
environment in the top bar reconfigures all open tool panes simultaneously.

### 1.2 Domain Objects

```
Project
  Id: Guid
  Name: string                     // "OrderPlatform"
  Description: string?
  IconColor: string                // hex color for project avatar
  CreatedAt: DateTimeOffset
  Environments: List<ProjectEnvironment>

ProjectEnvironment
  Id: Guid
  ProjectId: Guid
  Name: string                     // "Dev" | "Test" | "Acc" | "Prod"
  Tier: EnvironmentTier            // enum: NonProd | Production
  ServiceBusConfig: ServiceBusConfig?
  ObservabilityConfig: ObservabilityConfig?
  AksConfig: AksConfig?
  FavoriteEntities: List<FavoriteEntity>     // SB queues/topics pinned
  SavedQueries: List<SavedQuery>
  LastUsedFilters: Dictionary<string, FilterState>

ServiceBusConfig
  NamespaceHostname: string        // "myns.servicebus.windows.net"
  AuthMode: SbAuthMode             // enum: ManagedIdentity | ConnectionString | ServicePrincipal
  CredentialRef: string?           // key name in credential store (never plain text)
  FavoriteQueues: List<string>
  FavoriteTopics: List<string>

ObservabilityConfig
  Provider: ObservabilityProviderType   // enum: AppInsights | OtlpEndpoint
  // AppInsights branch:
  WorkspaceId: string?             // Log Analytics workspace GUID
  ApplicationId: string?           // App Insights application GUID
  CredentialMode: string           // "DefaultAzureCredential" | "ApiKey" | "ServicePrincipal"
  CredentialRef: string?
  // OTLP branch:
  OtlpEndpoint: string?            // "https://..."
  OtlpHeaders: Dictionary<string, string>?
  ResourceAttributes: Dictionary<string, string>?  // service.name, deployment.environment

AksConfig
  KubeconfigContext: string?       // context name from ~/.kube/config
  ExplicitClusterUrl: string?      // alternative to kubeconfig
  CredentialRef: string?
  DefaultNamespace: string         // "order-platform"
  WatchedDeployments: List<string> // deployments to show on overview

SavedQuery
  Id: Guid
  Name: string                     // "Errors last 15m"
  Area: QueryArea                  // enum: Logs | Traces | Metrics
  QueryText: string
  DefaultTimeRange: TimeRange

FavoriteEntity
  EntityType: EntityType           // Queue | Topic | Subscription | Deployment
  Name: string
  ParentName: string?              // topic name for subscriptions
```

### 1.3 Configuration Storage

**Location:** `%APPDATA%\SwebKit\` (Windows), cross-platform: `Environment.SpecialFolder.ApplicationData`

**Files:**
- `profiles.json` — all Projects and ProjectEnvironments (no secrets)
- `ui-state.json` — window layout, last selected project+env, open tabs
- `user-settings.json` — theme, keyboard overrides, global preferences

**Secrets:** NEVER stored in JSON files. All credential refs are logical keys looked up in:
- **Windows:** Windows Credential Manager via `Windows.Security.Credentials.PasswordVault`
- **Cross-platform fallback:** DPAPI-encrypted blob in `%APPDATA%\SwebKit\vault\`
- Abstracted behind `ICredentialStore` with methods `Save(key, secret)`, `Get(key)`, `Delete(key)`
- Connection strings decoded at runtime, never held longer than needed, cleared from memory after use

**Serialization:** `System.Text.Json` with source generators for performance. Custom converter for
`Guid`, `DateTimeOffset`. Enum serialization as strings for readability.

### 1.4 Key Abstractions / Interfaces

#### IObservabilityProvider
```
IObservabilityProvider
  ProviderType: ObservabilityProviderType
  IsConnected: bool
  Task<IReadOnlyList<LogEntry>> QueryLogsAsync(LogQuery query, CancellationToken ct)
  Task<TraceTimeline> GetTraceAsync(string operationId, CancellationToken ct)
  Task<IReadOnlyList<MetricSeries>> GetMetricsAsync(MetricsQuery query, CancellationToken ct)
  Task<bool> TestConnectionAsync(CancellationToken ct)

LogQuery
  TimeRange: TimeRange
  Levels: List<LogLevel>?
  TextSearch: string?
  CorrelationId: string?
  PropertyFilters: List<PropertyFilter>
  MaxRows: int               // default 500
  RawKql: string?            // advanced mode

LogEntry
  Timestamp: DateTimeOffset
  Level: LogLevel
  Message: string
  OperationName: string?
  OperationId: string?
  CorrelationId: string?
  Properties: Dictionary<string, object>
  SourceProvider: ObservabilityProviderType

TraceTimeline
  RootOperationId: string
  Spans: List<TraceSpan>     // sorted by start time

TraceSpan
  SpanId: string
  ParentSpanId: string?
  Name: string
  Kind: SpanKind             // Client | Server | Producer | Consumer | Internal
  StartTime: DateTimeOffset
  Duration: TimeSpan
  Status: SpanStatus         // Ok | Error | Unset
  Tags: Dictionary<string, string>
  Events: List<SpanEvent>

MetricsQuery
  MetricName: string
  TimeRange: TimeRange
  Granularity: TimeSpan
  Filters: Dictionary<string, string>
```

**Implementations:**
- `AppInsightsObservabilityProvider` — uses `Azure.Monitor.Query` (`LogsQueryClient`, `MetricsQueryClient`)
- `OtlpObservabilityProvider` — pulls from OTLP-compatible endpoint or Azure Monitor via OTLP

#### IServiceBusClient
```
IServiceBusClient
  Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct)
  Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct)
  Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct)
  Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct)
  Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct)
  Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct)
  Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct)
  Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct)
  Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> lockTokens, string? targetEntity, CancellationToken ct)
  Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> lockTokens, CancellationToken ct)
  Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct)
  Task<bool> TestConnectionAsync(CancellationToken ct)

SbMessage
  MessageId: string
  CorrelationId: string?
  Subject: string?
  ContentType: string?
  Body: BinaryData
  ApplicationProperties: IDictionary<string, object>
  SystemProperties: SbSystemProperties
  DeadLetterReason: string?
  DeadLetterErrorDescription: string?
  EnqueuedAt: DateTimeOffset
  DeliveryCount: int
  LockToken: string?

SbEntityStats
  ActiveMessageCount: long
  DeadLetterMessageCount: long
  ScheduledMessageCount: long
  TransferCount: long
```

**Implementation:** `AzureServiceBusClient` wraps `Azure.Messaging.ServiceBus` SDK.
Uses `ServiceBusClient` + `ServiceBusReceiver` in Peek mode by default.

#### IAksClient
```
IAksClient
  Task<IReadOnlyList<V1Deployment>> GetDeploymentsAsync(string ns, CancellationToken ct)
  Task<IReadOnlyList<V1Pod>> GetPodsAsync(string ns, string? labelSelector, CancellationToken ct)
  Task<IReadOnlyList<Corev1Event>> GetEventsAsync(string ns, string? involvedObjectName, CancellationToken ct)
  IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, CancellationToken ct)
  Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct)
  Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct)
  Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct)
  Task<bool> TestConnectionAsync(CancellationToken ct)

LogStreamOptions
  TailLines: int?           // null = follow from now
  Follow: bool              // live tail
  SinceSeconds: int?
  TextFilter: string?       // client-side filter

PortForwardSession
  SessionId: Guid
  LocalPort: int
  RemotePort: int
  ResourceName: string
  Namespace: string
  StartedAt: DateTimeOffset
  IsActive: bool
```

**Implementation:** `KubernetesAksClient` wraps `KubernetesClient` NuGet package.
Kubeconfig loaded from `~/.kube/config` (default) or explicit path.

---

## 2. Solution Structure & Tech Stack

### 2.1 Blazor Hybrid Approach

The app uses **.NET MAUI Blazor Hybrid**. The MAUI layer provides a native desktop window and
platform services (credential store, file system, shell launching). The entire UI is rendered
inside a `BlazorWebView` control hosted in `MainPage.xaml`. All UI components are Razor components
(`.razor` files). There are no XAML UI views — XAML is only used for the thin native host
(`MainPage.xaml`) and the App shell that wraps the `BlazorWebView`.

**Why Blazor Hybrid:**
- Full access to web UI ecosystem: component libraries, CSS layouts, Monaco Editor, xterm.js
- No MAUI XAML data grid performance limitations — use Radzen/MudBlazor grids with row virtualization
- CSS for dense, developer-tool-style layouts (VS Code aesthetic is achievable)
- Monaco Editor via JSInterop for KQL, JSON, and message body editing
- xterm.js for embedded terminal (pod shell) via JSInterop
- Easier cross-platform future: the Blazor UI can also target web with minimal changes

**Navigation:** Blazor `Router` + `NavigationManager` handles all in-app navigation. `@page`
directives define routes. No MAUI Shell navigation used for in-app routing.

### 2.2 Solution Layout

```
SwebKit.sln
├── src/
│   ├── SwebKit.App/                    # .NET MAUI Blazor Hybrid app project
│   │   ├── MauiProgram.cs              # MAUI + Blazor service registration
│   │   ├── MainPage.xaml               # Hosts <BlazorWebView> (thin XAML shell only)
│   │   ├── wwwroot/                    # Static web assets
│   │   │   ├── index.html              # Blazor entry point
│   │   │   ├── css/                    # Global styles, theme variables
│   │   │   └── js/                     # Monaco editor loader, xterm.js init
│   │   ├── Components/                 # All Razor UI components
│   │   │   ├── App.razor               # Root component with Router
│   │   │   ├── Layout/                 # Shell layout components
│   │   │   │   ├── MainLayout.razor    # Top bar + left nav + content area
│   │   │   │   ├── TopBar.razor        # Project+env selector, command palette btn
│   │   │   │   ├── LeftNav.razor       # Collapsible side navigation
│   │   │   │   └── StatusBar.razor     # Connection status, background tasks
│   │   │   ├── Pages/                  # Routable page components
│   │   │   │   ├── ProjectsPage.razor
│   │   │   │   ├── ServiceBusPage.razor
│   │   │   │   ├── ObservabilityPage.razor
│   │   │   │   ├── AksPage.razor
│   │   │   │   └── SettingsPage.razor
│   │   │   ├── ServiceBus/             # SB-specific components
│   │   │   │   ├── MessageListView.razor
│   │   │   │   ├── MessageDetailPane.razor
│   │   │   │   ├── DlqView.razor
│   │   │   │   ├── MessageComposer.razor
│   │   │   │   └── EntityTree.razor
│   │   │   ├── Observability/          # Observability-specific components
│   │   │   │   ├── LogTableView.razor
│   │   │   │   ├── TraceTimeline.razor
│   │   │   │   ├── MetricsDashboard.razor
│   │   │   │   └── KqlEditor.razor     # Monaco Editor wrapper
│   │   │   ├── Aks/                    # AKS-specific components
│   │   │   │   ├── WorkloadOverview.razor
│   │   │   │   ├── PodLogView.razor
│   │   │   │   ├── EventsPanel.razor
│   │   │   │   └── TerminalView.razor  # xterm.js wrapper
│   │   │   └── Shared/                 # Reusable generic components
│   │   │       ├── FilterBar.razor
│   │   │       ├── DataTable.razor     # Wraps Radzen/MudBlazor grid
│   │   │       ├── DetailsPane.razor
│   │   │       ├── CommandPalette.razor
│   │   │       ├── TabPanel.razor
│   │   │       ├── LoadingSpinner.razor
│   │   │       └── ErrorCallout.razor
│   │   ├── Services/                   # App-layer services (tab state, task queue, etc.)
│   │   └── Platforms/Windows/          # Windows-specific: WindowsCredentialStore.cs
│   ├── SwebKit.Core/                   # Domain models, interfaces, shared logic
│   │   ├── Domain/                     # All domain objects defined above
│   │   ├── Abstractions/               # IObservabilityProvider, IServiceBusClient, IAksClient
│   │   ├── Configuration/              # Config loading/saving, ICredentialStore
│   │   └── Extensions/
│   ├── SwebKit.Azure/                  # Azure integrations
│   │   ├── ServiceBus/                 # AzureServiceBusClient
│   │   ├── Observability/              # AppInsightsObservabilityProvider
│   │   └── Identity/                  # Azure credential helpers
│   ├── SwebKit.Kubernetes/             # Kubernetes integrations
│   │   └── AksClient/                  # KubernetesAksClient
│   └── SwebKit.OpenTelemetry/          # OTLP observability provider
│       └── OtlpObservabilityProvider.cs
└── tests/
    ├── SwebKit.Core.Tests/
    ├── SwebKit.Azure.Tests/
    └── SwebKit.Kubernetes.Tests/
```

### 2.3 NuGet Packages

**MAUI & Blazor Host:**

| Package | Purpose |
|---|---|
| `Microsoft.Maui.Controls` | MAUI native host framework |
| `Microsoft.AspNetCore.Components.WebView.Maui` | BlazorWebView for MAUI |
| `Microsoft.Extensions.DependencyInjection` | DI container |
| `Microsoft.Extensions.Hosting` | App host for DI + config |

**Blazor UI Component Library:**

| Package | Purpose |
|---|---|
| `Microsoft.FluentUI.AspNetCore.Components` | Microsoft Fluent UI Blazor — DataGrid (virtualized), NavMenu, TabPanel, Dialog, Toast, TreeView, Progress, Splitter, etc. |
| `Blazor-ApexCharts` (`ApexCharts.Blazor`) | Charts for metrics dashboard (Fluent UI has no chart component) |

> **Rationale:** Fluent UI Blazor is Microsoft's official design system for Blazor, meaning it
> aligns with VS Code / Azure Portal aesthetics that .NET developers recognize. `FluentDataGrid`
> supports row virtualization and is the right fit for dense message/log tables. For charts, the
> Fluent UI package has no chart component, so `Blazor-ApexCharts` is added solely for the
> metrics dashboard tiles (Section 6.4).

**KQL / JSON Editor:**

| Package | Purpose |
|---|---|
| `BlazorMonaco` (or custom JSInterop) | Monaco Editor in Blazor — for KQL queries, JSON body editing |

**Terminal (AKS shell):**

| Approach | Notes |
|---|---|
| xterm.js via JSInterop | Load xterm.js from wwwroot, wrap in `TerminalView.razor` with `IJSRuntime` |

**Azure SDKs:**

| Package | Purpose |
|---|---|
| `Azure.Messaging.ServiceBus` | Service Bus client SDK |
| `Azure.Monitor.Query` | Log Analytics + App Insights KQL queries |
| `Azure.Identity` | `DefaultAzureCredential`, service principal auth |

**Kubernetes:**

| Package | Purpose |
|---|---|
| `KubernetesClient` | kubectl-equivalent for pods, logs, port-forward |

**Other:**

| Package | Purpose |
|---|---|
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP client for OTLP provider |
| `Serilog` | Internal app logging |
| `System.Text.Json` | Config serialization (source generators for perf) |

### 2.4 Architecture Pattern

- **Blazor component model** throughout: Razor components with `@inject` for services; no MVVM
  ViewModels (state lives in injected services or component `@code` blocks using
  `INotifyPropertyChanged` via `StateHasChanged()`)
- **Cascading parameters** for current Project + Environment: `CascadingValue<AppContext>` wraps
  the entire layout so any component can access the current project/env without prop drilling
- **Dependency Injection** via `MauiProgram.cs` registering all services as `Scoped` or `Singleton`
- **Blazor Router** (`App.razor` + `NavigationManager`) for all in-app navigation
- **Async-first**: all service calls `async/await` with `CancellationToken`; components call
  `StateHasChanged()` after async updates; long-running streams use `IAsyncEnumerable`
- **Event bus** for cross-component messaging: lightweight `IAppEventBus` service using
  `Action<T>` subscriptions for events like `EnvironmentChanged`, `TabOpened`
- **JSInterop** for Monaco Editor and xterm.js; JSInterop calls isolated to wrapper components
  (`KqlEditor.razor`, `TerminalView.razor`) so the rest of the app never touches JS directly

---

## 3. Information Architecture & Navigation

### 3.1 Top-Level Navigation

**Shell structure:** Left-side nav rendered as a Blazor component (`LeftNav.razor`), collapsible,
icon + label, 240px expanded / 60px collapsed. State stored in `LayoutStateService` (DI Singleton).
Navigation triggered via `NavigationManager.NavigateTo("/service-bus")` etc.

```
[Project Avatar] OrderPlatform        <- collapsible header, shows project name
--------------------------------------------
[#] Projects                          Ctrl+1
[=] Service Bus                       Ctrl+2
[~] Observability                     Ctrl+3
[K] AKS                               Ctrl+4
--------------------------------------------
[*] Favorites                         (quick-access pinned entities)
--------------------------------------------
[?] Settings                          Ctrl+,
```

### 3.2 Top Bar (Always Visible)

```
[SwebKit logo]  [Project: OrderPlatform v]  [Env: DEV v]  ...  [>_ Command Palette Ctrl+P]  [Bell]  [Tasks spinner]
```

- **Project selector:** Custom Blazor dropdown (`<ProjectSelector>`) showing all projects with
  avatar color. Keyboard: `Alt+Shift+P` opens selector
- **Environment selector:** Segmented button or `Picker` showing Dev / Test / Acc / Prod.
  Background color changes the entire top bar:
  - Dev: neutral (dark gray / app default)
  - Test: blue tint
  - Acc: orange tint `#E8720C`
  - Prod: red `#C8002A` with ⚠ PROD warning badge
- **Keyboard:** `Alt+1` = Dev, `Alt+2` = Test, `Alt+3` = Acc, `Alt+4` = Prod

### 3.3 Environment Switching Behavior

When the user changes environment:
1. `AppStateService` (Singleton) updates `CurrentEnvironment` and fires `EnvironmentChanged` event
2. `CascadingValue<AppContext>` propagates the change; all subscribed components call `StateHasChanged()`
   and re-invoke their data loading logic
3. Active background tasks (log tail, port-forwards) are cancelled; user sees a toast
   "Switched to Prod — previous tasks stopped"
4. Prod → banner permanently shown at top of each pane: `"⚠ PRODUCTION — changes are real"`
5. Last used environment per project is remembered in `ui-state.json`

### 3.4 Command Palette

**Trigger:** `Ctrl+P`
**UI:** Floating overlay with text input, fuzzy-filtered list of commands (max 8 visible, scrollable)

**Command categories:**
```
Project & Navigation
  > Switch project: [fuzzy match project names]
  > Switch to Dev / Test / Acc / Prod
  > Go to Service Bus
  > Go to Observability
  > Go to AKS

Service Bus
  > Open queue: [fuzzy match queue names]
  > Open DLQ: [queue name] (DLQ)
  > Open topic/subscription: [name]
  > Send message to: [entity name]

Observability
  > Run saved query: [query name]
  > Find logs for correlation ID...      (opens input)
  > Find trace for operation ID...
  > Open metrics dashboard

AKS
  > Tail logs: [deployment name]
  > Describe pod: [pod name]
  > Port-forward: [service name]
  > Open shell in: [pod name]
  > Refresh workload overview
```

**Integration:** Commands invoke navigation or ViewModel methods; palette closes on selection.

---

## 4. Layout & Pane System

### 4.1 Main Screen Layout

```
+------------------------------------------------------------------+
|  TOP BAR: [logo] [Project] [Env] ............... [Cmd] [Bell]   |
+------------------------------------------------------------------+
|        |                                             |           |
| LEFT   |   CENTRAL TABBED WORK AREA                 |  DETAILS  |
| NAV    |   [Tab1: OrderQueue] [Tab2: DLQ] [+]        |  PANE     |
| (240px)|                                             | (300px,   |
|        |   [current pane content]                    |  toggle)  |
|        |                                             |           |
|        |                                             |           |
+------------------------------------------------------------------+
|  STATUS BAR: [Connected: SB OK  AI OK  AKS OK]  [Tasks: 2 ...]  |
+------------------------------------------------------------------+
```

- **Central work area:** Tabbed (like VS Code tabs). Tabs can be: Service Bus entity view,
  Observability view, AKS view, or any combination
- **Details pane:** Right-hand, toggled with `Ctrl+\`. Shows selected item details (message body,
  pod spec, log entry properties). Collapses fully to give more space to main area
- **Status bar:** Shows connection status for each provider (green dot = connected, red = error).
  Click on status indicator → opens connection details popup. Background task count with spinner.

### 4.2 Tab System

- Each tab: closeable (X), reorderable (drag), pinnable (right-click → Pin)
- Max recommended 8 tabs before overflow scrolling
- Tabs persist per Project+Environment in `ui-state.json` (tab type + entity + filter state)
- `Ctrl+Tab` = next tab, `Ctrl+Shift+Tab` = previous tab, `Ctrl+W` = close tab
- Right-click tab: Close, Close Others, Close to the Right, Pin, Rename

### 4.3 Concrete Example Layouts

#### Example A — Debugging a Service Bus DLQ Issue

```
Tabs: [OrderQueue (12) | OrderQueue DLQ (47) | Order.Created DLQ (5)]

LEFT PANE SELECTED: "OrderQueue DLQ"

Top: Filter bar — [Last 1h v] [All levels] [CorrelationId: ___] [Search body: ___] [Refresh: 30s v]
                  Banner: "⚠ PRODUCTION — changes are real"

Main split:
  LEFT (55%): DLQ message list
    Columns: [EnqueuedAt] [MessageId] [CorrelationId] [DeadLetterReason] [DeliveryCount] [Size]
    Row selected (highlighted)

  RIGHT (45%): Selected message details
    Tabs: [Body] [Properties] [System Properties] [DLQ Info]
    Body tab: JSON syntax-highlighted viewer with copy button
    DLQ Info tab: DeadLetterReason, DeadLetterErrorDescription in red callout box

Bottom action bar:
  [Resubmit to OrderQueue] [Move to...] [Complete (Delete)] [Abandon]
  [Resubmit Selected (3)] [Complete Selected (3)]
  [Find logs for this correlation ID →]   <- cross-links to Observability
```

#### Example B — Debugging a Failing AKS Pod

```
Tabs: [Workload Overview | order-api logs | Observability: Errors]

WORKLOAD OVERVIEW TAB:
  Top: Namespace: order-platform  [Refresh F5]

  Main split:
    LEFT (60%): Deployments table
      [order-api] [3/3] [Restarts: 12] [1.4.2] [Running] <- row highlighted red (high restarts)
      [inventory-api] [2/2] [Restarts: 0] [1.2.0] [Healthy]
      Expanded row shows pods:
        order-api-7d9f4-abc  Running  0  Node: aks-pool-1
        order-api-7d9f4-def  CrashLoopBackOff  5  <- red badge

    RIGHT (40%): Events panel
      Warning  BackOff  Restarting failed container  order-api-7d9f4-def  2m ago
      Warning  OOMKilled  Container killed  order-api-7d9f4-def  5m ago

  Actions on selected pod: [View Logs] [Describe] [Open Shell] [Delete Pod]

ORDER-API LOGS TAB (opened by "View Logs" action):
  Top: [Deployment: order-api v] [Pod: order-api-7d9f4-abc v] [Live tail: ON] [Filter: ___]
  Log lines streaming, error lines in red
  "Pod restarted — switch to new pod?" banner

OBSERVABILITY: ERRORS TAB:
  Pre-filtered: Level=Error, last 30m, service.name=order-api
  Shows exception timeline matching restart times
```

---

## 5. Service Bus Feature Design

### 5.1 Main SB Navigation

Left nav → Service Bus → expands tree:
```
SERVICE BUS
  Namespace: myns.servicebus.windows.net
  + Queues
      order-queue         (Active: 4 | DLQ: 47)
      inventory-queue     (Active: 0 | DLQ: 0)
  + Topics
      order-events
        > order-created-sub  (Active: 2 | DLQ: 5)
        > order-failed-sub   (Active: 0 | DLQ: 0)
  [Refresh]
```
- Right-click entity → Open, Open DLQ, Send Message, Refresh Stats
- DLQ count shown in red when > 0

### 5.2 Queue / Subscription Inspect View

**Layout:** Toolbar + list (left 55%) + details (right 45%)

**Toolbar:**
```
[Peek mode v] [Count: 50 v] [Last 1h v] [CorrelationId: ___] [Subject: ___] [Search body: ___]
[Refresh] [Auto-refresh: Off v]
```

**Message list columns:** EnqueuedAt | MessageId (first 8 chars) | CorrelationId | Subject |
ContentType | DeliveryCount | Size

**Actions (per row or toolbar):**
- `Enter` or click → show details in right panel
- Right-click: Copy MessageId, Copy Body, Open in new tab, View Raw
- `Ctrl+C` on selected row → copies MessageId

**Details pane tabs:**
- **Body:** Formatted JSON or XML (auto-detected) with syntax highlighting. Raw toggle.
  Copy button.
- **Properties:** Application properties as key-value table (editable in compose mode)
- **System Properties:** EnqueuedAt, DeliveryCount, ExpiresAt, LockToken, etc.

**Filtering behavior:**
- All filters are AND-combined
- Filters persist per entity per Project+Environment
- Time range: relative (Last 15m/1h/6h/24h) or absolute (date picker)
- Body search is client-side substring match on peeked messages (with note on limitations)
- "Advanced filter" toggle exposes full SQL filter expression for server-side filtering

### 5.3 Dead-Letter Queue (DLQ) Workflow

**Dedicated DLQ mode** — visually distinct from normal queue view:
- Banner: "Dead-Letter Queue — [entity name]"
- `DeadLetterReason` and `DeadLetterErrorDescription` shown as prominent columns in red
- Default sort: EnqueuedAt descending (most recent DLQ entries first)

**Single message actions (right panel bottom):**
```
[Resubmit to original queue]  [Move to...]  [Complete (delete)]  [Abandon]
```

**Batch actions (when 2+ selected via checkbox):**
```
[Resubmit Selected (N)]  [Complete Selected (N)]  [Move Selected (N) to...]
```

**Safety nets:**
- Prod environment: ALL destructive/mutative actions require confirmation dialog showing:
  - Action name + entity path
  - Number of affected messages
  - "⚠ This is PRODUCTION" warning
  - Type "CONFIRM" to proceed (for bulk operations > 10 messages)
- Resubmit shows: target entity, message count, "This will re-enqueue messages"

**Auto-refresh:** Configurable interval (Off / 10s / 30s / 60s). Indicator shows "Last refreshed: 5s ago".

### 5.4 Send / Compose / Replay

**Accessed via:** Entity right-click → "Send Message", or Command Palette → "Send message to..."

**Composer view:**
```
SEND MESSAGE TO: order-queue (DEV)
  Body editor (Monaco Editor via BlazorMonaco — JSON/XML syntax highlighting, line numbers,
               language auto-detection, format-on-save)
  Properties:
    ContentType:  [application/json    ]
    CorrelationId:[                    ]
    Subject:      [                    ]
    SessionId:    [                    ]
    MessageId:    [auto-generate v]
  Custom Properties:
    [+ Add Property]  Key: [___]  Value: [___]  Type: [String v]

  [Save as Template...]  [Send]  [Send & Close]
```

**Template system:**
- Templates stored per Project+Environment in `profiles.json`
- "Save as Template" dialog: enter name, optionally strip sensitive properties
- Template picker: flyout grid of saved templates, click to load into composer
- Templates also appear in Command Palette: "Send template: [name]"

**Scenario system:**
- Scenario = ordered list of `{entity, templateName, delayMs}` entries
- Scenario editor: drag-to-reorder steps, set delays, target environment
- "Run Scenario" button: executes steps sequentially with progress indicator
- Scenarios saved per Project, applicable across environments

### 5.5 SB Presets and Favorites

- Right-click entity in tree → "Add to Favorites" → pinned under Favorites nav section
- Favorites show real-time message counts (polled every 30s)
- Last-used filter state (time range, filters) remembered per entity per env
- Stats widget on entity list: Active count, DLQ count, updated on demand or auto-refresh

---

## 6. Observability Feature Design

### 6.1 Configuration Model

**Per ProjectEnvironment, `ObservabilityConfig`:**

AppInsights/Azure Monitor:
- WorkspaceId (Log Analytics workspace GUID)
- ApplicationId (App Insights resource GUID)
- CredentialMode: `DefaultAzureCredential` (recommended) | `ApiKey` | `ServicePrincipal`
- API Key stored in credential store; SP client secret stored in credential store

OTLP:
- OtlpEndpoint: e.g. `https://ingest.example.com/v1`
- Headers: `{"Authorization": "Bearer token123"}` (values in credential store)
- ResourceAttributes: `{"service.name": "order-api", "deployment.environment": "dev"}`

**Config wizard (in Settings → Environments → [env] → Observability):**
1. Choose provider type: AppInsights | OTLP
2. For AppInsights: paste WorkspaceId + ApplicationId, choose auth mode, enter credentials
3. "Test Connection" button → validates with a lightweight query
4. Save

### 6.2 Log Table View

**Layout:** Filter toolbar at top, results table filling main area, row click → details pane

**Filter toolbar:**
```
[Last 15m v] [Error, Warning, Info v] [Search message: ___] [CorrelationId: ___]
[Operation: ___] [+ Add filter] [Saved queries v] [Run Ctrl+Enter] [Cancel]
```

**Table columns (configurable, defaults):**
Timestamp | Level (colored badge) | Message (truncated) | OperationName | CorrelationId | Properties count

**Row details (right pane):**
- Full message text
- All custom properties as expandable key-value table
- Quick action: "Find trace for this operation" → opens Trace view

**Level color coding:**
- Critical/Error: red row background
- Warning: yellow tint
- Info: default
- Debug/Trace: muted gray

**Saved Queries:**
- Dropdown in filter bar: built-in presets + user-saved
- Built-in: "Errors last 15m" | "Slow requests (>2s)" | "Exceptions by type" | "Dependency failures"
- User saves current filter state as named query (stored in ProjectEnvironment)
- Run as KQL: `Ctrl+Enter`; running queries show spinner + elapsed time; cancel button active

**Export:**
- "Copy selected rows as JSON" | "Export to CSV" | "Copy KQL query"

**Async behavior:**
- Queries run on background thread; `IsBusy` shows spinner
- Results streamed: first page shown quickly, "Load more" at bottom
- Auth failures → clear error message: "Authentication failed. Check your credentials in Settings → [env]"
- Query parse errors → KQL error highlighted with red border + message

### 6.3 Trace / Correlation View

**Entry points:**
- Log table row right-click → "Find trace for this operation ID"
- DLQ message details → "Find logs for this correlation ID"
- Command Palette → "Find trace for operation ID..."
- Direct input at top of Trace view

**Display:**
```
TRACE: op-id-abc123  [2026-03-07 14:23:01]  Duration: 824ms

  Waterfall timeline (horizontal, time-aligned):
  ├─ [POST /orders]                       0ms ─────────────────── 824ms  [Server]
  │   ├─ [SQL: INSERT orders]            12ms ────── 45ms         [Client]
  │   ├─ [ServiceBus: Publish order.created] 80ms ─── 95ms       [Producer]
  │   └─ [HTTP: inventory-api/check]     100ms ──────────── 620ms [Client] ⚠ SLOW
  │       └─ Exception: TimeoutException at 615ms
```

- Click span → right panel shows all span attributes, tags, events
- Timeline zoom: scroll to zoom in on dense spans
- Provider-agnostic rendering: `TraceTimeline` domain model hides AppInsights vs OTEL differences
- Exceptions shown with red icon inline; click → full stack trace

### 6.4 Mini Metrics Dashboard

**Layout:** Grid of metric tiles, configurable, per Project+Environment

**Default tiles:**
- p95 Request Latency (line chart sparkline)
- Error Rate % (gauge or line)
- Request Count (bar chart, per time bucket)
- Dependency Failure Rate (line chart)
- Service Bus Queue Backlog (bar: active + DLQ counts per queue)

**Controls:**
- Time range: [15m] [1h] [6h] [24h] [Custom]
- Auto-refresh: [Off] [1m] [5m]
- Add/remove tiles, reorder via drag

**Chart library:** `Blazor-ApexCharts` (`ApexCharts.Blazor` NuGet package). Used only in
`MetricsDashboard.razor` for sparklines and metric tiles. Fluent UI Blazor does not include
charting, so this is the dedicated chart dependency.

### 6.5 Cross-Linking Workflows

Key cross-area jumps (always available via right-click or action buttons):
- DLQ message → "Find logs for CorrelationId: [id]" → Observability log view, pre-filtered
- Log entry → "Find trace for OperationId: [id]" → Trace view
- AKS pod logs → "Find App Insights logs for pod" → Observability filtered by pod name attribute

---

## 7. AKS Feature Design

### 7.1 Configuration

**Per ProjectEnvironment `AksConfig`:**
- Kubeconfig context: picker shows all contexts from `~/.kube/config` (discovered at startup)
- Default namespace: string (e.g. "order-platform")
- WatchedDeployments: list of deployment names to show prominently on overview

**Settings wizard:**
1. "Discover from kubeconfig" → lists available contexts → user picks one
2. Or: explicit API server URL + credentials (stored in credential store)
3. Test Connection → `kubectl cluster-info` equivalent
4. Set default namespace

### 7.2 Workload Overview

**Layout:** Left: deployments table (full width or 60%), Right: events panel (40%)

**Deployments table:**
```
NAME            NAMESPACE       DESIRED  READY  RESTARTS(1h)  IMAGE           AGE   STATUS
order-api       order-platform  3        3/3    12            1.4.2           2d    Degraded ⚠
inventory-api   order-platform  2        2/2    0             1.2.0           5d    Healthy
payment-api     order-platform  1        0/1    0             1.3.1           1d    Pending
```

- Status color: green=Healthy, yellow=Degraded (restarts>0 or not all ready), red=Failing
- Expandable row: shows pod list with individual pod status, node, IP, restarts
- Pod status colors: Running=green, Pending=yellow, CrashLoopBackOff=red, OOMKilled=red

**Events panel:**
- Filtered to selected namespace + optionally selected deployment
- Shows Warning events prominently (red icon)
- Columns: Type | Reason | Object | Message | Age

**Actions:**
- Per deployment: [Tail Logs] [Describe] [Scale] [Events]
- Per pod (expanded): [Tail Logs] [Describe] [Open Shell] [Delete] (with Prod confirmation)
- Top: [Refresh F5] [Namespace: order-platform v] [Watch: ON/OFF]

### 7.3 Pod / Log View

**Layout:**
```
[Deployment: order-api v]  [Pod: order-api-7d9f4-abc v] [Container: order-api v]
[Live: ON]  [Tail: 200 lines]  [Filter: ___]  [Level: All v]  [Correlation: ___]
[Clear]  [Export]

Log output area (monospace, line-numbered):
2026-03-07 14:23:01.234 [ERR] OrderProcessingService: Payment failed {CorrelationId: "abc123"}
  <- line highlighted red for ERR level
2026-03-07 14:23:01.250 [INF] Order state saved {OrderId: "ord-456"}
```

**Behavior:**
- Live tail: `IAsyncEnumerable<string>` streamed from `IAksClient.StreamPodLogsAsync`
- Ring buffer: keep last 10,000 lines in memory; scroll up to see history
- **Pod restart handling:** When pod UID changes, banner appears:
  "Pod restarted. Showing logs from new instance. [Switch] [Keep previous pod]"
- Multi-pod tailing: "Open parallel log" → new tab with color-coded lines per pod

**Filtering:**
- Text filter: real-time client-side highlight (not drop lines)
- Level filter: parses common log formats (Serilog JSON, ASP.NET console output)
- Correlation ID: highlights matching lines in bold

### 7.4 Shell & Port-Forward

**Open Shell:**
- Button on pod → checks for `/bin/sh` or `/bin/bash` available in container
- **Embedded terminal (primary):** `TerminalView.razor` hosts xterm.js via JSInterop.
  A local `Process` runs `kubectl exec -it [pod] -n [ns] -- /bin/sh`; stdin/stdout piped
  via a named pipe or WebSocket to xterm.js. All JSInterop in `terminalInterop.js` in wwwroot.
- **External fallback:** Spawns Windows Terminal with the kubectl exec command pre-filled
  (via `Process.Start` with `wt.exe` arguments)

**Port-Forward:**
- UI: popup "Port Forward [service/pod name]"
  ```
  Local port:  [8080]
  Remote port: [80  ]
  [Start Port Forward]
  ```
- Active forwards shown in status bar: `[→ 8080→order-api:80] [X]`
- Clicking a forward: copies `http://localhost:8080` to clipboard, shows in balloon
- Persisted across session (auto-reconnect on app restart with confirmation)
- "Tunnels panel": dedicated sub-view listing all active/previous forwards

---

## 8. Cross-Cutting UX Decisions

### 8.1 Keyboard Shortcuts

**Implementation:** Global keyboard shortcuts registered in `wwwroot/js/keyboardShortcuts.js`,
injected via `IJSRuntime` in `App.razor` `OnAfterRenderAsync`. Each shortcut calls a .NET method
via `DotNetObjectReference` which fires an event on `IAppEventBus`. Components subscribe to
relevant events (e.g., `CommandPaletteRequested`, `TabNavigationRequested`).

| Shortcut | Action |
|---|---|
| `Ctrl+P` | Open Command Palette |
| `Ctrl+1` | Navigate to Projects |
| `Ctrl+2` | Navigate to Service Bus |
| `Ctrl+3` | Navigate to Observability |
| `Ctrl+4` | Navigate to AKS |
| `Ctrl+,` | Open Settings |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Ctrl+W` | Close current tab |
| `Ctrl+\` | Toggle details pane |
| `F5` | Refresh current view |
| `Ctrl+Enter` | Execute query / Send message |
| `Ctrl+F` | Focus filter/search bar |
| `Ctrl+Shift+C` | Copy selected items as JSON |
| `Alt+1..4` | Switch environment (Dev/Test/Acc/Prod) |
| `Alt+Shift+P` | Open project selector |
| `Alt+D` | Open DLQ for current entity |
| `Ctrl+Shift+L` | Open Log view for current context |
| `Escape` | Close dialog / clear filter / cancel operation |

### 8.2 Consistency Rules

All three main areas (Service Bus, Observability, AKS) MUST use identical:

1. **Filter bar component:** Same layout (left: time/presets, middle: text search, right: run/cancel),
   same keyboard behavior (`Ctrl+Enter` to execute, `Escape` to clear)
2. **Table component:** Same selection behavior (click=select, Ctrl+click=multi, Shift+click=range),
   same right-click context menu structure (primary actions at top, secondary below separator),
   same column resize/reorder behavior
3. **Details pane:** Always right-hand side, always toggled with `Ctrl+\`, always tabbed header
4. **Loading states:** Same spinner position (top of content area), same "Cancel" button placement
5. **Error display:** Same error callout style (red border card at top of view), same retry button
6. **Empty states:** Illustrated empty state with descriptive message and primary action button

**Shared component library** in `SwebKit.App/Components/Shared/`:
- `FilterBar.razor` — generic filter toolbar with `EventCallback<FilterState> OnFilter`
- `DataTable.razor` — wraps `FluentDataGrid<TItem>` with consistent column, selection, and
  context-menu behavior; `TItem` generic so it works for SB messages, log entries, pods, etc.
- `DetailsPane.razor` — collapsible right panel with tabbed content slots
- `LoadingSpinner.razor` — centered spinner with cancel button
- `ErrorCallout.razor` — red-border error card with retry `EventCallback`
- `EmptyState.razor` — illustrated empty state with title, description, and action button
- `CommandPalette.razor` — modal overlay with text input and fuzzy-filtered command list
- `TabPanel.razor` — tab container managing open tabs, close, pin, reorder

### 8.3 Visual Safety for Production

1. **Top bar:** Background color changes to `#C8002A` (red) when Prod is selected
2. **Prod badge:** "⚠ PROD" badge in top bar, always visible regardless of nav section
3. **Per-pane banner:** Every pane shows "⚠ PRODUCTION ENVIRONMENT" banner at top (dismissible
   per session, re-appears on next environment switch)
4. **Confirmation dialogs:** All mutative actions in Prod show a modal with:
   - Action description
   - Affected resource count
   - "⚠ This is PRODUCTION" warning in red
   - For bulk operations (>10 items): type "CONFIRM" to proceed
5. **Mark environment as Production:** In Settings, user can flag an env as `Tier: Production`;
   app applies Prod styling even if name is not "Prod" (e.g., "Live", "Release")

### 8.4 Background Tasks & Non-Blocking Operations

**Task system:**
- `ITaskQueue` service: manages list of `BackgroundTask` records
- Status bar: spinner + count "3 tasks running" → click to expand task panel
- Task panel: list of tasks with [type] [name] [elapsed] [cancel button]
- Completed tasks: shown briefly in green, then removed after 3s

**Task types:**
- SB Resubmit (N messages) → shows per-message progress
- Log query → shows elapsed time, cancel cancels `CancellationToken`
- AKS port-forward → persistent, shows in status bar tunnel indicator
- AKS log tail → persistent, shows in tab header with live indicator dot

### 8.5 Persistence Scope

| Data | Scope | Storage |
|---|---|---|
| Window size/position | Machine | `ui-state.json` |
| Theme (light/dark) | Machine | `user-settings.json` |
| Last selected project | Machine | `ui-state.json` |
| Last selected environment | Per-project | `ui-state.json` |
| Open tabs + layout | Per-project+env | `ui-state.json` |
| Last used filters | Per-entity+env | `ui-state.json` |
| Saved queries | Per-project+env | `profiles.json` |
| Message templates | Per-project+env | `profiles.json` |
| Scenarios | Per-project | `profiles.json` |
| Favorite entities | Per-project+env | `profiles.json` |
| Active port-forwards | Per-project+env | `ui-state.json` (reconnect on startup) |
| Credentials / secrets | Machine (credential store) | Windows Credential Manager |

---

## 9. Implementation Roadmap

### Phase 1 — Foundation & MVP (Weeks 1–6)

**Goal:** Working app skeleton with real connections to all three services.

**Included:**
- Solution scaffold: all 5 projects created; MAUI Blazor Hybrid wiring in `MauiProgram.cs`
  (`.AddMauiBlazorWebView()`, `.AddFluentUIComponents()`, service registrations)
- `ICredentialStore` implementation for Windows (Credential Manager)
- Project + Environment CRUD: create/edit/delete projects and environments, config UI
- Environment color-coding in top bar + Prod safety banner
- Service Bus:
  - Connect via `DefaultAzureCredential` or connection string
  - List queues/topics/subscriptions in left nav tree
  - Peek messages (basic): list view + details pane (body + properties)
  - Basic DLQ view: list DLQ messages, single-message resubmit to original queue
- Observability:
  - Connect to App Insights (DefaultAzureCredential or API key)
  - Basic log table: time range, level filter, text search
  - 5 built-in KQL presets
- AKS:
  - Discover kubeconfig contexts
  - List deployments and pods (workload overview, non-live)
  - Show pod status and events panel
- Command palette: basic commands (switch project, switch env, navigate to sections)
- Keyboard shortcuts: Ctrl+P, Ctrl+1-4, F5, Ctrl+W

**Assumptions:** Single user, Windows only, Azure AD auth (DefaultAzureCredential)

---

### Phase 2 — Service Bus Power Features (Weeks 7–10)

**Included:**
- DLQ batch operations (multi-select, bulk resubmit/complete)
- Message composer (send/replay) with full property editing
- Message template system (save, load, manage)
- Scenario system (ordered message sequences, run with delays)
- Favorite entities in left nav with live message counts
- Auto-refresh for queues (configurable interval)
- Filter state persistence per entity
- Advanced SQL filter expression support
- Export: copy as JSON, export CSV

---

### Phase 3 — Observability Depth (Weeks 11–15)

**Included:**
- Trace / correlation timeline view (waterfall, span details)
- Mini metrics dashboard (5 default tiles, Blazor-ApexCharts)
- Saved queries per Project+Environment
- User-defined query builder (UI form → KQL generation)
- OTLP provider implementation (`OtlpObservabilityProvider`)
- Cross-linking: DLQ → logs by CorrelationId, Log entry → trace
- Export: CSV, copy rows as JSON, copy KQL
- Auth error UX improvements (clear guidance, re-auth flow)

---

### Phase 4 — AKS Depth (Weeks 16–20)

**Included:**
- Live log tailing with ring buffer and pod-restart handling
- Multi-pod tailing (color-coded, side-by-side tabs)
- Port-forward management (start/stop, status bar tunnels panel, reconnect on startup)
- Shell launcher (Windows Terminal integration)
- Real-time pod watch (live status updates via Kubernetes watch API)
- AKS events timeline (chronological event view for a namespace/deployment)
- Cross-link: AKS pod → Observability filtered by pod/service

---

### Phase 5 — Polish & Advanced (Weeks 21–26)

**Included:**
- Full fuzzy command palette (all commands, keyboard-navigable)
- Dockable/reorderable tabs (drag-and-drop reorder)
- Notifications system (toast + notification center for completed tasks, errors)
- Import/export project configurations (share profiles with teammates)
- Keyboard audit: verify all 19 shortcuts work consistently across all views
- Full dark/light theme support
- macOS and Linux MAUI target testing (identify blockers)
- Performance profiling: heavy query + large message list scenarios
- Settings page: all global preferences, shortcut customization, theme

---

## 10. Risks & Trade-offs

### R1: Secrets Management Complexity
**Risk:** Windows Credential Manager works well on Windows; cross-platform is harder.
**Mitigation:** `ICredentialStore` abstraction allows swapping implementations. On macOS: Keychain.
On Linux: SecretService API. For MVP, Windows-only is acceptable. Consider
`Microsoft.Extensions.SecretManager` or `Azure.Security.KeyVault.Secrets` for team-shared secrets
in a future phase.

### R2: Observability Abstraction Depth
**Risk:** App Insights (KQL) and pure OTLP are very different — the abstraction may leak.
**Mitigation:** The `IObservabilityProvider` maps to `LogEntry`, `TraceSpan`, `MetricSeries` — all
provider-specific details are hidden behind the implementation. The Trace view only renders the
normalized `TraceTimeline`. Accept that some AppInsights-specific KQL power features won't be
available via OTLP (and vice versa); offer "Raw KQL / Advanced mode" as an escape hatch.

### R3: UI Responsiveness with Heavy Data
**Risk:** Peeking 500+ SB messages, streaming 10k log lines, or rendering large trace timelines
can cause UI stutter in MAUI.
**Mitigation:**
- All I/O on background threads with `CancellationToken`
- Virtualized list rendering (SfDataGrid handles this)
- Log stream: ring buffer (last 10k lines) + UI update throttling (batch UI refresh every 100ms)
- Trace timeline: lazy-render only visible spans
- Add configurable result limits everywhere

### R4: UI Density vs. Overwhelming
**Risk:** Too much information visible at once causes cognitive overload; too little is useless.
**Mitigation:**
- Defaults are conservative (fewer columns, shorter time ranges, collapsed panels)
- Power user features behind right-click / toolbar overflow menus
- Details pane is collapsible — not forced on screen
- Column selection: user picks which table columns to show per view
- "Compact mode" option for tables (reduce row height)

### R5: Blazor Hybrid Data Grid Performance
**Risk:** Rendering thousands of rows in a Blazor grid inside BlazorWebView could cause stutter;
BlazorWebView uses embedded WebView2 (Chromium) on Windows, which adds some overhead vs. native.
**Mitigation:** `FluentDataGrid` supports `Virtualize="true"` which renders only visible rows.
Cap all data loads at configurable page sizes (default 200 rows). For log tailing, use an
`IAsyncEnumerable` consumer that batches DOM updates via a throttled `StateHasChanged()` call
(e.g., every 100ms), not per-line. WebView2 on Windows is mature and fast for this class of
workload — validate in a Phase 1 spike with a 10k-row dataset.

### R6: Kubeconfig Discovery & Auth Diversity
**Risk:** AKS clusters may use different auth methods (AAD, certificates, OIDC); kubeconfig
contexts can be stale or point to unreachable clusters.
**Mitigation:** `KubernetesClient` handles most kubeconfig auth modes natively. Show "Test
Connection" prominently. Handle `HttpOperationException` and `TimeoutException` gracefully with
clear user-facing error messages. Allow manual credential override.

### R7: Long-Running Operations Blocking Features
**Risk:** Port-forward and log-tail sessions are long-lived; poor lifecycle management causes
leaks, stale connections, or app slowdown.
**Mitigation:** All long-running operations tracked in `ITaskQueue`. `CancellationTokenSource`
per session. Dispose on tab close, environment switch, or app shutdown. Port-forwards run as
child processes (`kubectl port-forward`) with process group management for clean teardown.

---

## Verification Plan

### MVP Verification Checklist

1. **Project/Env CRUD:** Create project "TestProject", add Dev/Prod environments, switch between
   them — verify top bar color changes and Prod banner appears
2. **Service Bus:** Connect to a real namespace, list queues, peek 10 messages, view body +
   properties in details pane, resubmit one DLQ message
3. **Observability:** Connect to App Insights workspace, run "Errors last 15m" preset, verify
   results appear in table within 5 seconds, click row to see details
4. **AKS:** Connect to a cluster via kubeconfig, see deployments list, see pod status for one
   deployment, see events panel populated
5. **Keyboard:** Ctrl+P opens palette, Ctrl+1-4 navigate sections, Alt+1-4 switch env,
   Ctrl+W closes tab
6. **Prod safety:** With Prod environment selected, attempt SB resubmit — verify confirmation
   dialog appears before action executes

### Integration Test Approach

- `SwebKit.Azure.Tests`: Use Azure Service Bus emulator (or testcontainers) for SB client tests
- `SwebKit.Kubernetes.Tests`: Use k3s in Docker or mock `IKubernetes` interface
- `SwebKit.Core.Tests`: Pure unit tests for domain logic, config serialization, credential store
- End-to-end: manual testing against real Azure dev environment per phase

---

## Critical Files to Create (Phase 1)

```
SwebKit.sln

# Domain & abstractions (SwebKit.Core)
src/SwebKit.Core/Domain/Project.cs
src/SwebKit.Core/Domain/ProjectEnvironment.cs
src/SwebKit.Core/Domain/ServiceBusConfig.cs
src/SwebKit.Core/Domain/ObservabilityConfig.cs
src/SwebKit.Core/Domain/AksConfig.cs
src/SwebKit.Core/Abstractions/IObservabilityProvider.cs
src/SwebKit.Core/Abstractions/IServiceBusClient.cs
src/SwebKit.Core/Abstractions/IAksClient.cs
src/SwebKit.Core/Configuration/ICredentialStore.cs
src/SwebKit.Core/Configuration/ProfileRepository.cs
src/SwebKit.Core/Services/AppStateService.cs        # current project+env, fires EnvironmentChanged

# Azure integrations (SwebKit.Azure)
src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs
src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs

# Kubernetes integrations (SwebKit.Kubernetes)
src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs

# MAUI Blazor Hybrid app (SwebKit.App)
src/SwebKit.App/MauiProgram.cs                      # MAUI + Blazor + DI setup
src/SwebKit.App/MainPage.xaml                       # Only content: <BlazorWebView>
src/SwebKit.App/wwwroot/index.html                  # Blazor bootstrap
src/SwebKit.App/wwwroot/css/app.css                 # Global styles, CSS variables for themes
src/SwebKit.App/wwwroot/js/monacoLoader.js          # Monaco Editor init
src/SwebKit.App/Components/App.razor                # Router root
src/SwebKit.App/Components/Layout/MainLayout.razor  # Top bar + left nav + content + status bar
src/SwebKit.App/Components/Layout/TopBar.razor      # Project+env selector, command palette trigger
src/SwebKit.App/Components/Layout/LeftNav.razor     # Collapsible side nav
src/SwebKit.App/Components/Layout/StatusBar.razor   # Connection status, task queue
src/SwebKit.App/Components/Pages/ProjectsPage.razor
src/SwebKit.App/Components/Pages/ServiceBusPage.razor
src/SwebKit.App/Components/Pages/ObservabilityPage.razor
src/SwebKit.App/Components/Pages/AksPage.razor
src/SwebKit.App/Components/Pages/SettingsPage.razor
src/SwebKit.App/Components/Shared/FilterBar.razor
src/SwebKit.App/Components/Shared/DataTable.razor
src/SwebKit.App/Components/Shared/DetailsPane.razor
src/SwebKit.App/Components/Shared/CommandPalette.razor
src/SwebKit.App/Components/Shared/TabPanel.razor
src/SwebKit.App/Components/ServiceBus/EntityTree.razor
src/SwebKit.App/Components/ServiceBus/MessageListView.razor
src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor
src/SwebKit.App/Components/ServiceBus/DlqView.razor
src/SwebKit.App/Components/Observability/LogTableView.razor
src/SwebKit.App/Components/Observability/KqlEditor.razor   # BlazorMonaco wrapper
src/SwebKit.App/Components/Aks/WorkloadOverview.razor
src/SwebKit.App/Components/Aks/PodLogView.razor
src/SwebKit.App/Components/Aks/EventsPanel.razor
src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs
```
