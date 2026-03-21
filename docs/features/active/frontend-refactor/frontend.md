# Frontend Refactor — Design & Implementation Notes

## 1. CSS Architecture

### 1.1 Token system

Add to `wwwroot/app.css` (`:root` block):

```css
/* Spacing scale */
--spacing-xs:  4px;
--spacing-sm:  8px;
--spacing-md: 12px;
--spacing-lg: 16px;
--spacing-xl: 24px;

/* Typography scale */
--font-size-xs: 10px;
--font-size-sm: 11px;
--font-size-md: 13px;
--font-size-lg: 14px;
--font-size-xl: 16px;

/* Z-index scale */
--z-dropdown:  200;
--z-modal:     500;
--z-toast:     900;
--z-overlay:  1000;
```

Replace all magic values in CSS isolation files with these tokens. No inline style changes in this step — CSS files only.

### 1.2 Utility classes

Add to `app.css`:

```css
/* Form inputs (replaces 54 inline style copies) */
.form-input {
  background: var(--color-surface);
  color: var(--color-text);
  border: 1px solid var(--color-border);
  border-radius: 3px;
  padding: var(--spacing-xs) var(--spacing-sm);
  font-size: var(--font-size-sm);
}

/* Surface card */
.surface-card {
  background: var(--color-surface-2);
  border: 1px solid var(--color-border);
  border-radius: 4px;
}

/* Muted text */
.text-muted { color: var(--color-text-muted); }
.text-sm    { font-size: var(--font-size-sm); }
.text-xs    { font-size: var(--font-size-xs); }
```

### 1.3 AksPage.razor.css split

Break `AksPage.razor.css` (1,183 lines) into:

| File | Responsibility | Est. lines |
|------|---------------|-----------|
| `AksPage.razor.css` | Layout, toolbar, tabs | ~200 |
| `AksPage.Tables.razor.css` | Resource grids | ~250 |
| `AksPage.HpaPanel.razor.css` | HPA detail panel | ~300 |
| `AksPage.YamlEditor.razor.css` | YAML syntax highlight | ~200 |
| `AksPage.ScaleControls.razor.css` | Scale input, spinner | ~80 |

All files must be linked from the `.razor` via `@import` or via Blazor CSS isolation — verify Blazor supports multiple isolation files per component (it does via bundling).

> **Decision needed:** Blazor CSS isolation bundles all `.razor.css` files matching the component name. Sub-files must use a naming convention like `AksPage.hpa.razor.css` and be imported via `<link>` in `app.css` or use a Blazor CSS bundle. See [decisions.md](decisions.md).

---

## 2. Shared UI Primitives

### 2.1 `<EmptyState />`

**Location:** `Components/Shared/EmptyState.razor`

```razor
@* Parameters: Icon (string emoji/icon), Title (string), Subtitle (string?) *@
<div class="empty-state">
    <div class="empty-state-icon">@Icon</div>
    <div class="empty-state-title">@Title</div>
    @if (Subtitle is not null)
    {
        <div class="empty-state-subtitle">@Subtitle</div>
    }
</div>
```

CSS in `EmptyState.razor.css`:
```css
.empty-state { padding: 40px; text-align: center; color: var(--color-text-muted); }
.empty-state-icon { font-size: 28px; margin-bottom: 12px; opacity: 0.4; }
.empty-state-title { font-size: var(--font-size-lg); font-weight: 500; }
.empty-state-subtitle { font-size: var(--font-size-sm); margin-top: var(--spacing-xs); }
```

Replace all 10+ inline empty-state patterns.

### 2.2 `<Modal />`

**Location:** `Components/Shared/Modal.razor`

```razor
@* Parameters: IsOpen (bool), OnClose (EventCallback), ChildContent (RenderFragment) *@
@if (IsOpen)
{
    <div class="modal-backdrop" @onclick="OnClose">
        <div class="modal-container" @onclick:stopPropagation>
            @ChildContent
        </div>
    </div>
}
```

CSS in `Modal.razor.css`:
```css
.modal-backdrop  { position: fixed; inset: 0; background: rgba(0,0,0,0.5); z-index: var(--z-modal); display: flex; align-items: center; justify-content: center; }
.modal-container { background: var(--color-surface); border-radius: 6px; padding: var(--spacing-lg); }
```

Replace: `ConfirmDialog.razor` backdrop, `MessageListView.razor` save-filter dialog, `ServiceBusPage.razor` composer modal.

### 2.3 `<Dropdown />`

**Location:** `Components/Shared/Dropdown.razor`

```razor
@* Parameters: IsOpen (bool), OnClose (EventCallback), ChildContent (RenderFragment) *@
@if (IsOpen)
{
    <div class="dropdown-backdrop" @onclick="OnClose"></div>
    <div class="dropdown-menu">@ChildContent</div>
}
```

Replace filter dropdown in `MessageListView.razor`, namespace picker in `AksPage.razor`.

---

## 3. Component Splitting

### 3.1 `AksPage.razor` — target: < 150 lines (orchestrator only)

Extract:

| New Component | Responsibility | Key params |
|---------------|---------------|-----------|
| `DeploymentGrid.razor` | Deployment table + scale action | `Deployments`, `OnScale`, `OnOpenPanel` |
| `PodGrid.razor` | Pod table + log action | `Pods`, `OnOpenLogs`, `OnOpenPanel` |
| `ConfigMapGrid.razor` | ConfigMap table | `ConfigMaps`, `OnOpenPanel` |
| `HpaPanel.razor` | HPA detail panel | `Hpa`, `OnClose` |
| `YamlViewer.razor` | Read-only YAML display | `Yaml`, `OnClose` |

`AksPage.razor` retains only: namespace/resource type selection, data loading, child component wiring.

### 3.2 `MessageListView.razor` — target: < 250 lines

Extract:

| Class / Component | Responsibility |
|------------------|---------------|
| `MessageFilterState.cs` | Filter + saved filter CRUD |
| `MessageExporter.cs` | Export logic (CSV, JSON) |
| `AutoRefreshController.cs` | Timer lifecycle |

### 3.3 `ServiceBusPage.razor` — target: < 150 lines

Extract namespace+tab wiring into a `ServiceBusTabController.cs` service; page becomes pure rendering.

---

## 4. Async & Lifecycle Fixes

### 4.1 `PodLogView.razor` — fire-and-forget `Task.Run`

Replace:
```csharp
_ = Task.Run(async () => { ... });
```
With:
```csharp
_streamTask = Task.Run(async () => { ... }, _cts.Token);
```
Store task reference. In `DisposeAsync`, call `_cts.Cancel()` and `await _streamTask` with a timeout.

Also add a per-render debounce: buffer N lines then invoke `StateHasChanged` once, rather than per-line.

### 4.2 `MessageListView.razor` — timer disposal

`System.Timers.Timer` must be disposed in `IAsyncDisposable.DisposeAsync`, not only via `ApplyAutoRefresh`. Add explicit `Dispose` call in the component's `DisposeAsync`.

### 4.3 Event callbacks — missing `await`

All `EventCallback.InvokeAsync(...)` calls must be `await`ed. Current occurrences without await:
- `MessageListView.razor` line ~255: `_ = OnMultiSelectionChanged.InvokeAsync(...)`
- `ServiceBusPage.razor` line ~173: `OnMessageSelected.InvokeAsync(...)`

### 4.4 JS Interop error handling

Every `JS.InvokeVoidAsync` / `JS.InvokeAsync` call must be wrapped in `try/catch` with a logged warning. No generic `catch { }` without logging.

---

## 5. Code Duplication — Extractions

### 5.1 `SelectionService<T>`

**Location:** `Components/Shared/SelectionService.cs`

```csharp
public sealed class SelectionService<T>
{
    private readonly HashSet<T> _selected = [];
    public IReadOnlySet<T> Selected => _selected;

    public void Toggle(T item, bool select)
    {
        if (select) _selected.Add(item);
        else _selected.Remove(item);
    }

    public void Clear() => _selected.Clear();
    public bool IsSelected(T item) => _selected.Contains(item);
}
```

Replace duplicated toggle patterns in `MessageListView.razor`, `RedisKeyList.razor`, `AksPage.razor`.

### 5.2 `AutoRefreshController`

**Location:** `Components/Shared/AutoRefreshController.cs`

Encapsulates `System.Timers.Timer` start/stop/dispose and exposes:
```csharp
void SetInterval(int seconds, Func<Task> callback);
void Stop();
ValueTask DisposeAsync();
```

Replace timer logic in `MessageListView.razor` and `PodLogView.razor`.

---

## Files affected (key list)

| File | Change type |
|------|------------|
| `wwwroot/app.css` | Add tokens + utility classes |
| `Components/Pages/AksPage.razor.css` | Split into sub-files |
| `Components/Pages/AksPage.razor` | Extract child components |
| `Components/ServiceBus/MessageListView.razor` | Extract state + timer |
| `Components/Pages/ServiceBusPage.razor` | Extract tab controller |
| `Components/Aks/PodLogView.razor` | Fix async streaming |
| `Components/Shared/EmptyState.razor` (new) | Shared primitive |
| `Components/Shared/Modal.razor` (new) | Shared primitive |
| `Components/Shared/Dropdown.razor` (new) | Shared primitive |
| `Components/Shared/SelectionService.cs` (new) | Shared logic |
| `Components/Shared/AutoRefreshController.cs` (new) | Shared logic |
