# AKS QOL — Technical Implementation Plan

**Status:** Planned
**Parent:** [QOL Improvements Catalog](index.md)
**Architecture ref:** [AKS architecture](../../../architecture/functionalities/aks.md)

This document gives a concrete implementation plan for every AKS quality-of-life item (AKS-1 through AKS-21). Items are grouped by the same sections used in the catalog index.

---

## 1. Log Streaming

### AKS-1 — Container selector in PodLogView

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Aks/PodLogView.razor` — add a `[Parameter] public PodInfo? Pod { get; set; }` (or a `List<string> Containers` parameter) alongside the existing `PodName` parameter. Add a `<select>` dropdown in the `.log-toolbar` div (line 6) that lists container names and binds to a `_selectedContainer` field.
- `src/SwebKit.Core/Models/AksModels.cs` — verify `PodInfo` already carries a `Containers` property (`IReadOnlyList<ContainerStatus>`). If only names are needed, expose `ContainerNames` as a computed property.
- `src/SwebKit.App/Components/Pages/AksPage.razor` — wherever `<PodLogView>` is rendered, pass the currently selected `PodInfo` (already in scope as `SelectedPod` / `CtxPod`) so the component can populate the dropdown.

**Technical approach:**
```razor
<!-- PodLogView.razor, log-toolbar -->
@if (Containers.Count > 1)
{
    <select class="log-container-select"
            value="@_selectedContainer"
            @onchange="OnContainerChanged">
        @foreach (var c in Containers)
        {
            <option value="@c">@c</option>
        }
    </select>
}
```
`_selectedContainer` defaults to `Containers.FirstOrDefault()`. `OnContainerChanged` cancels the current CTS, sets `_selectedContainer`, and calls `StartTailingAsync()`. The `container` local variable at line 85 of `PodLogView.razor` (currently `string.Empty`) is replaced with `_selectedContainer`.

**Dependencies:** None.

**Risk:** Low. `IAksClient.StreamPodLogsAsync` already accepts a container name parameter; the field is just not wired from the UI.

---

### AKS-2 — Scroll-to-bottom on new lines with tail toggle

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Aks/PodLogView.razor` — add a `_tailEnabled` bool (default `true`). After each render batch (in `StreamLogsAsync`, after `InvokeAsync(StateHasChanged)`) call a JS helper when `_tailEnabled` is true.
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` — same pattern.
- `src/SwebKit.App/wwwroot/js/app.js` (or `keyboardShortcuts.js`) — add `SwebKit.scrollToBottom(el)` that sets `el.scrollTop = el.scrollHeight`.
- Add a "Tail" toggle button to the `.log-toolbar` div in both components; clicking it toggles `_tailEnabled` and, if re-enabled, immediately scrolls to bottom.

**Technical approach:**
```csharp
// after StateHasChanged in StreamLogsAsync:
if (_tailEnabled)
    await JS.InvokeVoidAsync("SwebKit.scrollToBottom", _logContainer);
```
To detect manual scroll-up, wire `@onscroll="OnLogScroll"` on `.log-output`. If `scrollTop + clientHeight < scrollHeight - 50`, set `_tailEnabled = false`. Re-enabling via the toggle snaps back.

**Dependencies:** Requires `IJSRuntime` injection — PodLogView currently injects `IAppEventBus` and `IAsyncDisposable` but not JS. Add `@inject IJSRuntime JS`.

**Risk:** Low. The `ElementReference _logContainer` is already defined (line 48 of `PodLogView.razor`); only the JS call and toggle UI need adding.

---

### AKS-3 — Multi-pod log ordering with timestamp merge toggle

**Priority:** Medium

**What to change:**
- `src/SwebKit.Core/Models/AksModels.cs` — add `DateTimeOffset? Timestamp` to `AggregatedLogLine`. Parse from the log line if it follows ISO 8601 or RFC 3339 format using a regex (first ~30 chars); leave null for unparseable lines.
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` — in `StreamDeploymentLogsAsync`, set `Timestamps = true` in `LogStreamOptions` when passed (add an `IncludeTimestamps` bool to `LogStreamOptions`). Strip the timestamp prefix from the displayed line but preserve it in `AggregatedLogLine.Timestamp`.
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` — add a `_mergeSort` bool toggle. When true, `FilteredLines` sorts the current buffer by `Timestamp` before rendering. Add a "⏱ Sort by time" toggle button to the `.log-toolbar`.

**Technical approach:**

The sort should only apply to the in-memory snapshot, not the live stream. On each render tick, copy `Lines` to a sorted list only when `_mergeSort` is on:
```csharp
private IReadOnlyList<AggregatedLogLine> FilteredLines
{
    get
    {
        var src = Lines.TakeLast(500);
        if (!string.IsNullOrEmpty(TextFilter))
            src = src.Where(l => l.Line.Contains(TextFilter, ...));
        return _mergeSort
            ? src.OrderBy(l => l.Timestamp ?? DateTimeOffset.MaxValue).ToList()
            : src.ToList();
    }
}
```
Show timestamp prefix in the rendered line only when `_mergeSort` is enabled.

**Dependencies:** AKS-2 (both touch the same render loop in MultiPodLogView); implement independently but test together.

**Risk:** Medium. Enabling `Timestamps = true` in the K8s streaming API adds a prefix to each line that must be stripped before display. Regex parsing is needed. Lines without parseable timestamps sort last.

---

### AKS-4 — Log buffer size configurable in AksConfig

**Priority:** Medium

**What to change:**
- `src/SwebKit.Core/Domain/AksConfig.cs` — add `public int LogBufferSize { get; set; } = 10_000;` with a comment indicating valid range 1 000–100 000.
- `src/SwebKit.App/Components/Aks/PodLogView.razor` line 90 — replace `10_000` with `BufferSize` parameter.
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` line 108 — same, replace `10000`.
- `src/SwebKit.App/Components/Aks/PodLogView.razor` — add `[Parameter] public int BufferSize { get; set; } = 10_000;`. Callers in `AksPage.razor` pass `AppState.Config.AksConfig?.LogBufferSize ?? 10_000`.
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor` — add a labeled `<select>` with options 5 000 / 10 000 / 50 000 bound to `AksConfig.LogBufferSize`.

**Dependencies:** None.

**Risk:** Low. Memory risk if user sets 100 000 and receives high-volume logs — constrain the UI dropdown to 50 000 max.

---

### AKS-5 — Pause log tail button

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Aks/PodLogView.razor` — add `_paused` bool. Add a "⏸ Pause" / "▶ Resume" button to `.log-toolbar`. When paused: the stream continues to write to `Lines` but `StateHasChanged` is not called (so the UI freezes). When resumed: call `StateHasChanged` once and re-enable auto-scroll.
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` — same.

**Technical approach:**
```csharp
// In StreamLogsAsync, replace InvokeAsync(StateHasChanged) with:
if (!_paused)
    await InvokeAsync(StateHasChanged);
```
The pause button sets `_paused = true`; resume sets it to `false` and invokes `StateHasChanged`. This is distinct from the "Live" toggle (which stops the stream entirely). The "Pause" button is only visible when `IsLive` is true.

Note: the buffer still fills while paused. If the buffer reaches `BufferSize`, oldest lines are still evicted. A future enhancement could freeze eviction too, but that is out of scope here.

**Dependencies:** AKS-4 (buffer size); AKS-2 (tail toggle — pause should also disable auto-scroll).

**Risk:** Low.

---

## 2. Multi-namespace Mode

### AKS-6 — Show Ingresses and Events in `*` mode

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor`, `LoadAsync()` method, lines 1092–1111 (the `if (IsMultiNamespace)` branch).

Current state: `Ingresses = []` and `Events = []` are explicitly zeroed out. Replace this with:
```csharp
var ingressesTask = Client.GetIngressesAsync(allNs);
var eventsTask    = Client.GetEventsAsync(allNs);
await Task.WhenAll(deploymentsTask, podsTask, statefulSetsTask, ingressesTask, eventsTask);
Ingresses = ingressesTask.Result.ToList();
Events    = eventsTask.Result.Take(50).ToList();
```

- `src/SwebKit.Core/Abstractions/IAksClient.cs` — confirm `GetIngressesAsync` and `GetEventsAsync` accept `IList<string> namespaces` overloads. If they currently only take `string namespace`, add the list overload and implement it in `KubernetesAksClient` (fan-out across namespaces, concatenate results).
- `src/SwebKit.App/Components/Aks/IngressGrid.razor` — the `IsMultiNamespace` parameter already gates the Namespace column. No change needed.
- The `*` mode must also NOT zero out `HelmReleases`, `ConfigMaps`, `Secrets`, `Hpas`, `CronJobs`, `PodMetricsList` until AKS-7 decides what to show. For now keep those zeroed; only add Ingresses and Events.

**Dependencies:** `IAksClient` multi-namespace overloads must exist or be added first.

**Risk:** Medium. Fan-out across many namespaces can be slow. Apply a `Take(200)` cap on Ingresses in multi-namespace mode. Events fan-out should be limited to 50 per namespace with a global `Take(100)`.

---

### AKS-7 — Visible signal for hidden resource types in `*` mode

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor` — in the `@switch (ActiveResourceType)` block (lines 141–204), add a fallback or per-case check. After each case that renders an empty grid when `IsMultiNamespace` is true (ConfigMaps, Secrets, Helm, Metrics, CronJobs), add a banner component instead of the empty grid.

**Technical approach:**
Create a small `<MultiNamespaceNotice>` component or an inline conditional:
```razor
case "ConfigMaps":
    @if (IsMultiNamespace)
    {
        <div class="aks-mns-notice">
            <FluentIcon Value="@(new Icons.Regular.Size16.Info())" />
            ConfigMaps are not loaded in all-namespaces view. Select a specific namespace to browse ConfigMaps.
        </div>
    }
    else
    {
        <ConfigMapGrid ... />
    }
```
Add `.aks-mns-notice` to `app.css` with a subtle info card style (border-left accent, muted text).

The resource-type tab bar (line 91–98) should also indicate unavailable tabs. Simplest approach: render a `title` attribute or a small `ⓘ` superscript on those tab buttons when `IsMultiNamespace` is true.

**Dependencies:** AKS-6 (defines which tabs remain available in `*` mode).

**Risk:** Low.

---

## 3. YAML Editor

### AKS-8 — YAML validation before apply (client-side)

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor` — find the "Apply YAML" handler (search for `ApplyYamlAsync` or similar in the YAML overlay section, around the panel rendering block at ~line 248). Before calling `Client.ApplyYamlAsync(...)`, invoke a local validation step.
- Add NuGet package `YamlDotNet` to `SwebKit.App` (or `SwebKit.Core`). Parse the edited YAML string into a `Dictionary<object, object>` via `new DeserializerBuilder().Build().Deserialize<object>(yaml)`. Catch `YamlException` and surface the message.

**Technical approach:**
```csharp
private string? ValidateYaml(string yaml)
{
    try
    {
        var deserializer = new DeserializerBuilder().Build();
        deserializer.Deserialize<object>(yaml);
        return null; // valid
    }
    catch (YamlException ex)
    {
        return $"YAML parse error at line {ex.Start.Line}: {ex.Message}";
    }
}
```
Call this in the apply handler. If the result is non-null, display it in a `<div class="aks-yaml-error">` banner above the editor and return early — do not call the API.

**Dependencies:** YamlDotNet NuGet package. Check `SwebKit.App.csproj` for existing reference before adding.

**Risk:** Low. YamlDotNet is widely used and stable. Does not require network access. This only prevents malformed YAML from reaching the API; semantic errors (wrong field names, incompatible API version) still come back from the server.

---

### AKS-9 — Find/replace in YAML editor

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor` — the YAML overlay panel. Currently search highlights are handled by `yamlHighlight.js` (called via JSInterop). Find/replace is a separate feature: add a "Replace" input row to the YAML panel toolbar.
- `src/SwebKit.App/wwwroot/js/yamlHighlight.js` — add a `replaceInYaml(preEl, find, replaceWith)` function that does a string replace on `preEl.textContent` and re-renders. However since the YAML is stored in a C# string (`_yamlContent`), the replace is simpler on the C# side.

**Technical approach:**
The YAML content is held in a `string _yamlContent` field on `AksPage.razor`. Add two inputs to the YAML toolbar:
- "Find" text box (reuses the existing search input)
- "Replace with" text box (new, visible when a non-empty find term is active)
- "Replace all" button

```csharp
private void YamlReplaceAll(string find, string replace)
{
    if (string.IsNullOrEmpty(find)) return;
    _yamlContent = _yamlContent.Replace(find, replace, StringComparison.Ordinal);
    StateHasChanged();
}
```

After replace, re-highlight matches via the existing `JS.InvokeVoidAsync("SwebKit.searchInPre", ...)` call.

**Dependencies:** None. Works with the existing YAML string model.

**Risk:** Low. Blazor re-renders the `<pre>` content; the JS highlight is re-applied on the next search input event.

---

### AKS-10 — Diff view on YAML edit

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor` — add a "Show diff" toggle button in the YAML panel toolbar. When toggled, render a side-by-side diff view instead of the single editor.
- The Monaco Editor is already in the tech stack (`BlazorMonaco`). Use `StandaloneEditorConstructionOptions` with `readOnly = true` for the left (original) pane and `DiffEditorConstructionOptions` for a `MonacoDiffEditor` component.

**Technical approach:**
```razor
@if (_showYamlDiff)
{
    <MonacoDiffEditor Original="@_yamlOriginal" Modified="@_yamlContent"
                      ConstructionOptions="DiffOptions" />
}
else
{
    <pre class="aks-yaml-pre" ...>@_yamlContent</pre>
}
```
`_yamlOriginal` is captured when the YAML panel first opens (before any edits). `_yamlContent` is the live editable buffer. The diff editor should be `readOnly = true` on both sides (view-only diff). Applying still uses `_yamlContent`.

**Dependencies:** BlazorMonaco must be installed. Check `SwebKit.App.csproj`. If not present, add `BlazorMonaco` NuGet. OBS-1 also requires BlazorMonaco — sequence this after OBS-1 to share the setup cost.

**Risk:** Medium. Monaco diff editor adds JS weight. Gate behind the explicit "Show diff" toggle so it only loads when needed. The `<MonacoDiffEditor>` component must be on a Blazor page with JS interop available (it is, since AksPage runs inside BlazorWebView).

---

## 4. Metrics & HPA

### AKS-11 — Metrics unavailable explanation

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/PodGrid.razor` — when `PodMetrics` is `null` or the list is empty, the CPU and Memory columns already show "—". Add a `title` attribute or a `FluentTooltip` to these cells:
  ```razor
  <span title="Metrics Server not available on this cluster">—</span>
  ```
- `src/SwebKit.App/Components/Pages/AksPage.razor` — after `LoadAsync()`, check `PodMetricsList.Count == 0` and set a `bool _metricsUnavailable` flag. Render an info banner at the top of the Pods tab:
  ```razor
  @if (ActiveResourceType == "Pods" && _metricsUnavailable && !IsMultiNamespace)
  {
      <div class="aks-info-banner">
          <FluentIcon Value="@(new Icons.Regular.Size16.Info())" />
          Metrics Server is not installed on this cluster — CPU and Memory columns show "—".
          <a href="https://github.com/kubernetes-sigs/metrics-server" target="_blank">Learn more</a>
      </div>
  }
  ```
  Add `.aks-info-banner` to `app.css` (subtle, dismissible with `[x]` close button).

**Dependencies:** None. The `PodMetricsList` collection is already populated (or empty) after `LoadAsync`.

**Risk:** Low. The metrics failure path is currently a silent empty list; the only risk is false positives if the first load hasn't completed yet. Guard with `HasAnyData` to avoid showing the banner during initial load.

---

### AKS-12 — HPA real-time refresh

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/HpaPanel.razor` — add a refresh button to the panel header. The component already has a `Hpa` parameter (passed from `AksPage`). Add an `OnRefreshRequested` EventCallback parameter that `AksPage` wires to a handler that re-fetches HPAs for the current namespace and updates the `Hpa` prop.
- `src/SwebKit.App/Components/Pages/AksPage.razor` — in the HPA panel render block, add `OnRefreshRequested="RefreshHpaAsync"`. Implement `RefreshHpaAsync` to call `Client.GetHpasAsync(CurrentNamespace)` and update the `Hpas` list, then re-bind the selected HPA.
- Optionally, tie HPA refresh to the main `AutoRefreshToggle` cycle: when auto-refresh fires `LoadAsync`, HPAs are already included. No separate timer is needed — the main refresh covers it when the panel is closed (per the existing `HasAnyPanel` pause logic). Add a manual "↻" refresh icon button inside the HPA panel for on-demand refresh while the panel is open.

**Dependencies:** None.

**Risk:** Low.

---

### AKS-13 — Configurable metric bar scale

**Priority:** Low

**What to change:**
- `src/SwebKit.Core/Domain/AksConfig.cs` — add:
  ```csharp
  public int CpuBarCeilingMillicores { get; set; } = 500;
  public int MemoryBarCeilingMi { get; set; } = 512;
  ```
- `src/SwebKit.App/Components/Aks/PodGrid.razor` — replace the hardcoded `500` and `512` denominators in the bar-width calculations with `[Parameter] public int CpuCeiling` and `MemoryCeiling` parameters. `AksPage.razor` passes these from `AppState.Config.AksConfig`.
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor` — add two numeric inputs for CPU ceiling (millicores) and Memory ceiling (MiB) with sensible min/max constraints (100–8000 for CPU, 64–65536 for memory).

**Dependencies:** AKS-4 (both touch `AksConfig`; group the config changes together).

**Risk:** Low. If the user sets a ceiling below actual usage the bar just shows 100% width — no crash.

---

## 5. Port-Forward

### AKS-14 — Copy localhost URL in port-forward session row

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor`, inside the `pf-col-actions` span (around line 50). Add a copy button alongside the existing "Open in browser" button:
  ```razor
  @if (session.Status == PortForwardStatus.Active)
  {
      <button class="pf-btn-copy" title="Copy URL"
              @onclick="() => CopyUrlAsync(session.LocalUrl)">
          <FluentIcon Value="@(new Icons.Regular.Size16.Copy())" Width="13px" />
      </button>
  }
  ```
  Add `@inject IJSRuntime JS` and implement:
  ```csharp
  private async Task CopyUrlAsync(string url)
  {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", url);
      // Fire INotificationService.ShowSuccess("Copied!") — inject INotificationService
  }
  ```

**Dependencies:** None.

**Risk:** Low.

---

### AKS-15 — Port availability check before spawning kubectl

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/PortForwardStartDialog.razor` — in the confirm/start handler, before calling `SessionService.StartAsync(...)`, attempt a TCP connect to `127.0.0.1:{localPort}` with a 300 ms timeout.
- Alternatively, add a static helper in `SwebKit.Core` or `SwebKit.Kubernetes`:
  ```csharp
  public static async Task<bool> IsPortFreeAsync(int port)
  {
      try
      {
          using var tcp = new System.Net.Sockets.TcpClient();
          await tcp.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromMilliseconds(300));
          return false; // port is in use (connection succeeded)
      }
      catch { return true; } // connection refused = port is free
  }
  ```
- If the port is in use, show a warning banner in the dialog with a suggested alternative (`localPort + 1`, or the first free port found by scanning +1…+10).

**Dependencies:** None.

**Risk:** Low. The TCP probe is fast; on Windows, a refused connection returns immediately.

---

### AKS-16 — Session error detail expansion

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor` — the error detail block already exists (lines 75–78):
  ```razor
  @if (session.Status == PortForwardStatus.Error && session.LastError is not null)
  {
      <div class="pf-error-detail">@session.LastError</div>
  }
  ```
  This is already an expanded block rendered below each error row. The issue is purely CSS: `.pf-error-detail` has `max-height` or overflow truncation. Remove any such constraint, or replace the static div with a collapsible (`_errorExpanded` dict keyed by session ID):
  ```razor
  <button class="pf-btn-expand" @onclick="() => ToggleError(session.Id)">
      @(_expandedErrors.Contains(session.Id) ? "▲ Hide" : "▼ Details")
  </button>
  @if (_expandedErrors.Contains(session.Id))
  {
      <div class="pf-error-detail">@session.LastError</div>
  }
  ```
  Add `private HashSet<string> _expandedErrors = [];` to the `@code` block.

**Dependencies:** None.

**Risk:** Low.

---

## 6. Keyboard & Navigation

### AKS-17 — Grid keyboard nav for all resource grids

**Priority:** High

**What to change:**
- `src/SwebKit.App/Components/Pages/AksPage.razor`, `SelectRelative()` method (lines 1206–1275).

Reading the code: `Deployments`, `Pods`, `StatefulSets`, `ConfigMaps`, `Secrets`, `Ingresses`, `Helm`, and `CronJobs` are already handled in `SelectRelative`. The index comment in the catalog ("Only Deployments have ↑↓/shortcut keys") is outdated — the switch is more complete than documented. Verify which types are missing from `HandleLetterActionAsync` (lines 1296–1347) rather than from `SelectRelative`.

Check: `Ingresses`, `Helm`, `CronJobs` are present in `SelectRelative` but may be missing from `HandleLetterActionAsync`. Add letter-action cases for them:
```csharp
case "Ingresses" when SelectedIngress is not null:
    switch (key)
    {
        case "y": await OpenYaml("Ingress", SelectedIngress.Name); break;
        case "Enter": /* open URL */ await OpenUrlAsync(SelectedIngress.Rules.FirstOrDefault()?.Host ?? ""); break;
    }
    break;

case "CronJobs" when SelectedCronJob is not null:
    switch (key)
    {
        case "y": await OpenYaml("CronJob", SelectedCronJob.Name); break;
    }
    break;

case "Helm" when SelectedHelmRelease is not null:
    // Already handled with h/v
    break;
```

Also update the keyboard hint bar (lines 208–239) to show `<kbd>↑↓</kbd> navigate` for ALL resource types, not just the three currently called out.

**Dependencies:** None.

**Risk:** Low. The infrastructure already exists; this is gap-filling.

---

### AKS-18 — "Copy name" context menu item on all resource types

**Priority:** Medium

**What to change:**
- Every `*Grid.razor` context menu: `DeploymentGrid.razor`, `StatefulSetGrid.razor`, `PodGrid.razor`, `IngressGrid.razor`, `ConfigMapGrid.razor`, `SecretGrid.razor`, `HelmGrid.razor`, `CronJobGrid.razor`.
- In each grid's context menu render block, add as the first menu item:
  ```razor
  <div class="ctx-menu-item" @onclick="() => CopyNameAsync(context.Name)">
      <FluentIcon Value="@(new Icons.Regular.Size16.Copy())" /> Copy name
  </div>
  ```
- Each grid needs `@inject IJSRuntime JS` and `@inject INotificationService Notifications` (or pass a callback from `AksPage`). Alternatively, handle copy centrally in `AksPage` via a new `EventCallback<string> OnCopyName` parameter — `AksPage` injects JS and calls `navigator.clipboard.writeText`.

For resources that also have a Namespace field (in multi-namespace mode), add a second item "Copy namespace".

**Dependencies:** None.

**Risk:** Low.

---

### AKS-19 — Configurable auto-refresh interval dropdown

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/AutoRefreshToggle.razor` — a dropdown already exists (lines 14–19) showing 10s / 30s / 60s. This item is partially done. The remaining gap is **persistence**: the selected interval resets to 30s on page reload.
- `src/SwebKit.Core/Domain/AksConfig.cs` — add `public int AutoRefreshIntervalSeconds { get; set; } = 30;`.
- `src/SwebKit.App/Components/Aks/AutoRefreshToggle.razor` — add `[Parameter] public int InitialInterval` and `[Parameter] public EventCallback<int> IntervalChanged`. On `OnIntervalChanged`, invoke `IntervalChanged` so `AksPage` can persist the value to `AppState.Config.AksConfig.AutoRefreshIntervalSeconds`.
- `src/SwebKit.App/Components/Pages/AksPage.razor` — pass `InitialInterval="@(AppState.Config.AksConfig?.AutoRefreshIntervalSeconds ?? 30)"` and wire `IntervalChanged`.

**Dependencies:** None.

**Risk:** Low.

---

## 7. Secrets

### AKS-20 — Bulk secret reveal with confirmation

**Priority:** Medium

**What to change:**
- `src/SwebKit.App/Components/Aks/SecretDetailPanel.razor` — a "Reveal all" / "Hide all" button already exists (line 14). The current implementation calls `ToggleRevealAll()` directly without a confirmation step.

Add a confirmation gate: replace the direct `@onclick="ToggleRevealAll"` call with a two-step flow:
```razor
<button class="secret-reveal-all" @onclick="OnRevealAllClick">
    @(_revealAll ? "Hide all" : "Reveal all")
</button>

@if (_confirmReveal)
{
    <div class="secret-confirm-banner">
        This will fetch and display all secret values. Continue?
        <button @onclick="ConfirmRevealAllAsync">Yes, reveal</button>
        <button @onclick="() => _confirmReveal = false">Cancel</button>
    </div>
}
```
```csharp
private bool _confirmReveal;

private void OnRevealAllClick()
{
    if (_revealAll) { _ = ToggleRevealAll(); return; } // hide is immediate
    _confirmReveal = true;
}

private async Task ConfirmRevealAllAsync()
{
    _confirmReveal = false;
    await ToggleRevealAll();
}
```

**Dependencies:** None.

**Risk:** Low. The existing `ToggleRevealAll` logic is preserved; only the trigger is gated.

---

### AKS-21 — Secret view audit hint timestamp

**Priority:** Low

**What to change:**
- `src/SwebKit.App/Components/Aks/SecretDetailPanel.razor` — add a `DateTimeOffset? _revealedAt` field. Set it to `DateTimeOffset.UtcNow` inside `EnsureFetchedAsync()` after the fetch completes (or inside `ToggleRevealAll` / `ToggleReveal` on first reveal). Render it in the toolbar:
  ```razor
  @if (_revealedAt.HasValue)
  {
      <span class="secret-audit-hint" title="@_revealedAt.Value.ToLocalTime().ToString("g")">
          👁 Viewed at @_revealedAt.Value.ToLocalTime().ToString("HH:mm")
      </span>
  }
  ```
  Add `.secret-audit-hint` to `app.css` (muted, small font). The timestamp resets when `OnParametersSet` fires (i.e. when the user navigates to a different secret), which is already where `_revealedValues` is cleared.

**Dependencies:** None.

**Risk:** Low. This is a display-only hint; it does not record anything to disk.

---

## Implementation Order

The following ordering minimises blocked work and groups related file changes:

**Wave 1 — Foundation (unblocks others)**
1. **AKS-4** — Add `LogBufferSize` to `AksConfig` (simple model change; grounds AKS-4 and AKS-13 in the same config file).
2. **AKS-13** — Add `CpuBarCeiling` / `MemoryBarCeiling` to `AksConfig` (batch with AKS-4 — same file).
3. **AKS-19** — Persist auto-refresh interval to `AksConfig` (batch with AKS-4 — same file).

**Wave 2 — High-priority UX (independent)**
4. **AKS-1** — Container selector (high-value, self-contained).
5. **AKS-2** — Scroll-to-bottom tail toggle (high-value; needed before AKS-3 and AKS-5).
6. **AKS-8** — YAML validation (add YamlDotNet, self-contained safety feature).
7. **AKS-17** — Complete keyboard nav for all grids (low-risk gap-fill).
8. **AKS-6** — Ingresses and Events in `*` mode (requires IAksClient overloads).

**Wave 3 — Medium-priority**
9. **AKS-3** — Multi-pod timestamp merge (depends on AKS-2 for shared render path changes).
10. **AKS-5** — Pause log tail (depends on AKS-2 for tail/auto-scroll state).
11. **AKS-11** — Metrics unavailable banner (standalone, low effort).
12. **AKS-12** — HPA refresh button (standalone).
13. **AKS-7** — Multi-namespace notice banners (depends on AKS-6 defining which tabs are available).
14. **AKS-15** — Port availability check (standalone).
15. **AKS-20** — Bulk secret reveal confirmation (touches SecretDetailPanel; confirm UX pattern first).
16. **AKS-14** — Copy URL in port-forward row (trivial, can slot in anywhere).
17. **AKS-9** — Find/replace in YAML editor (standalone).
18. **AKS-18** — "Copy name" context menu (touches many grid files; batch as one PR).

**Wave 4 — Low-priority / polish**
19. **AKS-10** — Diff view on YAML (needs BlazorMonaco; do after OBS-1 which also adds it).
20. **AKS-16** — Session error detail expansion (trivial CSS/state fix).
21. **AKS-21** — Secret audit hint timestamp (cosmetic).
