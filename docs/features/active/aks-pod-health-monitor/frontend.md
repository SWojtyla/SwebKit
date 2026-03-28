# Frontend Plan — AKS Pod Health Monitor

---

title: "Frontend Plan — AKS Pod Health Monitor"
owner: ""
status: "Not started"

---

## Goal

Provide UI controls for configuring pod health monitoring (namespace selection, enable/disable) and displaying monitoring status and alert history — integrated naturally into the existing AKS page and app shell.

## Impacted areas

- `src/SwebKit.App/Components/Aks/` — new components
- `src/SwebKit.App/Components/Aks/AksPage.razor` — integration point
- `src/SwebKit.App/Components/Shared/` — monitoring indicator (if placed in top bar)
- `src/SwebKit.App/_Imports.razor` — namespace imports (pitfall BL-1)
- `src/SwebKit.App/wwwroot/css/` — component styles if needed

## UX and accessibility notes

### User flows

1. **Enable monitoring:**
   User is on AKS page → opens namespace monitoring panel → selects namespaces → clicks "Start Monitoring" → status indicator turns active → monitoring runs in background

2. **Receive alert:**
   User is on any page (or app is minimized) → pod goes down → Windows toast appears → user clicks toast → app foregrounds to AKS page with relevant namespace

3. **Review history:**
   User opens alert history panel → sees recent pod health events with timestamps → can clear history or acknowledge alerts

4. **Disable monitoring:**
   User opens monitoring panel → clicks "Stop Monitoring" → status indicator turns inactive → polling stops

### Accessibility

- Namespace selector: keyboard navigable, screen reader labels
- Status indicator: use `aria-label` to describe monitoring state
- Alert history: sortable, focusable rows
- Fluent UI Blazor components handle most a11y concerns by default

## Components

### 1. `NamespaceMonitorSelector.razor`

**Location:** `src/SwebKit.App/Components/Aks/`

**Purpose:** Let the user select which namespaces to monitor and start/stop monitoring.

**Design:**

- Fluent `FluentSelect` or `FluentListbox` with multi-select showing all available namespaces
- Namespaces loaded from `IAksClient.GetNamespacesAsync()`
- "Start Monitoring" / "Stop Monitoring" button
- Shows current monitored count: "Monitoring 3 namespaces"
- Displays polling interval info

**Parameters:**

```csharp
[Parameter] public IAksClient? AksClient { get; set; }
// Injected: IPodHealthMonitorService, INotificationService
```

**States:**

- Loading namespaces
- Idle (not monitoring)
- Active (monitoring in progress)
- Error (client not connected, auth failure)

### 2. `MonitoringStatusIndicator.razor`

**Location:** `src/SwebKit.App/Components/Aks/`

**Purpose:** Small badge/icon showing whether monitoring is active, with quick stats.

**Design:**

- Compact indicator: green dot + "Monitoring" or gray dot + "Off"
- Tooltip or flyout with: monitored namespace count, last poll time, recent alert count
- Placed in AKS page header area (near existing `AutoRefreshToggle`)
- Click opens the `NamespaceMonitorSelector` or a flyout

**State updates:**

- Subscribe to `IPodHealthMonitorService.PodHealthChanged` event
- Use `InvokeAsync(StateHasChanged)` after event (pitfall BL-2)

### 3. `AlertHistoryPanel.razor`

**Location:** `src/SwebKit.App/Components/Aks/`

**Purpose:** Show a scrollable list of recent pod health events for review.

**Design:**

- Fluent `FluentDataGrid` or simple list
- Columns: Time, Pod, Namespace, Event Type, Status
- Newest events at top
- Max 100 events in memory (ring buffer in service)
- "Clear History" button
- Color-coded severity: Failed (red), CrashLoop (orange), Unknown (yellow), NotReady (amber)

**Data source:**

- Subscribe to `IAppEventBus` for `PodHealthEvent`
- Maintain local `List<PodHealthEvent>` in component
- Optionally expose via `IPodHealthMonitorService.RecentEvents`

## Integration with AKS page

### Layout integration

```
┌─────────────────────────────────────────────┐
│ AKS Page Header                              │
│  [Context Selector] [AutoRefresh] [Monitor●] │
├──────────────────────────┬──────────────────┤
│ Pod Grid                  │ Sidebar (opt.)   │
│                          │ ┌──────────────┐ │
│                          │ │ Namespace     │ │
│                          │ │ Monitor       │ │
│                          │ │ Selector      │ │
│                          │ ├──────────────┤ │
│                          │ │ Alert        │ │
│                          │ │ History      │ │
│                          │ └──────────────┘ │
└──────────────────────────┴──────────────────┘
```

**Option A (recommended):** Add monitoring controls as a collapsible sidebar panel or flyout on the AKS page, toggled by the `MonitoringStatusIndicator`. Keeps the pod grid as the primary view.

**Option B:** Add monitoring controls as a separate tab within the AKS page.

Final layout decision deferred to implementation — start with Option A.

## Tasks

- [ ] **PHM-14** — `NamespaceMonitorSelector.razor`
  - Multi-select namespace list from `GetNamespacesAsync()`
  - Start/Stop button bound to `IPodHealthMonitorService`
  - Persist selections to config via service
  - Handle loading and error states
- [ ] **PHM-15** — `MonitoringStatusIndicator.razor`
  - Green/gray dot indicator
  - Tooltip with stats
  - Click interaction to toggle selector visibility
  - Subscribe to service events with proper dispatcher pattern
- [ ] **PHM-16** — `AlertHistoryPanel.razor`
  - Event list with color-coded severity
  - Subscribe to `IAppEventBus` for `PodHealthEvent`
  - Ring buffer (max 100 events)
  - Clear history action
- [ ] **PHM-17** — AKS page integration
  - Add `MonitoringStatusIndicator` to page header
  - Add collapsible panel for selector + history
  - Ensure no interference with existing pod grid, auto-refresh, keyboard navigation
  - Add namespace to `_Imports.razor` if new subdirectory created (pitfall BL-1)

## Validation

- Component tests: Not started
- Manual UX checks:
  - Namespace selector loads namespaces correctly
  - Start/Stop monitoring toggles indicator state
  - Alert appears in history panel within seconds of service detection
  - Indicator updates without page refresh
  - Panel does not interfere with pod grid selection/keyboard nav
  - Works after navigating away and back to AKS page

## Notes

- **Pitfall BL-2:** All event handlers from `IPodHealthMonitorService` or `IAppEventBus` must call `await InvokeAsync(StateHasChanged)` — the service events fire from the timer thread, not the Blazor dispatcher.
- **Pitfall BL-5:** `MonitoringStatusIndicator` receives no parameters that change per-render, so `OnParametersSetAsync` overhead is minimal. But guard if parameters are added later.
- **Component disposal:** Unsubscribe from `IAppEventBus` and service events in `Dispose()` to prevent memory leaks and calls to disposed components.
- Follow existing Fluent UI Blazor component patterns from `PodGrid.razor` and `AutoRefreshToggle.razor`.
