# Service Bus UI Revamp — Frontend Plan

## Current Layout Analysis

### Shell structure (ServiceBusPage.razor)

The page root is a `<div>` at **line 10** with `style="display:flex; height:100%; overflow:hidden;"` and the CSS class `service-bus-page-shell` (optionally `left-pane-collapsed`). The layout is a two-column flex row with no named grid areas.

**Left column** (`service-bus-left-pane`, lines 13–133):

- CSS: `flex: 0 0 260px; min-width: 220px; max-width: 320px` (app.css line 986).
- Collapsed CSS: `flex-basis: 44px; min-width: 44px; max-width: 44px` via `.service-bus-page-shell.left-pane-collapsed .service-bus-left-pane` (app.css lines 991–995).
- Contains: namespace panel header with collapse toggle (lines 17–39), optional add-namespace form (lines 44–66), scrollable namespace list with `EntityTree` sub-components (lines 68–131).
- The collapse toggle is a plain `<button>` with a `▶` / `◀` character. The collapsed state shows only the toggle button; all content is hidden via `@if (!_isNamespacePaneCollapsed)` (line 42).

**Right column** (`service-bus-right-pane`, line 136):

- CSS: `flex: 1; display: flex; flex-direction: column; overflow: hidden` (inline style + app.css line 987).
- Contains: empty state prompt (lines 138–141) OR the tab bar + active tab content.
- **Tab bar** (`tab-bar`, lines 146–162): CSS `display:flex; overflow-x:auto; scrollbar-width:none; flex-shrink:0`. Tab items are 100–200px wide with `padding: 6px 12px`.
- **Active tab content** (lines 164–192):
  - If `IsScheduled`: renders `<ScheduledMessages>` directly (line 168).
  - If `IsDlq`: renders `<DlqView>` directly (line 173).
  - Else: a `<div class="service-bus-active-workspace" style="display:flex; flex:1; overflow:hidden;">` (line 177) containing three children side-by-side:
    1. `<div class="service-bus-message-pane" style="flex:1; overflow:hidden;">` wrapping `<MessageListView>` (lines 178–182).
    2. `<div @ref="_splitterRef" class="pane-splitter">` — the drag handle (line 183). CSS: `width:5px; cursor:col-resize`.
    3. `<div @ref="_detailPaneRef" class="details-pane">` wrapping `<MessageDetailPane>` (lines 184–189). CSS: `width:340px; flex:0 0 340px`.

**JS interop** (lines 261–273 in `@code`):

`OnAfterRenderAsync` calls `SwebKitSplitter.init(_splitterRef, _detailPaneRef, { minWidth:200, maxWidth:700 })` to make the splitter draggable. The handle is stored in `_splitterHandle` (field at line 218) and disposed on tab switch (`SetActive`, lines 511–518) and on `Dispose` (lines 553–556).

### Current layout — ASCII diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│ service-bus-page-shell  (display:flex; height:100%)                      │
│                                                                           │
│ ┌─────────────────────┐  ┌───────────────────────────────────────────┐   │
│ │ service-bus-left-   │  │ service-bus-right-pane  (flex:1)          │   │
│ │ pane  (260px fixed) │  │                                           │   │
│ │                     │  │ ┌─────────┬─────────┬─────────┐           │   │
│ │  [Namespaces header]│  │ │ tab     │ tab     │ tab +   │           │   │
│ │  [+ Add]  [◀]       │  │ └─────────┴─────────┴─────────┘           │   │
│ │                     │  │                                           │   │
│ │  ▼ orders-dev       │  │ ┌─────────────────────┬──┬────────────┐  │   │
│ │    Q  order-events  │  │ │ MessageListView      │▐▌│ details-  │  │   │
│ │    Q  order-dlq     │  │ │   (flex:1)           │  │ pane      │  │   │
│ │  ▼ payments-dev     │  │ │                      │  │ (340px    │  │   │
│ │    T  payments      │  │ │  [filter bar]        │  │  fixed)   │  │   │
│ │       ↳ sub-a       │  │ │  [message grid]      │  │           │  │   │
│ │       ↳ sub-b       │  │ │  [status bar]        │  │  [tabs:   │  │   │
│ │                     │  │ │                      │  │  Body/    │  │   │
│ │                     │  │ │                      │  │  Props/   │  │   │
│ │                     │  │ │                      │  │  System]  │  │   │
│ └─────────────────────┘  └─────────────────────┴──┴────────────┘  │   │
│                           └───────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘

Approximate widths at 1600px window:
  Left nav (global): 240px
  service-bus-left-pane: 260px
  pane-splitter: 5px
  MessageListView: ~761px  ← squeezed center
  details-pane: 340px
```

The message table receives roughly 47% of the content area — less than half the available space.

---

## Target Layout

### Design principles

1. The message table is always `flex:1` and grows to fill all remaining space.
2. The entity panel is collapsible to an icon-strip (~48px) but never zero — namespace affordance is always visible.
3. The detail pane is hidden by default and opened only when a message is selected. It can operate in two modes determined at open time.
4. The detail pane's toggle button lives on the panel itself (an `×` in the header) and on the message table toolbar.

### ASCII diagram — detail pane open, entity panel expanded (push mode, wide window)

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ service-bus-page-shell  (display:flex; height:100%)                            │
│                                                                                 │
│ ┌──────────────────────┐  ┌──────────────────────────────┬──┬──────────────┐   │
│ │ sb-entity-panel      │  │ MessageListView (flex:1)      │▐▌│ sb-detail-  │   │
│ │ (260px, expanded)    │  │                              │  │ drawer      │   │
│ │  [header + ◀ toggle] │  │  [filter bar + density btn]  │  │ (340px,     │   │
│ │  [namespace list]    │  │  [message grid — full width] │  │  push mode) │   │
│ │  [entity tree]       │  │  [status bar]                │  │  [× close]  │   │
│ └──────────────────────┘  └──────────────────────────────┴──┴──────────────┘   │
└────────────────────────────────────────────────────────────────────────────────┘
```

### ASCII diagram — detail pane closed, entity panel expanded

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ service-bus-page-shell  (display:flex; height:100%)                            │
│                                                                                 │
│ ┌──────────────────────┐  ┌────────────────────────────────────────────────┐   │
│ │ sb-entity-panel      │  │ MessageListView (flex:1 — full remaining width) │   │
│ │ (260px, expanded)    │  │                                                │   │
│ │                      │  │  [filter bar + density btn]                    │   │
│ │                      │  │  [message grid with extra cols: ExpiresAt,     │   │
│ │                      │  │   ContentType, SessionId]                      │   │
│ │                      │  │  [status bar]                                  │   │
│ └──────────────────────┘  └────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────────────┘
```

### ASCII diagram — entity panel collapsed to icon-strip, detail pane closed

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ service-bus-page-shell  (display:flex; height:100%)                            │
│                                                                                 │
│ ┌────┐  ┌──────────────────────────────────────────────────────────────────┐   │
│ │ ▶  │  │ MessageListView (flex:1 — maximum width)                         │   │
│ │ O  │  │                                                                  │   │
│ │ P  │  │  [filter bar + density btn]                                      │   │
│ │ D  │  │  [message grid — maximum columns visible]                        │   │
│ │    │  │  [status bar]                                                    │   │
│ └────┘  └──────────────────────────────────────────────────────────────────┘   │
│  48px                                                                           │
└────────────────────────────────────────────────────────────────────────────────┘
```

### ASCII diagram — overlay mode (narrow window, detail pane open)

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ service-bus-page-shell  (display:flex; height:100%; position:relative)         │
│                                                                                 │
│ ┌──────────────────────┐  ┌────────────────────────────────────────────────┐   │
│ │ sb-entity-panel      │  │ MessageListView (flex:1, NOT reflowed)          │   │
│ │ (260px)              │  │  [filter bar]                                  │   │
│ │                      │  │  [message grid — dimmed slightly but usable]   │   │
│ │                      │  │  [status bar]                                  │   │
│ └──────────────────────┘  └────────────────────────────────────────────────┘   │
│                                                          ┌──────────────────┐   │
│                                                          │ sb-detail-drawer │   │
│                                                          │  (position:abs,  │   │
│                                                          │   right:0,       │   │
│                                                          │   width:380px,   │   │
│                                                          │   full height,   │   │
│                                                          │   box-shadow)    │   │
│                                                          │  [× close]       │   │
│                                                          └──────────────────┘   │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## CSS Approach

Use the existing flexbox shell (`display:flex` on `service-bus-page-shell`) but add CSS custom properties for the two variable dimensions. No CSS grid with named areas is needed — the flex model is already correct, and changing it would require re-testing every sub-layout.

### New CSS variables (add to `:root` in app.css)

```css
--sb-entity-panel-width: 260px;
--sb-entity-panel-collapsed-width: 48px;
--sb-detail-drawer-width: 380px;
```

### New CSS classes to add to app.css (after the existing Service Bus block at line 983)

```css
/* ── Service Bus UI Revamp ─────────────────────────────── */

/* Entity panel */
.sb-entity-panel {
    flex: 0 0 var(--sb-entity-panel-width);
    min-width: var(--sb-entity-panel-width);
    max-width: var(--sb-entity-panel-width);
    border-right: 1px solid var(--color-border);
    display: flex;
    flex-direction: column;
    overflow: hidden;
    transition: flex-basis 0.2s ease, min-width 0.2s ease, max-width 0.2s ease;
}

.sb-entity-panel.collapsed {
    flex-basis: var(--sb-entity-panel-collapsed-width);
    min-width: var(--sb-entity-panel-collapsed-width);
    max-width: var(--sb-entity-panel-collapsed-width);
}

/* Icon-strip content (visible only when collapsed) */
.sb-entity-panel-icon-strip {
    display: none;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 8px 0;
}

.sb-entity-panel.collapsed .sb-entity-panel-icon-strip { display: flex; }
.sb-entity-panel.collapsed .sb-entity-panel-body       { display: none; }

.sb-ns-icon-badge {
    width: 28px;
    height: 28px;
    border-radius: var(--radius-sm);
    background: var(--color-surface-2);
    border: 1px solid var(--color-border);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: var(--font-size-xs);
    font-weight: 700;
    color: var(--color-nav-servicebus);
    cursor: pointer;
    title: attr(title);
}

.sb-ns-icon-badge:hover {
    background: var(--color-accent-subtle);
    border-color: var(--color-accent);
}

/* Detail drawer — push mode (default when open + wide window) */
.sb-detail-drawer {
    flex: 0 0 var(--sb-detail-drawer-width);
    min-width: 240px;
    max-width: 600px;
    border-left: 1px solid var(--color-border);
    background: var(--color-surface);
    display: flex;
    flex-direction: column;
    overflow: hidden;
    /* slide-in animation */
    animation: sb-drawer-slide-in 0.18s ease;
}

@keyframes sb-drawer-slide-in {
    from { transform: translateX(100%); opacity: 0; }
    to   { transform: translateX(0);    opacity: 1; }
}

/* Detail drawer — overlay mode */
.sb-detail-drawer.overlay {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    width: var(--sb-detail-drawer-width);
    flex: none;
    z-index: var(--z-dropdown);
    box-shadow: var(--shadow-lg);
    border-left: 1px solid var(--color-border);
}

.sb-detail-drawer-header {
    padding: 6px 8px 6px 12px;
    border-bottom: 1px solid var(--color-border);
    font-size: var(--font-size-xs);
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
}

.sb-detail-close-btn {
    margin-left: auto;
    background: transparent;
    border: none;
    color: var(--color-text-muted);
    cursor: pointer;
    padding: 2px 6px;
    border-radius: var(--radius-sm);
    font-size: var(--font-size-md);
    line-height: 1;
}

.sb-detail-close-btn:hover {
    background: var(--color-surface-2);
    color: var(--color-text);
}

/* Active workspace — needs position:relative for overlay mode */
.sb-active-workspace {
    display: flex;
    flex: 1;
    overflow: hidden;
    position: relative;
    min-width: 0;
}

/* Density variants on the FluentDataGrid host */
.message-grid-host.density-compact  fluent-data-grid-row { height: 28px; }
.message-grid-host.density-default  fluent-data-grid-row { height: 36px; }
.message-grid-host.density-comfort  fluent-data-grid-row { height: 44px; }

/* Compact tab item padding variant */
.tab-bar .tab-item {
    padding: 4px 10px;   /* override existing 6px 12px */
}

/* ResizablePanel drag handle — left-edge variant for detail drawer */
.sb-detail-resize-handle {
    width: 5px;
    cursor: col-resize;
    background: var(--color-border);
    flex-shrink: 0;
    transition: background 0.15s;
    align-self: stretch;
}

.sb-detail-resize-handle:hover { background: var(--color-accent); }
```

---

## Changes by File

### 1. ServiceBusPage.razor

#### Remove

- Fields `_splitterRef` (line 216), `_detailPaneRef` (line 217), `_splitterHandle` (line 218).
- The entire `OnAfterRenderAsync` override (lines 259–274) — it only initializes the JS splitter.
- The splitter disposal in `SetActive` (lines 511–518): remove the `if (_splitterHandle is not null)` block.
- The splitter disposal in `Dispose` (lines 553–556): remove the `if (_splitterHandle is not null)` block.
- The `IJSRuntime JS` inject (line 5) — check whether it is still needed for the composer; if the composer still uses it, keep it.

#### Add fields

```csharp
private bool _isDetailDrawerOverlay;   // true = overlay, false = push
private bool _isDetailPaneOpen => _activeTab?.SelectedMessage is not null;
```

Note: `_isNamespacePaneCollapsed` (line 213) already exists and drives the left panel toggle. Keep it — rename conceptually to `_isEntityPanelCollapsed` but updating the existing field name avoids a diff-heavy rename. Alternatively rename for clarity; see task list.

#### New outer HTML skeleton

Replace the content between lines 10 and 195 with the following structure (pseudocode showing key elements; preserve all `@if` / `@foreach` logic inside each zone):

```razor
<div class="service-bus-page-shell @(_isNamespacePaneCollapsed ? "sb-entity-panel-shell-collapsed" : "")"
     style="display:flex; height:100%; overflow:hidden; position:relative;">

    <!-- LEFT: entity panel -->
    <div class="sb-entity-panel @(_isNamespacePaneCollapsed ? "collapsed" : "")">

        <!-- Icon-strip (collapsed only) -->
        <div class="sb-entity-panel-icon-strip">
            <button type="button" class="namespace-pane-toggle" title="Expand"
                    @onclick="ToggleNamespacePane">▶</button>
            @foreach (var ns in _namespaceStates)
            {
                <div class="sb-ns-icon-badge" title="@ns.Namespace.Alias"
                     @onclick="() => { _isNamespacePaneCollapsed = false; ToggleNamespace(ns.Namespace.Id); }">
                    @(ns.Namespace.Alias.Length > 0 ? char.ToUpper(ns.Namespace.Alias[0]).ToString() : "?")
                </div>
            }
        </div>

        <!-- Full panel body (expanded only) -->
        <div class="sb-entity-panel-body" style="display:flex; flex-direction:column; height:100%; overflow:hidden;">
            <!-- [existing header, add-form, and namespace list content — lines 17–131 unchanged] -->
        </div>
    </div>

    <!-- RIGHT: tabbed content -->
    <div class="service-bus-right-pane" style="flex:1; display:flex; flex-direction:column; overflow:hidden; min-width:0;">
        <!-- [empty state unchanged — lines 137–141] -->

        <!-- Tab bar — unchanged structure, CSS handles compact padding -->
        <div class="tab-bar"> <!-- [lines 147–162 unchanged] --> </div>

        <!-- Active tab content -->
        @if (_activeTab is not null)
        {
            @if (_activeTab.IsScheduled)
            {
                <ScheduledMessages ... />   <!-- unchanged -->
            }
            else if (_activeTab.IsDlq)
            {
                <DlqView ... />             <!-- unchanged -->
            }
            else
            {
                <!-- NEW: use sb-active-workspace (position:relative for overlay) -->
                <div @key="_activeTab.Id" class="sb-active-workspace">

                    <!-- Message list — always flex:1, no fixed width -->
                    <div style="flex:1; overflow:hidden; display:flex; flex-direction:column; min-width:0;">
                        <MessageListView Client="_activeTab.Client"
                                         EntityPath="@_activeTab.EntityPath"
                                         NamespaceId="_activeTab.NamespaceId"
                                         ShowCompose="true"
                                         SelectedMessage="_activeTab.SelectedMessage"
                                         IsDetailPaneOpen="_isDetailPaneOpen"
                                         OnMessageSelected="async msg => await OnMessageSelectedAsync(msg)" />
                    </div>

                    <!-- Push-mode drag handle (visible only when pane is open in push mode) -->
                    @if (_isDetailPaneOpen && !_isDetailDrawerOverlay)
                    {
                        <ResizablePanel ... />   <!-- see detail pane section below -->
                    }

                    <!-- Detail drawer (push or overlay) -->
                    @if (_isDetailPaneOpen)
                    {
                        <div class="sb-detail-drawer @(_isDetailDrawerOverlay ? "overlay" : "")">
                            <div class="sb-detail-drawer-header">
                                <span style="font-size:var(--font-size-xs); color:var(--color-text-muted);">
                                    Message detail
                                </span>
                                <button class="sb-detail-close-btn" title="Close detail pane (Escape)"
                                        @onclick="CloseDetailPane">×</button>
                            </div>
                            <div style="flex:1; overflow:hidden;">
                                <MessageDetailPane Message="_activeTab.SelectedMessage"
                                                   OnEdit="msg => OpenComposer(msg, MessageComposer.ComposerMode.Edit)"
                                                   OnReplay="msg => OpenComposer(msg, MessageComposer.ComposerMode.Replay)"
                                                   OnSchedule="msg => OpenComposer(msg, MessageComposer.ComposerMode.Schedule)" />
                            </div>
                        </div>
                    }

                </div>
            }
        }
    </div>
</div>
```

#### New methods

```csharp
private async Task OnMessageSelectedAsync(SbMessage? msg)
{
    if (_activeTab is null) return;
    var wasOpen = _isDetailPaneOpen;
    _activeTab.SelectedMessage = msg;

    if (msg is not null && !wasOpen)
    {
        // Determine overlay vs push at open time
        var width = await JS.InvokeAsync<double>("eval", "window.innerWidth");
        _isDetailDrawerOverlay = width < 1400;
    }

    await InvokeAsync(StateHasChanged);
}

private void CloseDetailPane()
{
    if (_activeTab is not null) _activeTab.SelectedMessage = null;
    StateHasChanged();
}
```

The `OnServiceBusShortcut` handler (line 528) already deselects via `SelectedMessage = null` — add a call to `StateHasChanged()` if not already present, but the drawer will close automatically because `_isDetailPaneOpen` returns false.

**localStorage persistence for entity panel state:** Call a small JS helper on `ToggleNamespacePane`:

```csharp
private async Task ToggleNamespacePane()
{
    _isNamespacePaneCollapsed = !_isNamespacePaneCollapsed;
    if (_isNamespacePaneCollapsed) _showAddForm = false;
    await JS.InvokeVoidAsync("localStorage.setItem",
        "sb:entityPanel:collapsed", _isNamespacePaneCollapsed.ToString().ToLower());
}
```

And in `OnInitializedAsync`, after loading namespaces:

```csharp
var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "sb:entityPanel:collapsed");
if (stored == "true") _isNamespacePaneCollapsed = true;
```

---

### 2. EntityTree.razor

No structural changes are needed. The icon-strip badge and expand-on-click logic live in `ServiceBusPage.razor` (the parent), not in `EntityTree.razor`. The `EntityTree` component is only rendered when `ns.IsExpanded` is true (line 116 of `ServiceBusPage.razor`), and it continues to render inside the `sb-entity-panel-body` section. No new parameters required.

If a future iteration wants per-namespace collapse-to-icon in the expanded panel, an `IsCollapsed` parameter and a CSS transition could be added to `EntityTree.razor`, but this is out of scope for this revamp.

---

### 3. MessageDetailPane.razor

The component itself requires **no changes to its internal markup**. The detail pane header (with message ID and action buttons) and the tabbed body are unchanged.

What changes is that the component is now:
1. Conditionally rendered (`@if (_isDetailPaneOpen)`) rather than always present.
2. Wrapped in the `sb-detail-drawer` div (owned by the parent `ServiceBusPage.razor`), which provides the close button and drawer chrome.

The `Message is null` empty state block (lines 4–9 of `MessageDetailPane.razor`) will never render in the new design because the drawer is only opened when a message is selected. It can be kept as a safety fallback.

Remove the `Message is null` branch's "Select a message" placeholder from visual flow — it is already gated by `@if (_isDetailPaneOpen)` in the parent. No code changes needed in `MessageDetailPane.razor`.

---

### 4. MessageListView.razor

#### New parameter

```csharp
[Parameter] public bool IsDetailPaneOpen { get; set; }
```

#### Density toggle

Add a `_density` field and a toolbar button in the filter bar (between the Export button and the Peek button):

```csharp
private string _density = "default"; // "compact" | "default" | "comfort"
```

In the filter bar HTML, add before the Peek button:

```razor
<div style="display:flex; border:1px solid var(--color-border); border-radius:3px; overflow:hidden;">
    @foreach (var d in new[] { ("compact","C"), ("default","D"), ("comfort","R") })
    {
        <button @onclick="() => _density = d.Item1"
                style="padding:3px 7px; font-size:10px; cursor:pointer;
                       background:@(_density == d.Item1 ? "var(--color-accent)" : "var(--color-surface)");
                       color:@(_density == d.Item1 ? "white" : "var(--color-text-muted)");
                       border:none; border-right:1px solid var(--color-border);"
                title="@(d.Item1 == "compact" ? "Compact rows (28px)" : d.Item1 == "default" ? "Default rows (36px)" : "Comfortable rows (44px)")">
            @d.Item2
        </button>
    }
</div>
```

Apply the density class on the container div wrapping the `FluentDataGrid`:

```razor
<div class="data-table-container message-grid-scroll message-grid-host density-@_density" ...>
```

#### Additional columns when detail pane is closed

In the `FluentDataGrid` column definitions (after the `Delivery` column at line 153, before the `ShowDlqColumns` block):

```razor
@if (!IsDetailPaneOpen)
{
    <TemplateColumn Title="Content-Type" Width="110px"
                    SortBy="@(GridSort<SbMessage>.ByAscending(m => m.ContentType))">
        <span class="cell-truncate" title="@(context.ContentType ?? "-")">
            @(context.ContentType ?? "-")
        </span>
    </TemplateColumn>
    <TemplateColumn Title="Session ID" Width="130px"
                    SortBy="@(GridSort<SbMessage>.ByAscending(m => m.SessionId))">
        <span class="cell-truncate" title="@(context.SessionId ?? "-")">
            @(context.SessionId ?? "-")
        </span>
    </TemplateColumn>
}
```

`ExpiresAt` can be added if `SbMessage` exposes it. Check `SwebKit.Core.Models.SbMessage` — if `ExpiresAt` is not currently a property, add it in a separate, focused change first (it is available from `ServiceBusReceivedMessage.ExpiresAt` in the Azure SDK). Do not add the column here until the model property exists.

Note: The `FluentDataGrid` uses `ResizableColumns="true"` (line 129), which stores per-column widths in internal JS state. Column add/remove causes a full re-render of the grid. This is acceptable behavior: the grid re-renders anyway when `IsDetailPaneOpen` changes because the parent triggers `StateHasChanged`.

#### Density persistence

Persist density to localStorage via `IJSRuntime` (already injected on line 3). In `OnInitializedAsync` (or `OnAfterRenderAsync` on first render):

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "sb:msglist:density");
        if (stored is "compact" or "default" or "comfort")
        {
            _density = stored;
            StateHasChanged();
        }
    }
}
```

On density change:

```csharp
private async Task SetDensity(string d)
{
    _density = d;
    await JS.InvokeVoidAsync("localStorage.setItem", "sb:msglist:density", d);
    StateHasChanged();
}
```

Replace the `@onclick="() => _density = d.Item1"` in the toolbar button with `@onclick="() => SetDensity(d.Item1)"`.

---

### 5. ResizablePanel usage for push-mode drag handle

`src/SwebKit.App/Components/Aks/ResizablePanel.razor` provides a pure Blazor drag-to-resize panel. Its current shape:

- Parameters: `ChildContent`, `DefaultWidth` (int, default 380), `MinWidth` (int, default 200), `MaxWidth` (int, default 800).
- The resize handle (`div.resize-handle`) is on the **left** edge; dragging left makes the panel wider. This is correct for a right-side detail drawer.
- Uses `resize-overlay` (a full-screen transparent div) to capture mouse moves outside the handle.

For the push-mode detail drawer, wrap `MessageDetailPane` in `ResizablePanel`:

```razor
@if (_isDetailPaneOpen && !_isDetailDrawerOverlay)
{
    <ResizablePanel DefaultWidth="380" MinWidth="240" MaxWidth="600">
        <div class="sb-detail-drawer">
            <div class="sb-detail-drawer-header">
                <span ...>Message detail</span>
                <button class="sb-detail-close-btn" @onclick="CloseDetailPane">×</button>
            </div>
            <div style="flex:1; overflow:hidden;">
                <MessageDetailPane ... />
            </div>
        </div>
    </ResizablePanel>
}
```

The `ResizablePanel` already handles the width CSS via an inline `style="width:@(Width)px"` on the outer div. The `sb-detail-drawer` class inside it must have `width:100%; height:100%; display:flex; flex-direction:column;` — add these overrides in the CSS block above or via inline style.

Check `app.css` for existing `.resizable-panel` and `.resize-handle` styles (they likely exist near the AKS section). Confirm those styles do not conflict with the Service Bus usage.

**Do not use `ResizablePanel` for the overlay mode** — the overlay drawer is position:absolute and should not be resizable via drag (it would interact incorrectly with absolute positioning). In overlay mode, the width is fixed at `var(--sb-detail-drawer-width)`.

---

### 6. JS interop summary

| Purpose | Mechanism | Notes |
|---|---|---|
| Panel state persistence (entity panel) | `localStorage.setItem/getItem` via `JS.InvokeAsync` | No new JS module; uses existing `IJSRuntime`. |
| Density persistence | `localStorage.setItem/getItem` via `JS.InvokeAsync` | Same. |
| Overlay vs push decision | `eval("window.innerWidth")` via `JS.InvokeAsync<double>` | One-time call on drawer open. Could be replaced by a CSS media query approach. |
| Drag-to-resize (push mode) | `ResizablePanel.razor` (pure Blazor) | Replaces the `SwebKitSplitter` JS interop entirely for active messages tab. |
| `SwebKitSplitter.init` | **REMOVED** from `ServiceBusPage.razor` | Still used by `DlqView.razor` — do not remove from `wwwroot/js/`. |

---

## Implementation Tasks

1. **[CSS]** Add the new CSS variables (`--sb-entity-panel-width`, `--sb-entity-panel-collapsed-width`, `--sb-detail-drawer-width`) to `:root` in `app.css`.

2. **[CSS]** Add the new CSS class block (`.sb-entity-panel`, `.sb-entity-panel.collapsed`, `.sb-entity-panel-icon-strip`, `.sb-entity-panel-body`, `.sb-ns-icon-badge`, `.sb-detail-drawer`, `.sb-detail-drawer.overlay`, `.sb-detail-drawer-header`, `.sb-detail-close-btn`, `.sb-active-workspace`, `.message-grid-host.density-*`, `.sb-detail-resize-handle`) to `app.css` after line 997.

3. **[CSS]** Override `.tab-bar .tab-item` padding to `4px 10px` in the new block (this compacts the tab bar slightly without affecting other tab bars that use `.pill-tab-bar`).

4. **[ServiceBusPage.razor]** Remove `@ref` fields `_splitterRef`, `_detailPaneRef`, and `_splitterHandle` (lines 216–218). Remove the `OnAfterRenderAsync` override (lines 259–274). Remove splitter disposal from `SetActive` (lines 511–518) and `Dispose` (lines 553–556).

5. **[ServiceBusPage.razor]** Add `_isDetailDrawerOverlay` field and the computed `_isDetailPaneOpen` property.

6. **[ServiceBusPage.razor]** Restructure the outer `<div>` shell at line 10: change the left-pane div to use class `sb-entity-panel` (replacing `service-bus-left-pane`) and add the icon-strip + body substructure. Preserve all existing namespace list logic inside the body section.

7. **[ServiceBusPage.razor]** Restructure the active workspace at line 177: replace `service-bus-active-workspace` + `service-bus-message-pane` + `pane-splitter` + `details-pane` divs with the new `sb-active-workspace` + `ResizablePanel` (push) or absolute-positioned `sb-detail-drawer` (overlay) pattern.

8. **[ServiceBusPage.razor]** Add `OnMessageSelectedAsync` method and `CloseDetailPane` method. Wire `OnMessageSelectedAsync` as the `OnMessageSelected` callback on `MessageListView`. Wire `CloseDetailPane` to the `×` button in the drawer header.

9. **[ServiceBusPage.razor]** Make `ToggleNamespacePane` async and add `localStorage.setItem` persistence call. Add localStorage read in `OnInitializedAsync` to restore collapsed state.

10. **[MessageListView.razor]** Add `IsDetailPaneOpen` parameter.

11. **[MessageListView.razor]** Add `_density` field and `SetDensity` async method with localStorage persistence.

12. **[MessageListView.razor]** Add the density toggle button group to the filter bar (between Export and Peek).

13. **[MessageListView.razor]** Apply `density-@_density` class to the `data-table-container` host div.

14. **[MessageListView.razor]** Add `OnAfterRenderAsync(firstRender)` to restore density from localStorage on first render.

15. **[MessageListView.razor]** Add `ContentType` and `SessionId` `TemplateColumn` definitions gated on `!IsDetailPaneOpen`.

16. **[ServiceBusPage.razor → MessageListView parameter]** Pass `IsDetailPaneOpen="@_isDetailPaneOpen"` to the `<MessageListView>` component call.

17. **[ResizablePanel / CSS]** Check `app.css` for existing `.resizable-panel` and `.resize-handle` styles. Confirm they work for the Service Bus push-mode usage. If the handle color or positioning conflicts, add a `.sb-detail-drawer .resize-handle` override.

18. **[app.css]** Verify the `tab-item` padding change does not break the AKS page or any other page that uses `.tab-bar`. (The AKS page uses its own pill-tab-bar — no conflict expected.)

19. **[Manual test]** Verify the full message lifecycle in the new layout:
    - Select a message → drawer opens → detail pane shows all tabs (Body, Properties, System, DLQ Info).
    - Press Escape → drawer closes → extra columns appear in the grid.
    - Click `×` on the drawer → same as Escape.
    - Collapse entity panel → icon-strip appears → click badge → panel expands.
    - Toggle density → rows change height → density survives page reload (localStorage).
    - Reload page → entity panel collapsed state is restored from localStorage.
    - Open on a simulated narrow window (<1400px) → drawer opens in overlay mode → table behind it is not reflowed.

20. **[architecture doc]** Update `docs/architecture/functionalities/service-bus.md` to reflect the new layout structure, removal of the JS splitter, and the new `IsDetailPaneOpen` / `_density` parameters.
