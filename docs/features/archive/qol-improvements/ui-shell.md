# UI Shell QOL — Technical Implementation Plan

**Status:** Planned
**Scope:** UI-1 through UI-24 from `index.md` section 7 (Global UI Shell & Cross-cutting)
**Parent catalog:** [index.md](./index.md)

---

## Prerequisites and dependency map

Before starting implementation, note the hard dependency chain:

```
ISelectionContext (UI-7)
  └─ IsAvailable predicates in AppCommand  ←── UI-2 (context-aware filtering)
       └─ area-specific command registration  ←── UI-1 (feature page commands)
```

UI-7 **must land first**. The `ISelectionContext` interface already exists in
`src/SwebKit.Core/Abstractions/ISelectionContext.cs` but has no concrete
implementation or DI registration yet. All area-command work in UI-1 and UI-2
is blocked until the service is wired.

The error/loading infrastructure (UI-8 through UI-11) is the second hard
prerequisite: every feature page relies on reliable error surfacing, and
skeleton loaders reduce apparent blank-page time across all areas. Deliver these
early.

---

## Implementation order

### Wave 0 — Unblock everything (do first)

| Item | Why first |
|------|-----------|
| **UI-7** ISelectionContext | Unblocks UI-1 and UI-2 |
| **UI-8** Error boundary | Unblocks reliable page rendering; tiny change |

### Wave 1 — Error / loading infrastructure (UI-8 to UI-11)

Deliver as a coherent group. All pages benefit immediately and later waves
(command registration, settings forms) need reliable loading states to build on.

### Wave 2 — Command palette (UI-1 to UI-4)

Depends on UI-7. Deliver all four together so the palette feels complete.

### Wave 3 — Settings mini-sprint (UI-15 to UI-19)

Self-contained; involves `SettingsPage.razor` and the config form components.
No hard deps on other waves.

### Wave 4 — Notifications & feedback (UI-12 to UI-14)

Light touch across many call sites. Can be done in parallel with Wave 3.

### Wave 5 — Keyboard navigation (UI-5 to UI-6)

UI-5 is independent; UI-6 requires identifying modal open/close sites.

### Wave 6 — Theme & accessibility sweep (UI-20 to UI-24)

Fully independent of all other waves. Can be picked off one item at a time.

---

## UI-1 — Register area-specific commands

**Priority:** High
**Blocked by:** UI-7

### What to change

Each feature page's `OnInitialized` (or `OnAfterRenderAsync(firstRender)` if
async context is needed) calls `CommandRegistry.Register` for its area commands
and `CommandRegistry.Unregister` in `IDisposable.Dispose`.

Files to touch:
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/ReleasesPage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`

### Technical approach

Inject `CommandRegistry` and `ISelectionContext` at the top of each page. In
`OnInitialized`, build a list of command IDs registered by this page instance
so they can be removed on dispose. Each command sets `AreaScope` to the page's
area string so `CommandRegistry.GetAvailable` filters it correctly.

Example pattern for `AksPage.razor`:

```csharp
@inject CommandRegistry Commands
@inject ISelectionContext Selection
@implements IDisposable

private readonly List<string> _registeredCmds = [];

protected override void OnInitialized()
{
    // existing subscriptions ...

    Register(new AppCommand
    {
        Id   = "aks-restart-deployment",
        Label = "Restart Deployment",
        Category = "AKS",
        Icon = "↺",
        AreaScope = "aks",
        IsAvailable = () => Selection.GetSelection<DeploymentInfo>("aks") is not null,
        Execute = async () => { /* delegate to existing restart logic */ }
    });
    // ... more commands
}

private void Register(AppCommand cmd)
{
    Commands.Register(cmd);
    _registeredCmds.Add(cmd.Id);
}

public void Dispose()
{
    foreach (var id in _registeredCmds) Commands.Unregister(id);
    // existing unsubscribe ...
}
```

Suggested commands per area:

| Area | Commands |
|------|----------|
| Service Bus | Peek Messages, View DLQ, Resubmit DLQ, Send Message, Refresh Entities |
| AKS | Restart Deployment, Scale Deployment, Stream Pod Logs, Open Shell, Port-Forward |
| Redis | Flush Database, Delete Key, Refresh Keys, Set TTL |
| Storage | Copy Blob URL, Copy SAS URL, Download Blob, Refresh Blobs |
| Releases | Trigger Pipeline, Approve Stage, Refresh Board |
| Observability | Run Query, Clear Results, Save Query, Refresh Overview |

### Dependencies

`ISelectionContext` concrete implementation registered in DI (UI-7).

---

## UI-2 — Context-aware command filtering via availability predicates

**Priority:** Medium
**Blocked by:** UI-7

### What to change

- `src/SwebKit.App/Services/CommandRegistry.cs` — `GetAvailable` already calls
  `c.IsAvailable?.Invoke()` (line 42). No registry changes needed.
- `src/SwebKit.App/Components/Shared/CommandPalette.razor` — pass `ISelectionContext`
  as a cascading value or injected service so predicate closures can reference it.
- Each feature page (per UI-1) — supply the `IsAvailable` lambda that reads from
  `ISelectionContext`.

### Technical approach

`ISelectionContext` is a singleton service (see UI-7). Feature pages call
`Selection.SetSelection("aks", selectedDeployment)` whenever the selection
changes (existing `@onclick` handlers on grid rows). The `IsAvailable` lambda
in each `AppCommand` closes over the `ISelectionContext` instance:

```csharp
IsAvailable = () => Selection.GetSelection<DeploymentInfo>("aks") is not null,
```

Because `GetAvailable` is called every time the palette renders (inside
`GetSections()`), predicates always reflect the current selection state without
any additional event wiring.

Destructive commands (Flush DB, Restart, Complete DLQ) should also check
`AppState.Config`'s environment `IsProduction` flag:

```csharp
IsAvailable = () =>
    Selection.GetSelection<RedisConfig>("redis") is not null &&
    !AppState.IsCurrentEnvironmentProduction,
```

### Dependencies

UI-7 (ISelectionContext), UI-1 (commands registered).

---

## UI-3 — Prefix-boosted fuzzy search scoring

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Shared/CommandPalette.razor`, method `FuzzyScore`
  (lines 184–198).

### Technical approach

The existing algorithm awards +2 for consecutive character matches. Add an
additional +3 bonus when the first matched character in the label is at index 0
(i.e., the query starts at the label's beginning):

```csharp
private static int FuzzyScore(string query, string label)
{
    var q = query.ToLowerInvariant();
    var l = label.ToLowerInvariant();
    int qi = 0, score = 0, lastMatch = -1;
    bool firstMatchSeen = false;

    for (int i = 0; i < l.Length && qi < q.Length; i++)
    {
        if (l[i] != q[qi]) continue;
        int bonus = (i == lastMatch + 1) ? 2 : 1;   // consecutive bonus
        if (!firstMatchSeen && i == 0) bonus += 3;   // prefix bonus
        firstMatchSeen = true;
        score += bonus;
        lastMatch = i;
        qi++;
    }

    return qi == q.Length ? score : 0;
}
```

This ensures "Peek Messages" scores above "Open Peek Dialog" when the user
types "peek".

No other files change.

---

## UI-4 — "Go to resource" sub-commands

**Priority:** Low

### What to change

- `src/SwebKit.App/Components/Shared/CommandPalette.razor` — add a new
  section/mode when query starts with `>` (VS Code convention) or the literal
  text "go ".
- `src/SwebKit.App/Services/CommandRegistry.cs` — add a
  `RegisterDynamicProvider(Func<string, IEnumerable<AppCommand>>)` extension
  point, or handle inline in the palette.

### Technical approach

The simplest approach avoids changing `CommandRegistry`. In `GetSections()`,
detect a "go " prefix and dynamically build resource commands from
`AppState.ServiceBusNamespaces`, open tabs (from `TabService.Tabs`), and known
cluster/namespace pairs from `AppState.Config.AksConfig`:

```csharp
if (_query.StartsWith("go ", StringComparison.OrdinalIgnoreCase))
{
    var resourceQuery = _query[3..].Trim();
    var resourceCmds = BuildGoToCommands(resourceQuery);
    if (resourceCmds.Count > 0)
        result.Add(new CommandSection("Go to resource", resourceCmds));
}
```

`BuildGoToCommands` returns `AppCommand` objects with `Execute` lambdas that
call `NavigationManager.NavigateTo` or publish `NavigateToAreaEvent`. These
are transient — not registered in `CommandRegistry`, just returned inline.

A second trigger: typing a `/` prefix shows open tabs. This mirrors VS Code's
edfitor-switching behaviour and requires no new infrastructure.

---

## UI-5 — Grid keyboard nav completeness

**Priority:** High

### What to change

- `src/SwebKit.App/Components/Pages/RedisPage.razor` — key tree / key list
- `src/SwebKit.App/Components/Shared/StorageBlobList.razor` (or
  `src/SwebKit.App/Components/Pages/StoragePage.razor`)
- `src/SwebKit.App/Components/Pages/ReleasesPage.razor` — release board rows

### Technical approach

Replicate the pattern already used in `AksPage.razor` (`HandleGridKeyDown`
around line 1190). Each grid needs:

1. A `_selectedIndex` field (int, -1 = none).
2. A `@onkeydown` on the containing `<div>` with `tabindex="0"`.
3. A handler:

```csharp
private async Task HandleKeyDown(KeyboardEventArgs e)
{
    switch (e.Key)
    {
        case "ArrowDown":
            _selectedIndex = Math.Min(_selectedIndex + 1, _items.Count - 1);
            await ScrollSelectedIntoView();
            break;
        case "ArrowUp":
            _selectedIndex = Math.Max(_selectedIndex - 1, 0);
            await ScrollSelectedIntoView();
            break;
        case "Enter":
            if (_selectedIndex >= 0) SelectItem(_items[_selectedIndex]);
            break;
        case "Escape":
            _selectedIndex = -1;
            break;
    }
    StateHasChanged();
}
```

4. CSS class `selected` on the active row: `class="@(i == _selectedIndex ? "selected" : "")"`.
5. JS helper call for scroll: `await JS.InvokeVoidAsync("SwebKit.scrollItemIntoView", _gridRef, _selectedIndex)` — add `scrollItemIntoView` to `wwwroot/js/keyboardShortcuts.js`.

For the Redis key tree, the selected index operates on the flattened visible
key list (not the grouping tree). For the Storage blob list, it operates on
the `_blobs` list that the component already maintains.

---

## UI-6 — Focus restoration on modal close

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Shared/Modal.razor`
- `src/SwebKit.App/Components/Shared/ConfirmDialog.razor`
- Call sites that open modals (composer buttons, YAML edit button, delete buttons)
  in feature pages.

### Technical approach

Add a JS helper to `keyboardShortcuts.js`:

```javascript
window.SwebKit.focusElement = function (element) {
    if (element) element.focus();
};
window.SwebKit.saveFocus = function () {
    return document.activeElement;
};
```

In `Modal.razor`, capture the triggering element before the modal opens. The
cleanest way is a new `[Parameter] public ElementReference? TriggerElement`
parameter. When `IsOpen` transitions from `true` to `false` (detected in
`OnParametersSet` by comparing previous value), call:

```csharp
protected override async Task OnParametersSetAsync()
{
    if (_wasOpen && !IsOpen && _triggerElement.HasValue)
    {
        try { await JS.InvokeVoidAsync("SwebKit.focusElement", _triggerElement.Value); }
        catch { }
    }
    _wasOpen = IsOpen;
}
```

At every modal open site, pass `TriggerElement="@_openButtonRef"` where
`_openButtonRef` is the `ElementReference` of the button that triggered the
open. Feature pages already use `@ref` in some places; add it to modal trigger
buttons.

For `ConfirmDialog.razor`, the same pattern applies via an `OnAfterClose`
callback or a `TriggerElement` parameter.

---

## UI-7 — ISelectionContext service

**Priority:** Medium
**Note:** This is Wave 0 — implement before UI-1 and UI-2.

### What to change

1. `src/SwebKit.Core/Abstractions/ISelectionContext.cs` — interface already
   exists with the correct shape (`SetSelection`, `GetSelection<T>`, `SelectionChanged`).
2. New file: `src/SwebKit.Core/Services/SelectionContext.cs` — concrete implementation.
3. `src/SwebKit.App/MauiProgram.cs` (or wherever DI is configured) — register as singleton.

### Technical approach

Implement `ISelectionContext` as a plain dictionary-backed singleton:

```csharp
// src/SwebKit.Core/Services/SelectionContext.cs
namespace SwebKit.Core.Services;

public class SelectionContext : ISelectionContext
{
    private readonly Dictionary<string, object?> _selections = new();

    public event Action? SelectionChanged;

    public void SetSelection(string area, object? selected)
    {
        _selections[area] = selected;
        SelectionChanged?.Invoke();
    }

    public T? GetSelection<T>(string area) where T : class =>
        _selections.TryGetValue(area, out var v) ? v as T : null;
}
```

DI registration (Singleton) in `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<ISelectionContext, SelectionContext>();
```

Feature pages call `Selection.SetSelection("redis", selectedKey)` in their
existing selection-change handlers. The event bus (`IAppEventBus`) does not
need to be involved — the selection is synchronous state.

---

## UI-8 — Generic error boundary

**Priority:** High
**Note:** Wave 0 — implement early.

### What to change

- `src/SwebKit.App/Components/Layout/MainLayout.razor` — wrap `@Body` in a
  Blazor `ErrorBoundary`.
- New file: `src/SwebKit.App/Components/Shared/AppErrorBoundary.razor` — custom
  error UI.

### Technical approach

Blazor ships a built-in `ErrorBoundary` component (`Microsoft.AspNetCore.Components.Web`).
Create a thin wrapper with SwebKit styling:

```razor
@* src/SwebKit.App/Components/Shared/AppErrorBoundary.razor *@
@inherits ErrorBoundary

<ChildContent>@ChildContent</ChildContent>
<ErrorContent Context="ex">
    <div class="error-boundary-page">
        <FluentIcon Value="@(new Icons.Regular.Size32.ErrorCircle())" />
        <h2>Something went wrong</h2>
        <p class="error-boundary-message">@ex.Message</p>
        <button @onclick="Recover">Reload this area</button>
        <details>
            <summary>Technical details</summary>
            <pre>@ex.ToString()</pre>
        </details>
    </div>
</ErrorContent>
```

In `MainLayout.razor`, replace:

```razor
<main id="main-content" class="main-content" tabindex="-1">
    @Body
</main>
```

with:

```razor
<main id="main-content" class="main-content" tabindex="-1">
    <AppErrorBoundary>
        @Body
    </AppErrorBoundary>
</main>
```

Add `.error-boundary-page` styles to `app.css`: centered flex column,
`var(--color-error)` icon tint, monospace `<pre>` block.

The `Recover()` method (inherited from `ErrorBoundary`) resets the boundary
so the user can retry without a full app restart.

---

## UI-9 — Skeleton loaders for main data grids

**Priority:** Medium

### What to change

- `src/SwebKit.App/wwwroot/app.css` — add shimmer animation and skeleton row styles.
- New shared component: `src/SwebKit.App/Components/Shared/SkeletonRows.razor`.
- Feature pages: replace `<LoadingSpinner>` in the main grid area with
  `<SkeletonRows>` during initial load.

### Technical approach

CSS shimmer (add to `app.css`):

```css
@keyframes skeleton-shimmer {
    0%   { background-position: -400px 0; }
    100% { background-position: 400px 0; }
}

.skeleton-row {
    height: 32px;
    border-radius: var(--radius-sm);
    background: linear-gradient(
        90deg,
        var(--color-surface-2) 25%,
        var(--color-surface-3) 50%,
        var(--color-surface-2) 75%
    );
    background-size: 800px 100%;
    animation: skeleton-shimmer 1.4s infinite;
    margin-bottom: 4px;
}

.skeleton-cell {
    height: 14px;
    border-radius: var(--radius-sm);
    background: inherit;
    display: inline-block;
}
```

Skeleton component:

```razor
@* src/SwebKit.App/Components/Shared/SkeletonRows.razor *@
@for (int i = 0; i < Count; i++)
{
    <div class="skeleton-row" style="width: @GetWidth(i)%" aria-hidden="true"></div>
}

@code {
    [Parameter] public int Count { get; set; } = 6;
    private static int GetWidth(int i) => 100 - (i % 3 * 8); // slight variation
}
```

Usage in feature pages: show `<SkeletonRows Count="8" />` while
`_isLoading && _items.Count == 0`. Keep `<LoadingSpinner>` for
subsequent refreshes (when rows are already populated).

Target pages/components for skeleton replacement:
- `ServiceBusPage.razor` — message list area
- `AksPage.razor` — deployments grid, pods grid
- `RedisPage.razor` — key list area
- `StorageBlobList.razor` — blob list rows
- `ObservabilityLogs.razor` — results table

---

## UI-10 — Retry with exponential backoff on ErrorCallout

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Shared/ErrorCallout.razor` — add retry counter,
  backoff delay, attempt label.

### Technical approach

Replace the current stateless component with a stateful one:

```razor
@* ErrorCallout.razor *@
<div class="error-callout">
    <strong>Error:</strong>
    <span class="error-callout-message">@_displayMessage</span>
    @if (OnRetry.HasDelegate)
    {
        <button class="error-callout-retry"
                disabled="@_retrying"
                @onclick="RetryAsync">
            @(_retrying ? $"Retrying…" : $"Retry{(_attemptCount > 0 ? $" #{_attemptCount + 1}" : "")}")
        </button>
    }
</div>

@code {
    [Parameter] public string Message { get; set; } = "An error occurred.";
    [Parameter] public EventCallback OnRetry { get; set; }

    private bool _retrying;
    private int _attemptCount;
    private string _displayMessage => Message;

    private async Task RetryAsync()
    {
        _retrying = true;
        StateHasChanged();
        int delayMs = _attemptCount switch
        {
            0 => 0,
            1 => 1000,
            2 => 2000,
            _ => 4000
        };
        if (delayMs > 0) await Task.Delay(delayMs);
        _attemptCount++;
        _retrying = false;
        await OnRetry.InvokeAsync();
    }
}
```

The backoff resets when the parent re-renders `ErrorCallout` with a new
`Message` (i.e., when the error clears). No parent component changes required.

---

## UI-11 — Error message expansion toggle

**Priority:** Low

### What to change

- `src/SwebKit.App/Components/Shared/ErrorCallout.razor` — add "Show more"
  toggle for long messages.
- `src/SwebKit.App/Components/Notifications/NotificationToast.razor` — the
  `n.Detail` block at line 20 already clamps to 2 lines; add a button to expand.

### Technical approach

In `ErrorCallout.razor`:

```razor
@{
    const int TruncateAt = 120;
    var isTruncatable = Message.Length > TruncateAt;
}
<div class="error-callout">
    <strong>Error:</strong>
    <span>@(_expanded || !isTruncatable ? Message : Message[..TruncateAt] + "…")</span>
    @if (isTruncatable)
    {
        <button class="error-callout-expand" @onclick="() => _expanded = !_expanded">
            @(_expanded ? "Show less" : "Show more")
        </button>
    }
    @* retry button *@
</div>

@code {
    private bool _expanded;
    // ... existing params
}
```

In `NotificationToast.razor`, replace the CSS clamp on the `n.Detail` div with
a per-notification `_expanded` dictionary (`Dictionary<Guid, bool>`), and add a
"more" button that toggles the entry:

```csharp
private readonly Dictionary<Guid, bool> _expanded = new();
```

```razor
@if (!string.IsNullOrEmpty(n.Detail))
{
    var isExpanded = _expanded.GetValueOrDefault(n.Id);
    <div style="… @(isExpanded ? "" : "-webkit-line-clamp:2;")">@n.Detail</div>
    @if (n.Detail.Length > 80)
    {
        <button @onclick="() => ToggleExpanded(n.Id)">
            @(isExpanded ? "less" : "more")
        </button>
    }
}
```

---

## UI-12 — Consistent copy feedback via INotificationService.ShowSuccess

**Priority:** Medium

### What to change

Every place in the codebase that calls `JS.InvokeVoidAsync("navigator.clipboard.writeText", ...)`
or the `SwebKit.copyToClipboard` helper without showing feedback.

Primary locations (audit-identified):
- `MessageDetailPane.razor` — body copy, MessageId copy, sequence number copy
- `ObservabilityFailures.razor` (line 67) — stack trace copy
- `RedisKeyDetail.razor` — key name copy (RDS-5 companion)
- `StorageBlobList.razor` — Copy URL, Copy SAS URL actions
- `PortForwardSessionsPanel.razor` — Copy localhost URL
- `ServiceBusPage.razor` — connection string display actions

### Technical approach

Inject `INotificationService` wherever it is not already present. After each
successful clipboard write, add one line:

```csharp
await JS.InvokeVoidAsync("navigator.clipboard.writeText", value);
Notifications.ShowSuccess("Copied!", detail: label); // e.g., "Message ID copied"
```

`INotificationService.ShowSuccess` auto-dismisses after 2 s (the timer in
`NotificationToast.razor` uses 4000 ms for Success — reduce this to 2000 ms
for `ShowSuccess` calls that originate from clipboard actions, or add a
dedicated `ShowBrief` overload).

Alternatively, keep the 4 s timer and rely on the toast's close button for
earlier dismissal — both are acceptable.

---

## UI-13 — Persistent notification history in UiStateRepository

**Priority:** Medium

### What to change

- `src/SwebKit.Core/Configuration/UiStateRepository.cs` — add `NotificationHistory`
  list to `UiState`.
- `src/SwebKit.Core/Models/Notification.cs` (wherever `Notification` is defined)
  — ensure it is serializable.
- `src/SwebKit.App/Services/NotificationService.cs` — persist on `Show*` and
  prune to last 50.
- `src/SwebKit.App/Components/Layout/TopBar.razor` — wire unread count badge to
  history, add a dropdown panel.

### Technical approach

Add to `UiState`:

```csharp
public List<PersistedNotification> NotificationHistory { get; set; } = [];
```

Where `PersistedNotification` is a slim serializable record:

```csharp
public record PersistedNotification(
    Guid Id,
    string Message,
    string? Detail,
    NotificationSeverity Severity,
    DateTimeOffset Timestamp,
    bool Read);
```

In `NotificationService.ShowSuccess` (and the other `Show*` variants), after
adding to the in-memory `All` list, also append to `UiStateRepository`'s
history and call `_uiState.SaveAsync()` (fire-and-forget with
`Task.Run(...).ConfigureAwait(false)`).

Prune to 50 before saving:

```csharp
while (_uiState.State.NotificationHistory.Count > 50)
    _uiState.State.NotificationHistory.RemoveAt(0);
```

In `TopBar.razor`, the bell icon's unread count is
`State.NotificationHistory.Count(n => !n.Read)`. Opening the dropdown marks
all as read and calls `_uiState.SaveAsync()`.

---

## UI-14 — Action progress percentage in status bar

**Priority:** Low

### What to change

- `src/SwebKit.Core/Abstractions/ITaskQueue.cs` — `BackgroundTask` already has
  `Progress` (int?) and `Total` (int?) fields and a `ProgressText` computed
  property. No model changes needed.
- `src/SwebKit.App/Components/Layout/StatusBar.razor` — render progress when
  available.

### Technical approach

`BackgroundTask.Progress` and `BackgroundTask.Total` are already defined.
Callers (batch resubmit, AKS restart, tag creation) just need to call
`TaskQueue.Update(id, t => { t.Progress = done; t.Total = total; })` at each
step.

In `StatusBar.razor`, after the `<FluentProgressRing>`, add:

```razor
@if (running[0].Total is > 0)
{
    <span>@running[0].Progress / @running[0].Total</span>
    <progress value="@running[0].Progress" max="@running[0].Total"
              style="width:60px; height:6px;" />
}
```

For the multi-task case, show aggregate percentage:

```razor
var totalDone = running.Sum(t => t.Progress ?? 0);
var totalWork = running.Sum(t => t.Total ?? 0);
```

Sites to update for actual progress reporting:
- `DlqView.razor` batch resubmit loop — call `Update` after each message
- `AksPage.razor` rolling restart — update per-pod completion
- Wherever `ITaskQueue.Enqueue` is called for multi-step work

---

## UI-15 — Unsaved changes detection + navigation guard

**Priority:** High

### What to change

- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- All config form components (`ServiceBusConfigForm.razor`, `AksConfigForm.razor`,
  `RedisConfigForm.razor`, `StorageConfigForm.razor`, `DevOpsConfigForm.razor`)

### Technical approach

Config forms currently bind directly to `AppState.Config` (a reference type),
so changes mutate the live object immediately. The fix:

1. Each config form receives its config section as a parameter and works on a
   **copy** (`JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(original))`)
   that it edits locally.
2. The form tracks `_isDirty` by comparing the working copy to the original
   on each field change, or by setting `_isDirty = true` on any `@onchange`.
3. On save (`OnSaved.InvokeAsync()`), the working copy is applied back to
   `AppState.Config` and `AppState.SaveConfigAsync()` is called.

In `SettingsPage.razor`, register a navigation guard using
`NavigationManager.RegisterLocationChangingHandler`:

```csharp
private IDisposable? _navGuard;

protected override void OnInitialized()
{
    _navGuard = Nav.RegisterLocationChangingHandler(OnNavChanging);
}

private async ValueTask OnNavChanging(LocationChangingContext ctx)
{
    if (!_anyFormDirty) return;
    var confirmed = await JS.InvokeAsync<bool>(
        "confirm", "You have unsaved changes. Leave anyway?");
    if (!confirmed) ctx.PreventNavigation();
}

public void Dispose() => _navGuard?.Dispose();
```

`_anyFormDirty` is a bool exposed by each config form via an `[Parameter]
EventCallback<bool> OnDirtyChanged` and aggregated in `SettingsPage`.

The save flow in each form must also reset `_isDirty = false` after the
working copy is applied.

---

## UI-16 — Form validation field highlighting

**Priority:** Medium

### What to change

- All config form components under
  `src/SwebKit.App/Components/Settings/` (or wherever the forms live).
- `src/SwebKit.App/wwwroot/app.css` — add `.field-invalid` and `.field-valid` styles.

### Technical approach

Each config form already has a `Save` button handler. Before persisting, run
a validation pass that populates a `Dictionary<string, string> _errors` (field
name → message). Apply CSS class and `aria-invalid` attribute based on presence
in `_errors`:

```razor
<input class="settings-input @(_errors.ContainsKey("ConnectionString") ? "field-invalid" : "")"
       aria-invalid="@(_errors.ContainsKey("ConnectionString").ToString().ToLower())"
       aria-describedby="@(_errors.ContainsKey("ConnectionString") ? "err-cs" : null)"
       ... />
@if (_errors.TryGetValue("ConnectionString", out var csErr))
{
    <span id="err-cs" class="field-error-msg" role="alert">@csErr</span>
}
```

CSS additions to `app.css`:

```css
.field-invalid {
    border-color: var(--color-error) !important;
    outline: 1px solid var(--color-error);
}

.field-error-msg {
    display: block;
    color: var(--color-error);
    font-size: var(--font-size-xs);
    margin-top: var(--spacing-xs);
}
```

Required-field validation: check for empty strings on fields marked as
required. Connection string fields should also check for the correct format
(e.g., starts with `Endpoint=sb://` for Service Bus).

---

## UI-17 — Environment clone action

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.Core/Services/AppStateService.cs` — add `CloneConfigAsync(string newName)`.
- `src/SwebKit.Core/Domain/AppConfig.cs` — verify deep-cloneable via `System.Text.Json`.

### Technical approach

`AppConfig` is a POCO with no circular references, so deep-clone via JSON
roundtrip is safe:

```csharp
// AppStateService.cs
public async Task CloneEnvironmentAsync(string newEnvironmentName)
{
    var json = JsonSerializer.Serialize(_profiles.Config, SwebKitJsonOptions.Indented);
    var clone = JsonSerializer.Deserialize<AppConfig>(json, SwebKitJsonOptions.Indented)!;
    // The project/environment model may need a name field — add one if absent.
    await _profiles.AddEnvironmentAsync(newEnvironmentName, clone);
    await _profiles.SaveAsync();
}
```

In `SettingsPage.razor`, add a "Clone current environment" button in the
environment selector area (top of the settings shell). Clicking it shows a
`<FluentDialog>` (or the existing `Modal.razor`) prompting for the new
environment name, then calls `AppState.CloneEnvironmentAsync(newName)`.

Note: the current data model has `AppConfig` as a flat object per environment.
If multi-environment support is not yet wired (the config model shows a single
`AppConfig`), this item depends on first introducing a named-environment
wrapper in `ProfileData`. Check `ProfileRepository.cs` and defer if the model
does not support it — file a follow-up.

---

## UI-18 — Config export / import

**Priority:** Medium

### What to change

- New service: `src/SwebKit.Core/Services/ConfigExportService.cs`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor` — export/import buttons
- `src/SwebKit.Core/Configuration/ProfileRepository.cs` — expose raw
  `ProfileData` for serialization and a merge import path.

### Technical approach

**Export:**

`ConfigExportService.ExportSanitisedAsync()` returns a JSON string of
`ProfileData` with all secret-looking fields zeroed out. "Secret-looking"
fields are identified by naming convention: any property whose name contains
`ConnectionString`, `Key`, `Password`, `Secret`, `Token`, or `SharedAccessKey`
is replaced with `"<redacted>"`. Use reflection or a custom `JsonConverter`.

The export is triggered from `SettingsPage.razor` and downloads via the
existing `SwebKit.downloadText` JS helper:

```csharp
var json = await ConfigExport.ExportSanitisedAsync();
await JS.InvokeVoidAsync("SwebKit.downloadText",
    $"swebkit-config-{DateTime.Now:yyyyMMdd}.json", "application/json", json);
```

**Import:**

A file picker (`<InputFile>`) reads the uploaded JSON, deserialises to
`ProfileData`, and calls `ProfileRepository.MergeImportAsync(imported)`. The
merge strategy: overwrite non-secret scalars, preserve existing secrets in the
keychain, and prompt the user for any secrets found in the import file (they
will be `"<redacted>"` from a sanitised export).

Secret prompting: for each redacted field, show a `<FluentDialog>` asking the
user to enter the value, then call `ICredentialStore.Save(key, value)`.

---

## UI-19 — Centralised PasswordField component

**Priority:** Low

### What to change

- New component: `src/SwebKit.App/Components/Shared/PasswordField.razor`
- `src/SwebKit.App/Components/Settings/RedisConfigForm.razor` — replace plain
  connection string `<input>` with `<PasswordField>`.
- `src/SwebKit.App/Components/Settings/StorageConfigForm.razor` — same.
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` — any places that
  display connection strings.

### Technical approach

```razor
@* src/SwebKit.App/Components/Shared/PasswordField.razor *@
<div class="password-field">
    <input type="@(_show ? "text" : "password")"
           class="settings-input"
           value="@Value"
           @oninput="e => ValueChanged.InvokeAsync(e.Value?.ToString())"
           aria-label="@Label"
           autocomplete="off" />
    <button type="button"
            class="password-field-toggle"
            @onclick="() => _show = !_show"
            aria-label="@(_show ? "Hide" : "Show") value"
            title="@(_show ? "Hide" : "Show")">
        @(_show ? "🙈" : "👁")
    </button>
</div>

@code {
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public string Label { get; set; } = "Secret value";

    private bool _show;
}
```

The component uses `type="password"` by default (browser masks the value, no
JS needed) and toggles to `type="text"` on the show button. Replace emoji icons
with `FluentIcon` equivalents once a suitable eye icon is confirmed available in
the Fluent icon set (`Icons.Regular.Size16.Eye` / `EyeOff`).

---

## UI-20 — System dark/light preference auto-detect on first launch

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Layout/MainLayout.razor`, method
  `OnAfterRenderAsync` (lines 82–95).
- `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js` — add a helper to read
  `prefers-color-scheme`.

### Technical approach

The existing code (lines 83–94) reads `localStorage.getItem("swebkit-ui-theme")`
and applies it. The gap: when `stored` is null (first launch), it keeps the
`_currentTheme = "dark"` default without consulting the OS preference.

Add a JS helper:

```javascript
window.SwebKit.getSystemThemePreference = function () {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
};
```

Modify `OnAfterRenderAsync` in `MainLayout.razor`:

```csharp
var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "swebkit-ui-theme");
if (!string.IsNullOrWhiteSpace(stored))
{
    _currentTheme = stored;
}
else
{
    // First launch: respect OS preference
    var sysPref = await JS.InvokeAsync<string>("SwebKit.getSystemThemePreference");
    _currentTheme = sysPref == "light" ? "light-azure-bloom" : "dark";
    // Persist so subsequent launches don't re-detect
    await JS.InvokeVoidAsync("localStorage.setItem", "swebkit-ui-theme", _currentTheme);
}
```

The mapping `"light" → "light-azure-bloom"` is a reasonable default. If the
user subsequently changes the theme in Settings, the stored value takes
precedence.

---

## UI-21 — ARIA labels on all interactive elements

**Priority:** Medium

### What to change

Pervasive. Highest-impact targets first:

- `src/SwebKit.App/Components/Layout/TopBar.razor` — nav toggle, project
  selector, demo toggle, bell icon button.
- `src/SwebKit.App/Components/Layout/LeftNav.razor` — each nav item (icon-only
  in collapsed mode).
- `src/SwebKit.App/Components/Layout/StatusBar.razor` — port-forward button,
  connection dots.
- All toolbar buttons in feature pages that show only icons (refresh, filter,
  expand pane, etc.).
- Context menu items in grid rows.

### Technical approach

For icon-only buttons (`<button>` with no text content), add `aria-label`:

```razor
<button @onclick="ToggleNav" aria-label="@(IsNavExpanded ? "Collapse sidebar" : "Expand sidebar")">
    <FluentIcon ... />
</button>
```

For nav items in collapsed state where the label text is hidden by CSS:

```razor
<a href="/aks" aria-label="AKS">
    <FluentIcon ... />
    <span class="nav-label">AKS</span>
</a>
```

For `<span class="connection-indicator">` in `StatusBar.razor` (which uses
`title` already), add `role="status"` and ensure the `title` attribute is
populated for all states (currently only populated for error state — fix
the conditional on line 21).

Do this as a sweep PR touching all files. Use `aria-label` for action buttons,
`aria-labelledby` when a visible label already exists nearby, and `role="img"`
with `aria-label` for decorative icon-only status indicators.

---

## UI-22 — Color-blind safe status indicators

**Priority:** Medium

### What to change

- `src/SwebKit.App/Components/Layout/StatusBar.razor` — connection dots.
- Severity badges throughout (observability, releases).
- `src/SwebKit.App/wwwroot/app.css` — add shape/text cues to `.dot-*` classes.

### Technical approach

The three dots in `StatusBar.razor` currently rely solely on background color.
Add a shape cue by changing the HTML:

```razor
<span class="connection-indicator" title="@tooltip" role="status" aria-label="@tooltip">
    <span class="connection-dot @css">
        @(s.State switch {
            ConnectionState.Connected => "✓",
            ConnectionState.Error     => "✕",
            _                         => "?"
        })
    </span>
    <span class="connection-label">@label</span>
</span>
```

Update `.connection-dot` CSS to show the character:

```css
.connection-dot {
    width: 14px;
    height: 14px;
    border-radius: 50%;
    font-size: 9px;
    display: flex;
    align-items: center;
    justify-content: center;
    font-weight: 700;
    flex-shrink: 0;
}
```

For severity badges (observability `ExceptionSeverity`, releases pipeline
status), prepend a short text tag alongside the colored chip: "ERR", "WARN",
"OK", "SKIP". These can be added to the badge `span` as text nodes next to
the icon.

---

## UI-23 — Visible focus rings

**Priority:** Low

### What to change

- `src/SwebKit.App/wwwroot/app.css` — add global focus ring rules.

### Technical approach

Add after the existing theme blocks:

```css
/* ── Focus rings (accessibility) ── */
:focus-visible {
    outline: 2px solid var(--color-accent);
    outline-offset: 2px;
    border-radius: var(--radius-sm);
}

/* Suppress focus ring on mouse clicks but preserve for keyboard nav */
:focus:not(:focus-visible) {
    outline: none;
}

/* Elevated ring inside dark modals where accent may be subtle */
.modal-container :focus-visible,
.command-palette :focus-visible,
.shortcuts-panel :focus-visible {
    outline-color: var(--color-accent-hover);
    outline-width: 2px;
}
```

The `:focus-visible` pseudo-class is supported in all modern browsers and
respects the distinction between keyboard and pointer focus. The existing
`SwebKit.trapFocus` JS utility already manages Tab cycling within modals;
these CSS rules ensure the focused element is always visually obvious.

No Razor component changes required.

---

## UI-24 — Demo banner CSS variable

**Priority:** Low

### What to change

- `src/SwebKit.App/wwwroot/app.css` — `.demo-banner` rule (line 299).
- Optionally `src/SwebKit.App/Components/Layout/MainLayout.razor` if any
  inline style references the hardcoded color.

### Technical approach

The `:root` block and each theme block already define `--color-warning`. The
demo banner uses the hardcoded value `#d97706` (line 299 of `app.css`).

Change:

```css
.demo-banner {
    /* ... */
    background: #d97706;   /* before */
    background: var(--color-warning);   /* after */
    color: #1a1a1a;
```

The `color: #1a1a1a` is safe across all current themes (dark warning colors
are all sufficiently light to provide contrast against near-black text). If
a future theme inverts this, add `--color-warning-text` to the theme blocks.

Check `MainLayout.razor` lines 19–25 — the demo banner `<div>` and its button
use class names only (`demo-banner`, `demo-banner-disable`), so no inline
color changes are needed there.

---

## Cross-cutting notes

### Naming convention for new files

| Type | Path pattern |
|------|-------------|
| Core service | `src/SwebKit.Core/Services/SelectionContext.cs` |
| Core interface | `src/SwebKit.Core/Abstractions/ISelectionContext.cs` |
| App service | `src/SwebKit.App/Services/ConfigExportService.cs` |
| Shared component | `src/SwebKit.App/Components/Shared/SkeletonRows.razor` |
| Layout component | `src/SwebKit.App/Components/Layout/AppErrorBoundary.razor` |

### DI registrations to add

All new services go in the DI setup file (locate via `AddSingleton` or
`AddScoped` calls in `MauiProgram.cs`):

```csharp
builder.Services.AddSingleton<ISelectionContext, SelectionContext>();
// ConfigExportService needs ProfileRepository and ICredentialStore — register as Scoped or Singleton
builder.Services.AddSingleton<ConfigExportService>();
```

### Architecture doc updates required

When any item in this plan changes the runtime behaviour of Settings,
Observability, or the Status Bar area, update the matching file under
`docs/architecture/functionalities/` in the same changeset (per CLAUDE.md
instruction).

Specifically:
- UI-7, UI-1, UI-2 → touch no functionality doc (cross-cutting infrastructure)
- UI-15, UI-17, UI-18 → update `docs/architecture/functionalities/` for each
  area whose config changes
- UI-20 → no functionality doc change (purely presentation)
