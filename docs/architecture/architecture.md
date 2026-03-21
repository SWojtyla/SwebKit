<!-- Copied from docs/ARCHITECTURE.md -->

# SwebKit — Architecture Overview

> Full detail: [DESIGN.md](design.md)

## What Is SwebKit

A .NET MAUI Blazor Hybrid desktop tool for .NET developers who work daily with:

- **Azure Service Bus** — inspect queues, fix DLQs, replay messages
- **AKS (Kubernetes)** — workload overview, live log tail, port-forward, pod shell
- **Redis** — inspect keys, view values, manage cache
- **Azure Storage** — browse blobs, containers
- **Azure DevOps** — view and trigger releases

Each feature is standalone with its own configuration stored in a single global `AppConfig` (`profiles.json`).

## Functional Deep Dives

Per-functionality architecture notes live in `docs/architecture/functionalities/`.

### Scope

- What each functionality supports today.
- Core technical flow and runtime behavior.
- Main code locations to inspect first.
- Important implementation notes and known constraints.

- [Service Bus](functionalities/service-bus.md)
- [AKS](functionalities/aks.md)
- [Redis](functionalities/redis.md)
- [Settings and Configuration](functionalities/settings-and-configuration.md)
- [Storage](functionalities/storage.md)

### Update Rule

Whenever behavior changes in one of these areas, update the corresponding file under
`docs/architecture/functionalities/` in the same change set as the code.

---

## Tech Stack

| Layer         | Choice                                                                  |
| ------------- | ----------------------------------------------------------------------- |
| Platform      | .NET MAUI Blazor Hybrid (Windows primary)                               |
| UI Components | Microsoft Fluent UI Blazor (`Microsoft.FluentUI.AspNetCore.Components`) |
| Charts        | Blazor-ApexCharts (metrics dashboard only)                              |
| Code Editor   | BlazorMonaco (Monaco Editor via JSInterop — JSON body)                  |
| Terminal      | xterm.js via JSInterop (`TerminalView.razor`)                           |
| Azure SB      | `Azure.Messaging.ServiceBus`                                            |
| Kubernetes    | `KubernetesClient`                                                      |
| Serialization | `System.Text.Json` (source generators)                                  |
| Secrets       | Windows Credential Manager (ICredentialStore abstraction)               |

---

## Solution Layout

```
SwebKit.sln
├── src/
│   ├── SwebKit.App/          # MAUI Blazor Hybrid app (all Razor components)
│   ├── SwebKit.Core/         # Domain models, interfaces, config logic
│   ├── SwebKit.Azure/        # Azure SB + App Insights implementations
│   ├── SwebKit.Kubernetes/   # Kubernetes/AKS implementation
└── tests/
    ├── SwebKit.Core.Tests/
    ├── SwebKit.Azure.Tests/
    └── SwebKit.Kubernetes.Tests/
```

---

## Key Design Decisions

### Blazor Hybrid (not XAML)

All UI lives in Razor components inside a `BlazorWebView`. `MainPage.xaml` is a thin MAUI shell
containing only the `<BlazorWebView>`. This gives access to the full web component ecosystem
(Fluent UI, Monaco Editor, xterm.js) and CSS-based layouts.

### Global Config Model

- A single `AppConfig` instance (stored as `profiles.json`) holds all feature configs
- `AppStateService.Config` exposes the global config to all Blazor components
- Feature pages (`ServiceBusPage`, `AksPage`, `RedisPage`, `StoragePage`) read their config directly from `AppState.Config`
- `CascadingValue<AppStateService>` propagates context to all Blazor components

### Secrets

- Config files (`profiles.json`, `ui-state.json`) contain **no secrets**
- All credentials stored in Windows Credential Manager via `ICredentialStore`
- Credential refs are logical string keys; secrets decoded at runtime only

### Core Abstractions (in `SwebKit.Core`)

- `IServiceBusClient` — Peek, Send, Resubmit DLQ, Complete, List entities
- `IAksClient` — GetDeployments, GetPods, StreamLogs, PortForward, OpenShell
- `ICredentialStore` — Save, Get, Delete

### UI Consistency

All feature areas share identical:

- `FilterBar.razor` — filter toolbar component
- `DataTable.razor` — wraps `FluentDataGrid<TItem>` with consistent column/selection behavior
- `DetailsPane.razor` — collapsible right-hand details panel
- Same keyboard shortcuts, same error display, same loading states

### Keyboard Shortcuts

Global shortcuts registered via `keyboardShortcuts.js` (JSInterop). Key bindings:
`Ctrl+P` (command palette), `Alt+1-5` (navigate sections),
`Ctrl+Tab`/`Ctrl+Shift+Tab` (tab navigation), `Ctrl+W` (close tab), `F5` (refresh),
`Ctrl+Enter` (execute/send), `Ctrl+F` (focus filter), `Ctrl+\` (toggle details pane).

### Production Safety

When `Tier = Production` environment is selected:

- Top bar turns red (`#C8002A`)
- `⚠ PROD` badge always visible
- Per-pane production banner
- Confirmation dialogs for all mutative/destructive actions
- Bulk ops (>10 items): type "CONFIRM" to proceed

---

## Data Flow Example: Peek Queue Messages

```
User selects "order-queue" in EntityTree.razor
  → ServiceBusPage.razor sets ActiveEntity = "order-queue"
  → MessageListView.razor calls IServiceBusClient.PeekMessagesAsync("order-queue", 50, ct)
      → AzureServiceBusClient creates ServiceBusReceiver in PeekLock mode
      → Returns List<SbMessage>
  → FluentDataGrid renders rows
  → User clicks row → MessageDetailPane.razor shows body (JSON formatted) + properties
```
