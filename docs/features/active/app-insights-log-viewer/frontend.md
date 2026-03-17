# Frontend Plan — App Insights Log Viewer

---

title: "Frontend Plan - App Insights Log Viewer"
owner: ""
status: "Planned"
created: "2026-03-17"
updated: "2026-03-17"

---

## Goal

Add a functional log viewer UI to the existing `ObservabilityPage`. The viewer shows
App Insights log results in a data grid, lets the user filter by time range and severity,
toggle to KQL mode, and see which Microsoft account is authenticated. The config form
gains a browseable resource picker so the user can discover and select from their
accessible App Insights instances instead of pasting a raw resource ID.

---

## Impacted files

| File                                                             | Change                                                                         |
| ---------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`       | Auth status bar, time range and severity filter controls, partial-data warning |
| `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor` | Replace resource ID text field with resource picker (browse combobox)          |
| `src/SwebKit.App/Components/Shared/ErrorCallout.razor`           | Already exists; use for auth and query errors                                  |

No new component files are required for the initial deliverable — all additions go into
the existing page and config form.

---

## Component layout

```
ObservabilityPage
├── AuthStatusBar          ← new: credential identity badge + error ribbon
├── QueryToolbar           ← existing: extend with TimeRangePicker + SeverityFilter
│   ├── TimeRangePicker    ← new inline control (FluentSelect)
│   ├── SeverityFilter     ← new inline multi-select (FluentSelect, multi)
│   └── KQL toggle         ← existing; keep
├── FluentDataGrid (log results)  ← existing; extend columns
│   └── Columns: Timestamp · Severity · Message · OperationId
└── DetailsPane            ← existing; extend property mapping for LogEntry fields
```

---

## Auth status bar

A small banner immediately below the page title. States:

| State        | Display                                                                             |
| ------------ | ----------------------------------------------------------------------------------- |
| Not tested   | Grey — "Not connected"                                                              |
| Testing      | Spinner — "Connecting…"                                                             |
| Connected    | Green badge — `user@contoso.com` (or "DefaultAzureCredential" if UPN not available) |
| Auth failure | Red callout — `LastAuthError` message with a "Retry" button                         |

Rendered using `FluentBadge` / `FluentMessageBar` from Fluent UI Blazor.
Call `Provider.TestConnectionAsync()` on environment change (existing `OnEnvironmentChanged`
lifecycle already triggers a provider rebuild — extend it to also test the connection).

---

## Config form — resource picker

Replace the plain `AppInsightsResourceId` text field with a **two-mode picker**:

```
App Insights Resource

[ Search resources...               ] [Refresh ↺]
  ▼ my-app-insights  (my-subscription · rg-prod)
    other-app          (my-subscription · rg-staging)
    analytics-prod     (other-subscription · rg-ops)
  ...

Resource ID (read-only when selected, editable manually)
[ /subscriptions/.../components/my-app-insights ]

Or use Workspace ID instead
[ xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx           ]
```

### Behaviour

| Interaction                                    | Result                                                                                        |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Config form opens                              | Resource list is **not** auto-loaded (lazy)                                                   |
| User clicks the search box or `Refresh` button | `IAppInsightsDiscoveryService.ListResourcesAsync()` is called; spinner shows while loading    |
| Load succeeds                                  | Results populate the `FluentCombobox`; grouped by subscription name                           |
| Load fails (no credential)                     | Inline warning: _"Could not discover resources — check your login."_ Manual entry still works |
| User types in search box                       | `nameFilter` is passed to `ListResourcesAsync`; debounced 300 ms                              |
| User selects a resource                        | `AppInsightsResourceId` field is populated; `WorkspaceId` field is cleared                    |
| User clears the selection                      | Both fields cleared                                                                           |
| User edits the resource ID manually            | Selection in the combobox is cleared; manual value is used                                    |
| More than 1 000 results                        | Warning badge: _"Showing 1000 results — type to search."_                                     |

### Combobox item format

Each option shows:

- **Bold**: resource name
- **Subtitle**: `{subscriptionId short} · {resourceGroup}`

Items are sorted alphabetically by name within each subscription group.

### Hint

Below both fields, a persistent info callout:

> _Resource ID takes priority for queries. Set either a resource ID (direct App Insights
> querying) or a Workspace ID (Log Analytics workspace path). Resources are loaded using
> your Microsoft sign-in — only resources you have Reader access to will appear._

---

## Config form — subscription scope (optional)

If wanted later: an optional `SubscriptionId` filter field (`FluentTextField`) above the
resource picker. When set, narrows the Resource Graph query to that subscription only.
Not required for the initial deliverable — track as a follow-up.

---

## Time range selector

Inline `FluentSelect` in the query toolbar.

| Label         | Value passed to `LogQuery.TimeRange` |
| ------------- | ------------------------------------ |
| Last 1 hour   | `PT1H`                               |
| Last 6 hours  | `PT6H`                               |
| Last 24 hours | `P1D`                                |
| Last 7 days   | `P7D`                                |
| Last 30 days  | `P30D`                               |

Default: `PT1H`.

---

## Severity filter

`FluentSelect` (or `FluentListbox` multi-select) letting the user show only specific
severity levels. Values map to App Insights `severityLevel` (0–4):

| Label       | severityLevel |
| ----------- | ------------- |
| Verbose     | 0             |
| Information | 1             |
| Warning     | 2             |
| Error       | 3             |
| Critical    | 4             |

Default: all selected. When any level is deselected, a `where severityLevel in (…)`
clause is appended to the KQL generated by `BuildKql`. In raw KQL mode this filter is
hidden (user controls the query directly).

---

## Log table columns

Extend the existing `FluentDataGrid<LogEntry>` with explicit column definitions:

| Column    | Field           | Notes                                        |
| --------- | --------------- | -------------------------------------------- |
| Time      | `Timestamp`     | Formatted `HH:mm:ss.fff`, sortable           |
| Level     | `SeverityLevel` | Coloured badge chip                          |
| Message   | `Message`       | Truncated to ~120 chars with ellipsis        |
| Operation | `OperationId`   | Monospace, clickable → pre-fills trace query |

Virtualization: wrap items with `<Virtualize>` to keep rendering fast for large result
sets (matches Blazor pitfall **BL-3** in `docs/pitfalls/blazor-maui.md`).

---

## Details pane

When a row is selected, the existing `DetailsPane` populates with all `LogEntry` fields.
Add App Insights-specific properties: `OperationId`, `SeverityLevel`, `CustomDimensions`.

---

## Partial data warning

When `Provider` returns a partial result (indicated via a new `bool IsPartialResult`
property on the next query call, or by catching `LogsQueryResultStatus.Partial`):

> ⚠ Results may be incomplete — query exceeded the data limit. Narrow the time range or
> add a stricter filter.

Rendered as a `FluentMessageBar` of type `Warning` above the grid.

---

## Blazor patterns to follow

- `InvokeAsync(StateHasChanged)` for any state update triggered from async paths
  (pitfall **BL-2** in `docs/pitfalls/blazor-maui.md`).
- Don't bind `FluentDataGrid` directly to async observables; materialize to `List<LogEntry>`
  first.
- Cancel the previous query `CancellationTokenSource` before starting a new one to prevent
  stale result races.

---

## Tasks

- [ ] Add auth status bar to `ObservabilityPage.razor`.
  - Show `CredentialIdentity` on success; `LastAuthError` with retry on failure.
- [ ] Wire `TestConnectionAsync` into environment-change lifecycle in `ObservabilityPage.razor`.
- [ ] Replace `AppInsightsResourceId` text field in `ObservabilityConfigForm.razor` with discover combobox.
  - Inject `IAppInsightsDiscoveryService`.
  - Lazy-load resources on search box focus or Refresh click.
  - Debounce search input (300 ms) before calling `ListResourcesAsync(nameFilter)`.
  - Group results by subscription in the combobox dropdown.
  - Populate `AppInsightsResourceId` on selection; clear on deselect.
  - Show inline error when discovery returns empty due to auth failure.
  - Show truncation warning when result count is exactly 1 000.
- [ ] Add `TimeRangePicker` (FluentSelect) to the query toolbar.
- [ ] Add `SeverityFilter` (FluentSelect multi) to the query toolbar.
- [ ] Extend `BuildKql` (or apply filter in page) to include severity level clause.
- [ ] Define explicit `FluentDataGrid` column set (Timestamp, Level, Message, OperationId).
- [ ] Wrap log results list in `<Virtualize>`.
- [ ] Show partial-data warning when indicated by provider.
- [ ] Extend `DetailsPane` property mapping for App Insights-specific `LogEntry` fields.

---

## Acceptance checks

- [ ] Auth bar shows green + identity string after successful connection test.
- [ ] Auth bar shows red callout with error text when credentials are unavailable.
- [ ] Changing the environment re-runs the connection test.
- [ ] Resource picker loads App Insights list on demand (not on form open).
- [ ] Typing in the search box narrows the list via `nameFilter`.
- [ ] Selecting a resource populates the resource ID field and clears workspace ID.
- [ ] Discovery failure shows inline message; manual ID entry still works.
- [ ] Truncation warning appears when exactly 1 000 results are returned.
- [ ] Time range selector changes the query range and re-fetches.
- [ ] Severity filter limits returned rows (Verbose / Info / Warning / Error / Critical).
- [ ] Large result sets (>500 rows) scroll smoothly via `Virtualize`.
- [ ] Log row click populates the details pane with all fields.
- [ ] Partial-data warning appears when SDK indicates incomplete results.
- [ ] Saved config reloads correctly (resource ID persists, subscription grouping rebuilt).
