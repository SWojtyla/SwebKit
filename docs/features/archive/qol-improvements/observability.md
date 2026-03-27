# Observability QOL — Technical Implementation Plan

**Status:** Planned
**Parent:** [QOL Improvements Catalog](index.md)
**Architecture ref:** [Observability architecture](../../../architecture/functionalities/observability.md)

This document gives a concrete implementation plan for every Observability quality-of-life item (OBS-1 through OBS-15). Items are grouped by the same sections used in the catalog index.

---

## 1. Logs Tab

### OBS-1 — Monaco editor for KQL

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`, lines 39–44. Replace the `<textarea>` with a `<StandaloneCodeEditor>` from `BlazorMonaco`.
- `src/SwebKit.App/SwebKit.App.csproj` — add the `BlazorMonaco` NuGet package if not already present (check existing references first).
- `src/SwebKit.App/wwwroot/index.html` (or `_Imports.razor`) — add the Monaco loader script reference per BlazorMonaco docs if not already present.

**Technical approach:**

```razor
@* Replace the <textarea> block: *@
<StandaloneCodeEditor @ref="_editor"
                      Id="kql-editor"
                      ConstructionOptions="EditorOptions"
                      OnKeyDown="OnEditorKeyDown" />
```

```csharp
private StandaloneCodeEditor _editor = null!;

private StandaloneEditorConstructionOptions EditorOptions(StandaloneCodeEditor _) =>
    new()
    {
        Language = "sql",          // closest built-in; KQL grammar is addable via monarch
        Theme = "vs-dark",
        AutomaticLayout = true,
        MinimapEnabled = false,
        LineNumbers = "on",
        ScrollBeyondLastLine = false,
        FontSize = 13,
        Value = _query
    };
```

The `_query` field (line 122) must be kept in sync with the editor value. On each Run, read the current editor content:
```csharp
private async Task RunQueryAsync()
{
    _query = await _editor.GetValue();
    // ... existing logic
}
```

For `LoadQueryAsync` (line 143 — called from Drill-to-Logs), set the editor value programmatically:
```csharp
public async Task LoadQueryAsync(string kql)
{
    _query = kql;
    await _editor.SetValue(kql);
    await RunQueryAsync();
}
```

`OnEditorKeyDown` becomes a JS-side handler; use `@onkeydown` on the editor's container div or wire a Monaco action via `_editor.AddCommand(...)` for Ctrl+Enter.

**Keyboard shortcut for Ctrl+Enter:** Use `_editor.AddAction(new ActionDescriptor { Id = "run-query", Label = "Run Query", KeyBindings = [2048 | 3], Run = async _ => await RunQueryAsync() })` in `OnAfterRenderAsync` (first render only). The key code `2048 | 3` is `CtrlCmd | Enter` in Monaco's enum.

**Dependencies:** BlazorMonaco NuGet package. If AKS-10 is also planned, both share this dependency — install once.

**Risk:** Medium. Monaco requires the JS loader bundle (~2 MB). In a MAUI Blazor Hybrid app the bundle is served from the local file system, so network latency is not a concern, but the initial parse/JIT of Monaco's JS happens on the UI thread. Test editor startup time. If sluggish, defer Monaco initialization with a loading placeholder.

---

### OBS-2 — Query validation feedback inline

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor` — in `RunQueryAsync()` (line 164), the `catch (Exception ex)` block currently sets `_error = ex.Message` which feeds into `<ErrorCallout>`. Instead, distinguish query parse errors from connectivity errors. Add a separate `_queryError` string for inline display directly below the editor.
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs` — the Azure Monitor `LogsQueryClient` returns a `LogsQueryException` for malformed KQL. Catch `LogsQueryException` specifically and extract the inner error detail from its `Status` property or `Message`.

**Technical approach:**

In `RunQueryAsync`:
```csharp
catch (Azure.RequestFailedException rfe) when (rfe.ErrorCode == "BadArgumentError")
{
    _queryError = rfe.Message; // KQL parse error from Azure Monitor
}
catch (OperationCanceledException) { }
catch (Exception ex) { _error = ex.Message; } // connectivity / auth error
```

In the Razor template, replace the single `<ErrorCallout>` block with:
```razor
@if (_queryError is not null)
{
    <div class="obs-query-error">
        <FluentIcon Value="@(new Icons.Regular.Size16.Warning())" />
        @_queryError
    </div>
}
@if (_error is not null)
{
    <ErrorCallout Message="@_error" />
}
```
`.obs-query-error` is styled inline below the editor (amber border-left, small font) so it reads as a code-level feedback rather than a full error card.

**Dependencies:** OBS-1 (Monaco editor). The query error div sits below the editor in the layout. Can be implemented independently with the textarea editor if OBS-1 is deferred.

**Risk:** Low. The `BadArgumentError` error code may vary by Azure Monitor API version. Test with a deliberately broken KQL string (`traces | where nonexistentFunction()`) and log the actual error code to confirm.

---

### OBS-3 — Saved query folders

**Priority:** Medium

**What to change:**
- `src/SwebKit.Core/Domain/ObservabilityConfig.cs` — `SavedQuery.Name` is already a plain string. No model change required. Folder is implicit in the name via a `"folder/name"` prefix convention.
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor` — the saved queries sidebar (lines 18–34) currently renders a flat list. Group by the folder prefix:

```csharp
private IEnumerable<IGrouping<string, SavedQuery>> GroupedSavedQueries =>
    _savedQueries
        .GroupBy(sq => sq.Name.Contains('/') ? sq.Name[..sq.Name.IndexOf('/')] : string.Empty)
        .OrderBy(g => g.Key);

private static string DisplayName(SavedQuery sq) =>
    sq.Name.Contains('/') ? sq.Name[(sq.Name.IndexOf('/') + 1)..] : sq.Name;
```

Render in the sidebar:
```razor
@foreach (var group in GroupedSavedQueries)
{
    @if (!string.IsNullOrEmpty(group.Key))
    {
        <div class="obs-folder-header">📁 @group.Key</div>
    }
    @foreach (var sq in group)
    {
        <div class="obs-saved-query-row">
            <button class="obs-preset-item" @onclick="() => LoadSaved(sq)">@DisplayName(sq)</button>
            <button class="obs-saved-query-delete" @onclick="() => DeleteSaved(sq)">✕</button>
        </div>
    }
}
```

- In the Save Query dialog (`_saveQueryName` input, line 104), update the placeholder to `"folder/name or just name"` to hint at the convention.

**Dependencies:** None.

**Risk:** Low. Existing saved queries without a `/` in their name fall into the ungrouped bucket and render as before.

---

### OBS-4 — Export to JSON/CSV file download

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor` — the "Copy CSV" button (line 53). Change it to a split button or add a dropdown with "Copy CSV", "Download CSV", and "Download JSON".
- `src/SwebKit.App/wwwroot/js/app.js` — add a `SwebKit.downloadFile(filename, mimeType, base64Content)` helper that creates an `<a>` element with a `data:` URL and triggers a click (standard Blazor file download pattern).

**Technical approach:**

```csharp
private async Task DownloadCsvAsync()
{
    if (_result is null) return;
    var csv = BuildCsv(); // reuse CopyCsvAsync logic but return the string
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
    var b64 = Convert.ToBase64String(bytes);
    await JS.InvokeVoidAsync("SwebKit.downloadFile", "query-results.csv", "text/csv", b64);
}

private async Task DownloadJsonAsync()
{
    if (_result is null) return;
    var rows = _result.Rows.Select(r =>
        _result.ColumnNames.ToDictionary(c => c, c => GetCell(r, c)));
    var json = System.Text.Json.JsonSerializer.Serialize(rows,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    await JS.InvokeVoidAsync("SwebKit.downloadFile", "query-results.json", "application/json", b64);
}
```

JS helper:
```js
SwebKit.downloadFile = (filename, mimeType, base64) => {
    const a = document.createElement('a');
    a.href = `data:${mimeType};base64,${base64}`;
    a.download = filename;
    a.click();
};
```

**Dependencies:** None. `JS` is already injected (line 113).

**Risk:** Low. MAUI Blazor Hybrid runs inside a WebView; `data:` URI downloads trigger a file-save dialog on Windows. Test that the dialog appears correctly. For very large result sets (>50 000 rows) the base64 encoding may be slow — acceptable given the `MaxRowsPerQuery` cap (default 500).

---

### OBS-5 — MaxRowsPerQuery in Settings UI

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`, around line 113 (the Observability settings card). Add a labeled numeric input:
  ```razor
  <FluentNumberField Label="Max rows per query"
                     @bind-Value="Config.ObservabilityConfig.MaxRowsPerQuery"
                     Min="50" Max="5000" Step="50" />
  ```
- `src/SwebKit.Core/Domain/ObservabilityConfig.cs` — the `MaxRowsPerQuery` property already exists (default 500). No model change.
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor` — `MaxRows` is already a `[Parameter]` (line 118) passed from `ObservabilityPage`. `ObservabilityPage` must read `AppState.Config.ObservabilityConfig.MaxRowsPerQuery` and pass it through. Verify this wiring exists; add if missing.

**Dependencies:** None.

**Risk:** Low. The only risk is a user setting a very high value (e.g. 5 000) and incurring Azure Monitor cost and slow queries. Document the cost implication in the Settings tooltip.

---

## 2. Overview & Charts

### OBS-6 — Performance tab trend charts (P50/P95/P99)

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityPerformance.razor` — the detail panel (lines 45–65) currently shows static stat cards for P50/P95/P99. Replace or augment with an ApexCharts line chart showing latency over time.
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs` — add a new method:
  ```csharp
  Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(
      string operationName, TimeRange range, CancellationToken ct = default);
  ```
- `src/SwebKit.Core/Models/ObservabilityModels.cs` — add:
  ```csharp
  public record LatencyDataPoint(DateTimeOffset Timestamp, double P50Ms, double P95Ms, double P99Ms);
  ```
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs` — implement the new method. KQL skeleton:
  ```kql
  requests
  | where name == '{operationName}'
  | summarize P50=percentile(duration, 50),
              P95=percentile(duration, 95),
              P99=percentile(duration, 99)
              by bin(timestamp, {binSize})
  | order by timestamp asc
  ```
  `binSize` derived from `TimeRange` (e.g. 5m for Last1Hour, 1h for Last7d).
- `src/SwebKit.Core/Services/DemoObservabilityProvider.cs` — implement with synthetic time-series data.
- `src/SwebKit.App/Components/Observability/ObservabilityPerformance.razor` — in `SelectException` / selection handler, after setting `_selected`, call `LoadTrendAsync()`:
  ```csharp
  private async Task LoadTrendAsync()
  {
      if (_selected is null || Provider is null) return;
      _trendLoading = true;
      StateHasChanged();
      _trend = await Provider.GetOperationLatencyTrendAsync(_selected.OperationName, Range, _cts.Token);
      _trendLoading = false;
      StateHasChanged();
  }
  ```
  Render the chart in the detail panel:
  ```razor
  <ApexChart TItem="LatencyDataPoint" Title="Latency trend">
      <ApexPointSeries TItem="LatencyDataPoint" Items="_trend"
                       Name="P50" XValue="@(x => x.Timestamp.ToUnixTimeMilliseconds())"
                       YValue="@(x => (decimal)x.P50Ms)" SeriesType="SeriesType.Line" />
      <ApexPointSeries TItem="LatencyDataPoint" Items="_trend"
                       Name="P95" XValue="@(x => x.Timestamp.ToUnixTimeMilliseconds())"
                       YValue="@(x => (decimal)x.P95Ms)" SeriesType="SeriesType.Line" />
      <ApexPointSeries TItem="LatencyDataPoint" Items="_trend"
                       Name="P99" XValue="@(x => x.Timestamp.ToUnixTimeMilliseconds())"
                       YValue="@(x => (decimal)x.P99Ms)" SeriesType="SeriesType.Line" />
  </ApexChart>
  ```

**Dependencies:** `Blazor-ApexCharts` NuGet already in the tech stack. `IObservabilityProvider` interface change requires updating both `AzureAppInsightsProvider` and `DemoObservabilityProvider`.

**Risk:** Medium. This adds a new KQL query per selected operation — one additional Azure Monitor call each time the user selects a row. Mitigate by only loading the trend after a 300 ms debounce on selection change, and cancelling the previous CTS.

---

### OBS-7 — Auto-refresh toggle with "last updated" timestamp

**Priority:** Medium

**What to change:**
- The Observability page entry point. Since `ObservabilityPage.razor` does not exist as a file (the file read failed), locate the main page component. Check `src/SwebKit.App/Components/Pages/` for an observability page, or look for the component that renders `ObservabilityLogs`, `ObservabilityPerformance`, etc. Based on the architecture doc, it is `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`. Verify the path and add to it.
- Add to the toolbar: an auto-refresh toggle button (similar to `AutoRefreshToggle.razor` in AKS) and a "Last updated: HH:MM:SS" label.

**Technical approach:**

In `ObservabilityPage.razor`:
```csharp
private bool _autoRefresh;
private int _autoRefreshMinutes = 2;
private DateTimeOffset? _lastUpdated;
private CancellationTokenSource? _autoRefreshCts;

private void ToggleAutoRefresh()
{
    _autoRefresh = !_autoRefresh;
    if (_autoRefresh) StartAutoRefresh();
    else StopAutoRefresh();
}

private void StartAutoRefresh()
{
    _autoRefreshCts = new CancellationTokenSource();
    _ = RunAutoRefreshLoopAsync(_autoRefreshCts.Token);
}

private async Task RunAutoRefreshLoopAsync(CancellationToken ct)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_autoRefreshMinutes));
    while (await timer.WaitForNextTickAsync(ct))
    {
        await InvokeAsync(async () =>
        {
            await RefreshActiveTabAsync();
            _lastUpdated = DateTimeOffset.Now;
            StateHasChanged();
        });
    }
}
```

Toolbar Razor:
```razor
<button class="obs-auto-refresh-btn @(_autoRefresh ? "active" : "")" @onclick="ToggleAutoRefresh">
    Auto-refresh
</button>
@if (_autoRefresh)
{
    <select @bind="_autoRefreshMinutes" @bind:after="RestartAutoRefresh">
        <option value="1">1 min</option>
        <option value="2">2 min</option>
        <option value="5">5 min</option>
    </select>
}
@if (_lastUpdated.HasValue)
{
    <span class="obs-last-updated">Updated @_lastUpdated.Value.ToLocalTime().ToString("HH:mm:ss")</span>
}
```

**Dependencies:** None.

**Risk:** Low. Azure Monitor has 1–5 minute ingestion lag, so refreshes more frequent than 1 minute are not useful — enforce a minimum of 1 minute in the dropdown.

---

### OBS-8 — Local timezone display

**Priority:** Medium

**What to change:**
- All observability chart and table components that show timestamps: `ObservabilityFailures.razor` (lines 204–208, `FormatTimestamp`), `ObservabilityAvailability.razor` (line 47, 139–142), `ObservabilityPerformance.razor`.
- `ObservabilityFailures.razor` line 207: `dto.ToLocalTime().ToString("HH:mm:ss")` — this already calls `ToLocalTime()`. However the source `DateTimeOffset` may be UTC-only (no offset info) from the KQL result. The fix is to get the browser's timezone offset via JS and apply it.

**Technical approach:**

Add a shared JS helper to `app.js`:
```js
SwebKit.getBrowserTimezoneOffset = () => -new Date().getTimezoneOffset(); // minutes ahead of UTC
```

In `ObservabilityPage.razor` (or a shared `ObservabilityTimeHelper` service), read the offset once on init:
```csharp
private int _tzOffsetMinutes;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _tzOffsetMinutes = await JS.InvokeAsync<int>("SwebKit.getBrowserTimezoneOffset");
        StateHasChanged();
    }
}
```

Pass `_tzOffsetMinutes` as a `[Parameter]` to each sub-component. Each component applies it:
```csharp
private DateTimeOffset ToLocal(DateTimeOffset utc) =>
    utc.ToOffset(TimeSpan.FromMinutes(TzOffsetMinutes));
```

Alternatively, cascade `_tzOffsetMinutes` via a `CascadingValue<int>` named `"TzOffset"` from `ObservabilityPage` to all child components. This avoids adding a parameter to each component individually.

**Dependencies:** None. `IJSRuntime` is already available in the page.

**Risk:** Low. If JS interop fails (unlikely in MAUI Blazor), fall back to `TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)` which reads the OS timezone.

---

### OBS-9 — User-configurable metric thresholds

**Priority:** Low

**What to change:**
- `src/SwebKit.Core/Domain/ObservabilityConfig.cs` — add threshold properties:
  ```csharp
  /// <summary>Failure rate above which the metric is shown red (0.0–1.0). Default 5%.</summary>
  public double FailureRateRedThreshold { get; set; } = 0.05;
  /// <summary>Failure rate above which the metric is shown amber. Default 1%.</summary>
  public double FailureRateAmberThreshold { get; set; } = 0.01;
  /// <summary>P95 latency (ms) above which the metric is red. Default 2000.</summary>
  public double LatencyRedThresholdMs { get; set; } = 2000;
  /// <summary>P95 latency (ms) above which the metric is amber. Default 500.</summary>
  public double LatencyAmberThresholdMs { get; set; } = 500;
  ```
- `src/SwebKit.App/Components/Observability/ObservabilityPerformance.razor` — the `FailClass()` method (line 124) and `P95Class()` method (line 131) use hardcoded thresholds. Add `[Parameter] public ObservabilityConfig Config` and replace the literals:
  ```csharp
  private string FailClass(double rate)
  {
      if (rate > Config.FailureRateRedThreshold) return "obs-red";
      if (rate > Config.FailureRateAmberThreshold) return "obs-amber";
      return "obs-green";
  }
  ```
- `src/SwebKit.App/Components/Observability/ObservabilityOverview.razor` — apply the same pattern for its status color methods.
- `src/SwebKit.App/Components/Pages/SettingsPage.razor` — add threshold inputs to the Observability settings card (numeric fields with `%` and `ms` unit labels).

**Dependencies:** None.

**Risk:** Low. If `Config` is null (demo mode or missing config), fall back to hardcoded defaults.

---

## 3. Discovery & Resource Selection

### OBS-10 — Subscription scan progress counter

**Priority:** Medium

**What to change:**
- `src/SwebKit.Observability/AppInsightsDiscoveryService.cs` — the `DiscoverResourcesAsync` method yields `ObservabilityResourceInfo` items. Change the return type from `IAsyncEnumerable<ObservabilityResourceInfo>` to a discriminated union or a wrapper type. Simpler approach: yield a progress sentinel before each subscription scan.

Add a `DiscoveryProgressEvent` record:
```csharp
public record DiscoveryProgressEvent(int Current, int Total);
```

Change `IObservabilityResourceDiscovery.DiscoverResourcesAsync` to yield `DiscoveryEvent` (a union type):
```csharp
// In IObservabilityProvider.cs:
public abstract class DiscoveryItem { }
public sealed class DiscoveryResourceItem(ObservabilityResourceInfo Resource) : DiscoveryItem;
public sealed class DiscoveryProgressItem(int Current, int Total) : DiscoveryItem;
```

`AppInsightsDiscoveryService` yields a `DiscoveryProgressItem` before each subscription:
```csharp
int idx = 0;
await foreach (var sub in _armClient.GetSubscriptions().GetAllAsync(ct))
{
    yield return new DiscoveryProgressItem(++idx, totalSubs);
    // then yield resources for this sub...
}
```

Getting `totalSubs` requires enumerating subscriptions once first. To avoid a double-pass, emit progress without a known total: `"Scanning subscription 3…"` (no denominator). Alternatively, list all subscription IDs first (cheap ARM call) to get the count.

- `src/SwebKit.App/Components/Observability/ResourceSelectorDialog.razor` — in `StartDiscoveryAsync()` (line 87), handle the new union:
  ```csharp
  await foreach (var item in Discovery.DiscoverResourcesAsync(_cts.Token))
  {
      if (item is DiscoveryProgressItem p)
      {
          _scanProgress = $"Scanning {p.Current} / {p.Total} subscriptions…";
      }
      else if (item is DiscoveryResourceItem r)
      {
          _resources.Add(r.Resource);
      }
      await InvokeAsync(StateHasChanged);
  }
  ```
  Render `_scanProgress` in the loading state:
  ```razor
  <div class="resource-selector-loading">
      <FluentProgressRing /> @(_scanProgress ?? "Scanning subscriptions…")
  </div>
  ```

**Dependencies:** This is a breaking change to `IObservabilityResourceDiscovery`. Update both `AppInsightsDiscoveryService` and `DemoObservabilityProvider` (which also implements discovery).

**Risk:** Medium. The interface change cascades to the demo provider and any future providers. Consider keeping the interface backward-compatible by adding a separate `IAsyncEnumerable<DiscoveryProgressItem> GetProgressAsync()` method that runs in parallel, rather than changing the resource stream.

---

### OBS-11 — Resource type badge (Classic vs Workspace-based)

**Priority:** Medium

**What to change:**
- `src/SwebKit.Core/Models/ObservabilityModels.cs` — add to `ObservabilityResourceInfo`:
  ```csharp
  public string? WorkspaceType { get; init; } // "Classic" | "Workspace-based" | null
  ```
- `src/SwebKit.Observability/AppInsightsDiscoveryService.cs` — when building `ObservabilityResourceInfo` from the ARM resource, inspect `component.Data.IngestionMode`:
  - `ApplicationInsightsIngestionMode.ApplicationInsights` → "Classic"
  - `ApplicationInsightsIngestionMode.LogAnalytics` → "Workspace-based"
  Map to the `WorkspaceType` string.
- `src/SwebKit.App/Components/Observability/ResourceSelectorDialog.razor` — in the resource item render block (line 35–40), add a badge after the resource name:
  ```razor
  <div class="resource-item-name">
      <FluentIcon Value="@(new Icons.Regular.Size16.DataTrending())" />
      @r.Name
      @if (r.WorkspaceType is not null)
      {
          <span class="resource-type-badge @(r.WorkspaceType == "Classic" ? "badge-classic" : "badge-workspace")">
              @r.WorkspaceType
          </span>
      }
  </div>
  ```
  Add `.resource-type-badge`, `.badge-classic`, `.badge-workspace` to `app.css`.

**Dependencies:** `Azure.ResourceManager.ApplicationInsights` beta package must expose `IngestionMode`. Verify the property exists on `ApplicationInsightsComponentData`. If not, inspect `component.Data.AdditionalProperties` or the raw ARM JSON.

**Risk:** Low. The badge is display-only. If the property is unavailable from the ARM SDK, leave `WorkspaceType` null and show no badge.

---

### OBS-12 — Demo mode isolated resource profiles

**Priority:** Low

**What to change:**
- `src/SwebKit.Core/Services/DemoObservabilityProvider.cs` — the current demo provider returns a single fixed dataset. Add a `DemoProfile` enum:
  ```csharp
  public enum DemoProfile { HighTraffic, QuietService, ErrorSpike }
  ```
  And a `[Parameter]` or constructor argument to select the profile. Three distinct data shapes:
  - **HighTraffic:** 150 req/h average, P95 ~300ms, ~2% failure rate, many log lines.
  - **QuietService:** 3 req/h, P95 ~50ms, 0% failures, sparse logs.
  - **ErrorSpike:** 30 req/h, P95 ~2500ms, ~25% failure rate, exceptions dominating.

- `src/SwebKit.App/Components/Observability/ResourceSelectorDialog.razor` — in demo mode, `DemoObservabilityProvider` registers three fake resources in its `DiscoverResourcesAsync`:
  ```csharp
  yield return new DiscoveryResourceItem(new ObservabilityResourceInfo
  {
      Name = "demo-high-traffic",
      ResourceGroup = "demo",
      SubscriptionName = "Demo Subscription",
      ResourceId = "demo://high-traffic",
      WorkspaceType = "Workspace-based"
  });
  // ... quiet-service, error-spike
  ```
  When `ActivateResourceAsync("demo://high-traffic")` is called, the provider switches to the `HighTraffic` profile seed data.

**Dependencies:** OBS-11 (adds `WorkspaceType` to `ObservabilityResourceInfo`, used by demo resources too).

**Risk:** Low. Demo providers are self-contained. The only complexity is maintaining three distinct seed datasets.

---

## 4. Failures & Traces

### OBS-13 — Trace ID drill-down link in Failures tab

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityFailures.razor` — the "Recent Occurrences" section (lines 72–89) renders `operationId` in each sample row (line 85). Add a "View trace" button for rows where `operationId` is non-empty.

In the sample row render:
```razor
<div class="obs-sample-row">
    <span class="obs-sample-time">@FormatTimestamp(row)</span>
    <span class="obs-sample-op">@GetCol(row, "operationName")</span>
    <span class="obs-sample-id">@GetCol(row, "operationId")</span>
    @{
        var opId = GetCol(row, "operationId");
    }
    @if (!string.IsNullOrEmpty(opId))
    {
        <button class="obs-trace-link" @onclick="() => DrillToTrace(opId)" title="View trace for @opId">
            → Trace
        </button>
    }
</div>
```

Add `DrillToTrace`:
```csharp
private async Task DrillToTrace(string operationId)
{
    var kql = $"union *\n| where operation_Id == '{operationId}'\n| order by timestamp asc\n| take 200";
    await OnDrillToLogs.InvokeAsync(kql);
}
```

Also add a trace drill link from the exception detail header when `_selected.SampleOperationId` is available. Add `string? SampleOperationId` to `ExceptionGroup` in `ObservabilityModels.cs`, populated from the first sample's `operationId` in `GetTopExceptionsAsync`.

- `src/SwebKit.Observability/KqlPresets.cs` — add a `TraceByOperationId(string opId)` preset builder that returns the KQL above. Use it in both `DrillToTrace` and `DrillToLogs` for consistency.

**Dependencies:** `OnDrillToLogs` EventCallback already exists (line 105). The Logs tab's `LoadQueryAsync` already handles externally-provided KQL (line 143).

**Risk:** Low. The KQL `union *` is a broad cross-table query — it scans all tables. In large workspaces this can be slow and costly. Document the cost implication in a tooltip on the button. Alternatively scope to `traces | union exceptions | union requests` for a more targeted query.

---

### OBS-14 — Copy feedback toast on stack trace copy

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityFailures.razor` — `CopyStackAsync()` method (line 176):

Current:
```csharp
private async Task CopyStackAsync()
{
    if (_selected?.SampleStackTrace is null) return;
    await JS.InvokeVoidAsync("navigator.clipboard.writeText", _selected.SampleStackTrace);
}
```

Add `@inject INotificationService Notifications` to the component (it currently only injects `IJSRuntime`), then:
```csharp
private async Task CopyStackAsync()
{
    if (_selected?.SampleStackTrace is null) return;
    await JS.InvokeVoidAsync("navigator.clipboard.writeText", _selected.SampleStackTrace);
    await Notifications.ShowSuccessAsync("Stack trace copied to clipboard");
}
```

**Dependencies:** `INotificationService` must be registered in DI (verify in `MauiProgram.cs`).

**Risk:** Low. One-line change after injection is added.

---

### OBS-15 — Availability heatmap

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Observability/ObservabilityAvailability.razor` — replace (or supplement) the flat `FluentDataGrid` list with an ApexCharts heatmap.
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs` — the existing `GetAvailabilityAsync` returns `IReadOnlyList<AvailabilityResult>`. No interface change needed — the data is reshaped in the component.
- `src/SwebKit.Core/Models/ObservabilityModels.cs` — `AvailabilityResult` already has `TestName`, `Location`, `Timestamp`, `Success`, `DurationMs`. The heatmap needs a time-bucketed structure: rows = test/location combos, columns = time buckets.

**Technical approach:**

In `ObservabilityAvailability.razor`, compute the heatmap data from `_results`:
```csharp
// Group results into hourly/daily buckets depending on TimeRange
private IEnumerable<ApexChartSeries<HeatCell>> BuildHeatmapSeries()
{
    var bucketSize = Range == TimeRange.Last1Hour ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1);
    var groups = _results.GroupBy(r => $"{r.TestName} / {r.Location}");
    foreach (var g in groups)
    {
        yield return new ApexChartSeries<HeatCell>
        {
            Name = g.Key,
            Data = g.GroupBy(r => BucketOf(r.Timestamp, bucketSize))
                    .Select(b => new HeatCell(
                        b.Key,
                        (int)(b.Count(r => r.Success) * 100.0 / b.Count())))
                    .ToList()
        };
    }
}
```

Render:
```razor
@if (_showHeatmap && _results.Any())
{
    <ApexChart TItem="HeatCell" ChartType="ChartType.Heatmap" Title="Availability by time">
        @foreach (var series in BuildHeatmapSeries())
        {
            <ApexPointSeries TItem="HeatCell" Items="series.Data"
                             Name="@series.Name"
                             XValue="@(c => c.Bucket.ToString("HH:mm"))"
                             YValue="@(c => c.PassPct)" />
        }
    </ApexChart>
}
```

Add a toggle button `"⬚ Heatmap"` / `"☰ List"` to switch between views.

**Dependencies:** `Blazor-ApexCharts` already in the tech stack. OBS-6 uses it too — implement OBS-6 first to validate the ApexCharts integration pattern in this codebase.

**Risk:** Medium. The heatmap requires reshaping `AvailabilityResult` data into a 2D grid structure. For time ranges over 7 days with many test/location combos, the cell count can be large — cap at 24 buckets (daily) for the Last30d range. Test with the demo provider first.

---

## Implementation Order

**Wave 1 — Foundation (unblocks the most work)**
1. **OBS-1** — Monaco editor for KQL. Installs BlazorMonaco (also needed by AKS-10). All subsequent Logs tab items build on this editor.
2. **OBS-5** — MaxRowsPerQuery in Settings UI. Trivial config wiring; do with OBS-1 since both touch the Logs tab area.
3. **OBS-8** — Local timezone display. Low effort; affects timestamps across all tabs. Add the JS helper and cascade the offset value before building charts (OBS-6, OBS-15).

**Wave 2 — High-priority features**
4. **OBS-6** — Performance trend charts. Requires `IObservabilityProvider` interface extension — do this before OBS-13 which also touches the provider interface (ExceptionGroup model). Validates the ApexCharts integration pattern for OBS-15.
5. **OBS-2** — Query validation feedback. Depends on OBS-1 (editor layout shift); implement in the same PR or immediately after.
6. **OBS-3** — Saved query folders. Pure UI reshaping of existing data; no backend changes.

**Wave 3 — Medium-priority**
7. **OBS-7** — Auto-refresh toggle. Needs the main page component; straightforward timer loop.
8. **OBS-10** — Subscription scan progress. Requires `IObservabilityResourceDiscovery` interface change — coordinate with any work touching that interface.
9. **OBS-11** — Resource type badge. Requires ARM SDK property check; do after OBS-10 (both touch `AppInsightsDiscoveryService`).
10. **OBS-13** — Trace ID drill-down. Small addition to `ObservabilityFailures` and `KqlPresets`; self-contained once OBS-1 is done (uses `LoadQueryAsync`).
11. **OBS-14** — Copy stack trace toast. One-line change; batch with OBS-13 (same file).

**Wave 4 — Low-priority / polish**
12. **OBS-4** — Export to JSON/CSV. Standalone; add JS helper and buttons.
13. **OBS-9** — Configurable metric thresholds. Config model + Settings UI + two component updates.
14. **OBS-12** — Demo mode profiles. Isolated to `DemoObservabilityProvider`; do after OBS-11 (uses `WorkspaceType` in fake resources).
15. **OBS-15** — Availability heatmap. Do last — depends on OBS-6 (ApexCharts pattern) and OBS-8 (timezone-aware timestamps in chart labels).
