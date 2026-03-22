# Frontend — Application Insights Viewer

## Page Entry Point

**File:** `src/SwebKit.App/Components/Pages/AppInsightsPage.razor`

Registered in nav under `Alt+5` (currently unused slot) and as a command palette target.
Nav icon: a chart/pulse icon (Fluent System Icons `PulseSquare`).

---

## Layout Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ [Resource Selector ▼]  [● Last 24h ▼]  [↻ Refresh]     [⚙ Settings]│
├─────────────────────────────────────────────────────────────────────┤
│ [Overview] [Failures] [Performance] [Logs] [Availability]           │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   Tab content area (takes remaining vertical space)                 │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

All tabs share the resource selector and time range picker in the top toolbar.

---

## Components

### `AppInsightsPage.razor`
Top-level page. Manages:
- Selected resource (persisted to config)
- Selected time range
- Tab routing (`ActiveTab` enum)
- Resource list loading state

### `ResourceSelectorDialog.razor`
Flyout/dropdown triggered by the resource selector button.

- Shows all discovered resources grouped by subscription
- Search box to filter by name, subscription, or resource group
- Shows resource location and resource group as secondary text
- Loading state: "Scanning subscriptions… (4 / 11)"
- Selecting a resource closes the dialog and triggers data reload

```
┌──────────────────────────────────────────┐
│ 🔍 Filter resources...                   │
├──────────────────────────────────────────┤
│ ▾ Subscription: My Dev Sub               │
│     📊 my-app-insights     East US       │
│     📊 api-monitoring      West Europe   │
│ ▾ Subscription: Production               │
│     📊 prod-appinsights    East US  ★    │
└──────────────────────────────────────────┘
```

★ = currently selected

### `TimeRangePicker.razor`
Dropdown with preset options + custom range:
- Last 1 hour / 6 hours / 24 hours / 7 days / 30 days
- Custom: two `<FluentDatePicker>` fields (start / end)

---

## Tab: Overview

**Component:** `AppInsightsOverview.razor`

Layout:
```
┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│ Requests │ │ Failures │ │   P95    │ │Exceptions│ │ Avail.   │
│  12,403  │ │  1.2%    │ │  342 ms  │ │    87    │ │  99.8%   │
│ +8% ↑   │ │ -0.3% ↓  │ │          │ │          │ │          │
└──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘

┌──────────────────────────────────┐ ┌──────────────────────────────┐
│   Request Volume                 │ │   Failure Rate               │
│   [ApexCharts area chart]        │ │   [ApexCharts area chart]    │
└──────────────────────────────────┘ └──────────────────────────────┘
```

- Summary cards use `FluentCard` with color-coded trend indicator
- Failure rate card turns amber if > 1%, red if > 5%
- Charts are ApexCharts `area` type with the app's primary theme color
- Hovering a chart point shows tooltip with exact value + timestamp

---

## Tab: Failures

**Component:** `AppInsightsFailures.razor`

Split layout (left list + right detail pane, same pattern as MessageListView):

```
┌─────────────────────────────┬──────────────────────────────────────┐
│ Exception Type        Count │ NullReferenceException               │
│─────────────────────────────│ Problem ID: abc123                   │
│ NullReferenceExc…     1,204 │ Count: 1,204   Last: 2 min ago       │
│ ArgumentException       342 │                                      │
│ HttpRequestExc…         118 │ Sample Message:                      │
│ TimeoutException         67 │ Object reference not set to an...    │
│ ...                         │                                      │
│                             │ Stack Trace:                         │
│                             │ ┌──────────────────────────────────┐ │
│                             │ │ at MyApp.Foo.Bar() line 42       │ │
│                             │ │ at MyApp.Controllers.HomeCtrl... │ │
│                             │ └──────────────────────────────────┘ │
│                             │ [📋 Copy Stack Trace]                │
│                             │                                      │
│                             │ Recent Occurrences                   │
│                             │ 14:32:01  op: GET /api/users        │
│                             │ 14:28:44  op: POST /api/orders      │
│                             │ [→ View in Logs tab]                 │
└─────────────────────────────┴──────────────────────────────────────┘
```

- Left panel: `FluentDataGrid` with exception type + count + sparkline of occurrences
- Clicking a row loads the detail pane (async, shows skeleton loader)
- "View in Logs tab" button pre-populates the Logs KQL editor and switches tab

---

## Tab: Performance

**Component:** `AppInsightsPerformance.razor`

```
┌──────────────────────────────────────────────────────────────────────┐
│ Sort by: [P95 Duration ▼]    Show: [Top 20 ▼]                        │
├─────────────────────────────────┬────────┬──────┬────────┬──────────┤
│ Operation                       │ Req/h  │ Fail │  P50   │  P95     │
├─────────────────────────────────┼────────┼──────┼────────┼──────────┤
│ GET /api/v2/orders              │  2,400 │  2%  │  120ms │  ████ 1.8s│
│ POST /api/v2/payments           │    340 │  0%  │   80ms │  ██   410ms│
│ GET /api/v2/users/{id}          │  8,200 │  0%  │   22ms │  █    120ms│
└─────────────────────────────────┴────────┴──────┴────────┴──────────┘
```

- P95 column has an inline bar chart (CSS width proportional to max P95 in view)
- Color thresholds: green < 500 ms, amber 500–2000 ms, red > 2 s
- Failure % column: green < 1%, amber 1–5%, red > 5%
- Clicking a row opens detail pane:
  - P50 / P95 / P99 trend chart (ApexCharts)
  - List of sample slow requests with operation ID + timestamp
  - "Drill into Logs" button

---

## Tab: Logs

**Component:** `AppInsightsLogs.razor`

Split layout: preset sidebar + editor + results

```
┌───────────────┬──────────────────────────────────────────────────────┐
│ Presets       │  [BlazorMonaco KQL editor]                Ctrl+Enter  │
│ ─────────────│  requests                                             │
│ Top Exceptions│  | where timestamp > ago(24h)                        │
│ Failed Req.   │  | where success == false                            │
│ Slow Requests │  | summarize count() by bin(timestamp, 1h)           │
│ Dependencies  │  ──────────────────────────────────────── [▶ Run]    │
│ Custom Events │                                                       │
│ Availability  │  ─── Results (342 rows, 1.2s) [📋 Copy CSV] ─────── │
│ ─────────────│  ┌────────────────────────────────────────────────┐   │
│ Saved Queries │  │ timestamp    │ name │ duration │ resultCode    │   │
│ + Save Query  │  │ 14:32:01    │ GET… │ 142ms    │ 500           │   │
│               │  │ 14:31:55    │ POST │  88ms    │ 200           │   │
│               │  └────────────────────────────────────────────────┘  │
└───────────────┴──────────────────────────────────────────────────────┘
```

- Monaco editor with KQL language mode (already available via BlazorMonaco)
- `Ctrl+Enter` runs the query (same shortcut as in VS Code)
- Results displayed in `FluentDataGrid` with dynamic columns based on query output
- Row count + query execution time shown above results
- "Truncated" warning banner if row limit was hit
- "Copy CSV" exports results to clipboard
- Saved Queries: user can name and save any query; persisted to `profiles.json`
- Clicking a preset loads its KQL and runs it immediately

---

## Tab: Availability

**Component:** `AppInsightsAvailability.razor`

```
┌──────────────────────────────────────────────────────────────────────┐
│   Overall Availability: 99.7%                                        │
│   [ApexCharts heatmap — tests × time slots, green/red cells]         │
├──────────────────────┬──────────┬─────────┬──────────────────────────┤
│ Test Name            │ Location │ Avail.  │ Avg Response             │
├──────────────────────┼──────────┼─────────┼──────────────────────────┤
│ Homepage ping        │ East US  │ 100%    │  88ms                   │
│ API health check     │ West EU  │  97.2%  │ 210ms  ▲ 3 failures      │
└──────────────────────┴──────────┴─────────┴──────────────────────────┘
```

- Heatmap: X = time buckets, Y = test+location combos; cell = pass/fail ratio color
- Clicking a failure cell loads the detail pane with the failure message and response body

---

## UX Improvements Over Azure Portal

| Azure Portal Pain Point | SwebKit Solution |
|---|---|
| 10+ clicks to see a stack trace | 2 clicks: tab → row → stack in right pane |
| Slow page navigation (full reloads) | Single-page, tab switching, no reloads |
| KQL presets buried in sidebar | Always-visible preset library in Logs tab |
| No quick copy for stack traces or operation IDs | Copy buttons on every detail field |
| Can't save KQL queries per-resource | Saved Queries persisted in `profiles.json` |
| No inline performance bar charts | P95 bar column in Performance tab |
| Failure rate not visible at a glance | Color-coded summary cards on Overview |
| Resource switching = full navigation | Resource selector dropdown, no page reload |
| No keyboard-driven workflow | `Ctrl+Enter` run query, `F5` refresh, `Ctrl+\` toggle detail pane |
| Time picker is hidden in menus | Persistent time range picker in top bar |

---

## Keyboard Shortcuts (App Insights specific)

| Shortcut | Action |
|---|---|
| `F5` | Refresh current tab |
| `Ctrl+Enter` | Run KQL query (Logs tab) |
| `Ctrl+\` | Toggle detail pane |
| `Ctrl+F` | Focus filter / search |
| `Alt+1`–`Alt+5` | Switch between Overview / Failures / Performance / Logs / Availability |

---

## Components to Create

| File | Responsibility |
|---|---|
| `AppInsightsPage.razor` | Page shell, toolbar, tab host |
| `ResourceSelectorDialog.razor` | Resource picker flyout |
| `TimeRangePicker.razor` | Shared time range dropdown (reusable) |
| `AppInsightsOverview.razor` | Overview tab |
| `AppInsightsFailures.razor` | Failures tab |
| `AppInsightsPerformance.razor` | Performance tab |
| `AppInsightsLogs.razor` | Logs tab with Monaco + presets |
| `AppInsightsAvailability.razor` | Availability tab |
| `ExceptionDetailPane.razor` | Exception detail / stack trace panel |

---

## Tasks

- [ ] Scaffold `AppInsightsPage.razor` with tab layout and toolbar
- [ ] Build `ResourceSelectorDialog.razor` with subscription grouping and search
- [ ] Build `TimeRangePicker.razor` (preset + custom date range)
- [ ] Build Overview tab with summary cards and ApexCharts
- [ ] Build Failures tab with grouped list and detail pane
- [ ] Build Performance tab with inline bar chart column
- [ ] Build Logs tab with Monaco editor, preset sidebar, results grid
- [ ] Build Availability tab with heatmap chart
- [ ] Wire up all keyboard shortcuts
- [ ] Register page in nav + command palette
