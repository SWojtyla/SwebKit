# Phase 3 — Observability Depth

**Status:** ⏳ Pending (starts after Phase 2 complete)
**Goal:** Full trace/correlation explorer, metrics dashboard with charts, OTLP as a first-class
provider, user-defined saved queries, and seamless cross-linking from Service Bus to logs.

---

## 1. Trace / Correlation Timeline View

**Component:** `Components/Observability/TraceTimeline.razor`

- [ ] Entry points:
  - "Find trace for OperationId" → from log entry right-click or command palette
  - "Find trace" link in correlation ID filter results
- [ ] Input bar: paste operation ID or correlation ID → `[Load Trace]`
- [ ] Waterfall visualization:
  - Horizontal timeline (time-aligned), spans rendered as colored bars
  - Hierarchy: nested spans indented under parent
  - Span kinds: Server (blue), Client (teal), Producer (orange), Consumer (purple), Internal (gray)
  - Errors: red bar + ⚠ icon
  - Duration label on bar
- [ ] Click span → right details pane shows:
  - Name, kind, status, start time, duration
  - All tags / attributes as key-value table
  - Events (span events with timestamp + message)
  - Exception (stack trace if status = Error)
- [ ] Zoom: horizontal scroll, zoom in/out with mouse wheel on timeline area
- [ ] Provider-agnostic: `TraceTimeline` domain model hides AppInsights vs OTLP differences

**AppInsights KQL for trace:**
```kql
let opId = "[OPERATION_ID]";
union requests, dependencies, exceptions, traces
| where operation_Id == opId
| order by timestamp asc
```
Map result rows to `TraceSpan` based on `itemType`.

---

## 2. Mini Metrics Dashboard

**Component:** `Components/Observability/MetricsDashboard.razor`

**Chart library:** `Blazor-ApexCharts` (`ApexCharts` NuGet package)

- [ ] Default tiles (5):
  1. **p95 Request Latency** — line chart, 1 data point per time bucket
  2. **Error Rate %** — area chart
  3. **Request Count** — bar chart per time bucket
  4. **Dependency Failure Rate** — line chart
  5. **Service Bus Queue Backlog** — grouped bar: active + DLQ per watched queue
- [ ] Time range selector: `[15m]` `[1h]` `[6h]` `[24h]` `[Custom]`
- [ ] Auto-refresh toggle: Off / 1m / 5m
- [ ] "Add tile" button → tile picker: list of metric names, chart type selector
- [ ] Tile drag-to-reorder (CSS grid, order stored in `ui-state.json`)
- [ ] Tile remove button (hover state)
- [ ] Empty tile state when metric query returns no data: "No data for this time range"

**Metric queries (AppInsights):**
- p95 latency: `requests | summarize percentile(duration, 95) by bin(timestamp, [bucket])`
- Error rate: `requests | summarize error=countif(success==false), total=count() by bin(timestamp, [bucket]) | extend rate = error/total*100`

---

## 3. Saved Queries Per Project+Environment

**Storage:** `profiles.json` → `ProjectEnvironment.SavedQueries: List<SavedQuery>`

- [ ] "Save query" button in log table filter bar: dialog → enter name → saves current filter state as KQL
- [ ] Saved queries dropdown in filter bar: built-in presets + user-saved (separated by divider)
- [ ] Built-in presets loaded as defaults (not persisted unless user explicitly saves changes)
- [ ] Right-click saved query → Rename, Delete, Edit
- [ ] Saved query manager: Settings → Observability → Saved Queries
- [ ] Quick-run from Command Palette: "Run query: [name]"

---

## 4. OTLP Provider (`SwebKit.OpenTelemetry`)

**File:** `src/SwebKit.OpenTelemetry/OtlpObservabilityProvider.cs`

- [ ] Implement `IObservabilityProvider` (Provider = OtlpEndpoint)
- [ ] `QueryLogsAsync`:
  - If OTLP endpoint is actually Azure Monitor (via OTLP ingestion), proxy to `LogsQueryClient`
  - If generic OTLP: fetch from OTLP-compatible query API (Jaeger, Tempo, etc.)
  - Normalize results to `List<LogEntry>`
- [ ] `GetTraceAsync`:
  - Fetch trace by operation ID from configured OTLP backend
  - Map to `TraceTimeline`
- [ ] `GetMetricsAsync`:
  - Fetch from Prometheus-compatible endpoint or Azure Monitor
- [ ] Config form in Settings: endpoint URL, headers (key-value), resource attributes
- [ ] "Test Connection" — send a minimal request to the endpoint

---

## 5. Cross-Linking Workflows

- [ ] **DLQ → Logs:** Message detail pane → `[Find logs for this CorrelationId →]` button
  - Opens new Observability tab, pre-filters by `CorrelationId = [value]`
- [ ] **Log entry → Trace:** Log table row right-click → "Find trace for this operation"
  - Opens Trace Timeline tab, pre-loaded with `operation_Id`
- [ ] **AKS pod → Logs (Phase 4 prerequisite):** Pod log view → "Find App Insights logs for this pod"
  - Opens Observability tab filtered by `cloud_RoleName = [deployment name]`
- [ ] Cross-links use `NavigationManager.NavigateTo("/observability?correlationId=abc")` with query params
  - Observability page reads query params on init and pre-fills filter bar

---

## 6. Query Builder (UI → KQL)

- [ ] "Builder" mode in log filter bar (default) vs "Raw KQL" mode toggle
- [ ] Builder mode renders form fields → generates KQL preview (read-only Monaco, `kql` language)
- [ ] KQL preview updates in real time as form fields change
- [ ] "Edit as KQL" button: copies generated KQL into editable Monaco, switches to raw mode
- [ ] Raw mode KQL runs directly (no field validation)

---

## 7. Export

- [ ] Log table: `[Copy selected rows as JSON]` → clipboard
- [ ] Log table: `[Export CSV]` → downloads file: Timestamp, Level, Message, OperationName, CorrelationId
- [ ] Trace timeline: `[Copy as JSON]` → copies `TraceTimeline` object to clipboard

---

## 8. Auth Error UX

- [ ] Auth failure: distinct error callout with:
  - "Authentication failed. Check credentials in Settings → [Project] → [Env] → Observability"
  - `[Open Settings]` button that navigates directly to the relevant settings section
- [ ] Token expiry: detect `AuthenticationFailedException` and prompt: "Re-authenticate?" with `[Sign In]` button
- [ ] Query timeout: `OperationCanceledException` → "Query timed out after 30s" + retry button

---

## Acceptance Criteria (Phase 3 Complete)

- [ ] Enter an operation ID → trace waterfall renders with correct hierarchy
- [ ] Metrics dashboard shows p95 latency and error rate for last 1h
- [ ] Tile auto-refreshes every 1 minute when toggle is on
- [ ] "Save query" saves current filter; reloading the page restores it from `profiles.json`
- [ ] Click "Find logs for this CorrelationId" on a DLQ message → observability tab opens, pre-filtered
- [ ] OTLP endpoint config: enter URL + headers → test connection succeeds against a test OTLP server
- [ ] Auth failure shows clear guidance and direct link to settings
