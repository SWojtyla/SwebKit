# Frontend — Monitoring Alert Rules

## Overview

A new routed page at `/monitoring` under the "Signals" shell group. The page uses the existing shell layout conventions (same structure as `IncidentTimelinePage`, `ObservabilityPage`): `RoutePageHeader` + `PageToolbar` at the top, then a horizontal two-panel split. Rule editing uses a `ResizablePanel`-style slide-over drawer (right-anchored) rather than a modal, so the rule list stays visible while editing. Alert history lives in a separate collapsible right column. Design tokens, spacing, and component primitives follow the conventions already established across the app.

---

## 1. Navigation Wiring

### `ShellNavigation.cs`

Add after `IncidentTimeline`:

```csharp
public static readonly ShellNavEntry Monitoring = new(
    "monitoring",
    "/monitoring",
    "Monitoring",
    "Define alert rules and receive Windows notifications when thresholds are breached.",
    "Signals",
    new Icons.Regular.Size24.AlertOn());
```

Add `Monitoring` to both `Items` and the `"Signals"` group in `Groups`.

### `Routes.razor`

Add:
```razor
<Route path="/monitoring" component="@typeof(MonitoringPage)" />
```

### `_Imports.razor`

Add:
```razor
@using SwebKit.App.Components.Monitoring
```

---

## 2. File Structure

```
src/SwebKit.App/Components/
└── Monitoring/
    ├── MonitoringPage.razor              # Routed page — orchestrates sub-components
    ├── MonitoringPage.razor.css
    ├── AlertRuleGroups.razor             # Collapsible source-group list with status dots
    ├── AlertRuleGroups.razor.css
    ├── AlertRuleRow.razor                # Single rule row (status dot, name, badges, actions)
    ├── AlertRuleRow.razor.css
    ├── AlertRuleDrawer.razor             # Slide-over editor (create + edit)
    ├── AlertRuleDrawer.razor.css
    ├── AlertHistoryPanel.razor           # Right-column firing history
    └── AlertHistoryPanel.razor.css
```

**Also add to `SwebKit.App.Tests.csproj`:** `<RazorComponent Include="..." />` entries for all new `.razor` files (per editing-notes.md pitfall).

---

## 3. `MonitoringPage.razor`

### Route and layout

```razor
@page "/monitoring"
@layout MainLayout
```

### Shell header

Uses `<RoutePageHeader Area="monitoring" />` — same pattern as all other pages. This renders the eyebrow ("Signals"), page title ("Monitoring"), and subtitle from `ShellNavigation.Monitoring`.

### Toolbar (`PageToolbar`)

```
[LeadingContent]                          [TrailingContent]
  ● Monitoring active / ○ Paused            [+ Add rule]
    {N} rules · {M} firing
```

- The status pill is a `<button class="monitoring-toggle-btn">` that calls `StartAsync` / `StopAsync`. Visually mirrors the port-forward session button in `StatusBar.razor` — small pill with colored dot.
- "N rules · M firing" is a subtitle span that updates reactively.
- `[+ Add rule]` button (icon + label, trailing slot) opens the `AlertRuleDrawer` in create mode.

### Page layout

```
┌─────────────────────────────────┬──────────────────────┐
│  AlertRuleGroups                │  AlertHistoryPanel   │
│  (flex-grow: 1, min-width 420px)│  (fixed 320px, via   │
│                                 │   ResizablePanel)    │
└─────────────────────────────────┴──────────────────────┘
```

- `AlertHistoryPanel` wraps in `<ResizablePanel DefaultWidth="320" MinWidth="220" MaxWidth="520">` — the resize handle is on the left edge, consistent with side panels in AKS.
- When the drawer is open, the `AlertRuleGroups` column shrinks (CSS `flex-grow` reduced) to accommodate the drawer without overlapping history.
- `AlertRuleDrawer` is positioned absolutely against the page content area (not the viewport), so it slides over the `AlertRuleGroups` column only — history stays visible.

### Drawer state

`MonitoringPage` owns:
```csharp
private bool _drawerOpen;
private MonitoringAlertRule? _editingRule;  // null = create mode
```

Open on Add button → `_drawerOpen = true; _editingRule = null;`  
Open on Edit click from `AlertRuleRow` → `_drawerOpen = true; _editingRule = rule;`  
Close on save or cancel → `_drawerOpen = false; _editingRule = null;`  
After save → call `IAlertRuleRepository.UpsertAsync`, then notify `IAlertMonitorService` to reload rules.

### Loading state

On `OnInitializedAsync`: load rules from repository. During load, `AlertRuleGroups` shows `<SkeletonRows Count="4" />` (existing shared component).

### Services injected

- `IAlertMonitorService`
- `IAlertRuleRepository`
- `INotificationService`

### `AlertFired` subscription

```csharp
protected override void OnInitialized()
{
    _monitor.AlertFired += OnAlertFired;
}

private async void OnAlertFired(AlertFiredEvent evt)
{
    _history.Insert(0, evt);  // newest first
    if (_history.Count > 200) _history.RemoveAt(_history.Count - 1);
    await InvokeAsync(StateHasChanged);  // BL-2
}
```

---

## 4. `AlertRuleGroups.razor`

### Purpose

Renders the four source categories as collapsible sections. Each section header shows a summary badge. Rules within each section are rendered by `AlertRuleRow`.

### Parameters

```csharp
[Parameter] public IReadOnlyList<MonitoringAlertRule> Rules { get; set; } = [];
[Parameter] public EventCallback<MonitoringAlertRule> OnEdit { get; set; }
[Parameter] public EventCallback<MonitoringAlertRule> OnDelete { get; set; }
[Parameter] public EventCallback<MonitoringAlertRule> OnToggle { get; set; }
```

### Group definitions (static)

```csharp
private static readonly IReadOnlyList<(string Label, string Icon, AlertRuleSource[] Sources)> Groups =
[
    ("AKS",         "☸",  [AksPodHealth, AksPodRestartRate, AksNamespaceHealthScore]),
    ("Service Bus", "⇄",  [ServiceBusDlqDepth, ServiceBusActiveDepth, ServiceBusDeadSubscription]),
    ("Redis",       "⬡",  [RedisMemoryUsage, RedisConnectedClients]),
    ("Storage",     "🗄",  [StorageBlobCount]),
];
```

### Section header

```
▼  ☸  AKS           3 rules · 1 firing
```

- Collapse toggle (`▼` / `▶`) on click — persisted in local component state (not persisted to repository).
- Source icon (matches icon pattern from existing area pills in `StatusBar`).
- Label.
- Summary badge: `{total} rule{s}` when all OK; turns amber `{N} firing` when any rule in this group is in `Firing` status.
- Empty groups are still shown (so operators know where to add rules) but the badge shows "0 rules".

### Empty state per group

When a group has no rules:
```
<EmptyState Icon="+" Title="No {GroupLabel} rules" Subtitle="Click Add rule to monitor {GroupLabel} resources.">
```
Uses the existing `<EmptyState>` shared component.

### Global empty state

When there are no rules at all (before any are added), the entire groups area shows a single centered `<EmptyState>` with an "Add your first alert rule" action button.

---

## 5. `AlertRuleRow.razor`

### Parameters

```csharp
[Parameter, EditorRequired] public MonitoringAlertRule Rule { get; set; } = default!;
[Parameter] public EventCallback<MonitoringAlertRule> OnEdit { get; set; }
[Parameter] public EventCallback<MonitoringAlertRule> OnDelete { get; set; }
[Parameter] public EventCallback<MonitoringAlertRule> OnToggle { get; set; }

/// Live engine state for this rule — updated by MonitoringPage on each AlertFired / status tick.
[Parameter] public AlertRuleUiState UiState { get; set; }
```

### `AlertRuleUiState`

```csharp
public enum AlertRuleUiStateKind { Unknown, Ok, Cooldown, Firing, Skipped, Error }

public readonly record struct AlertRuleUiState(
    AlertRuleUiStateKind Kind,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset? LastEvaluatedAt);
```

`MonitoringPage` maintains a `Dictionary<string, AlertRuleUiState>` updated on each `AlertFired` event and on a 10-second Blazor timer (`PeriodicTimer` not used in UI — use `System.Threading.Timer` via `IAsyncDisposable`).

### Row anatomy

```
● ──────────────────────────────────────── ✏ 🗑
│  [toggle] Rule name               [source badge] [severity] [interval]
│           Last fired 3 min ago  ·  Cooldown active
```

- **Status dot** (leftmost, 8 px circle):
  - Grey = `Unknown` or `Skipped`
  - Green = `Ok`
  - Amber pulsing = `Cooldown` (CSS `animation: pulse 2s infinite`)
  - Red pulsing = `Firing`
  - Yellow = `Error`
- **Enable/disable toggle**: native `<input type="checkbox" role="switch">` (matches pattern from `NamespaceMonitorSelector.razor`) — calls `OnToggle`.
- **Rule name**: truncated with title tooltip on overflow.
- **Source badge**: small pill `<span class="rule-source-badge rule-source-aks">☸ AKS</span>` — colour-coded per source group (CSS custom properties, consistent with connection dots in `StatusBar`).
- **Severity badge**: `⚠ Warning` (amber) or `🔴 Critical` (red) — same visual weight as the production badge in `ConfirmDialog`.
- **Interval chip**: `60s` — right-aligned, muted text.
- **Meta line** (below name, small text):
  - When `Firing`: `"Firing — {message truncated}"`
  - When `Cooldown`: `"Cooldown active — next check in ~{remaining}"`
  - When `Ok`: `"Last checked {time}"` or `"Never evaluated"` if `LastEvaluatedAt` is null
  - When `Skipped`: `"Skipped — source not connected"`
  - When `Error`: `"Evaluation error — see app notifications"`
- **Edit button** (`FluentIcon Size16 Edit`, icon-only, aria-label): calls `OnEdit`.
- **Delete button** (`FluentIcon Size16 Delete`, icon-only, aria-label): opens inline `<ConfirmDialog>` before calling `OnDelete`. Production flag not required here (deleting a monitoring rule is not a data-destructive action).

### Disabled row style

When `Rule.Enabled = false`: entire row opacity 0.5, status dot grey, meta line shows "Disabled".

---

## 6. `AlertRuleDrawer.razor`

### Concept

A right-anchored slide-over panel that overlays the rule list (not the full page). Slides in with CSS `transform: translateX(0)` / `translateX(100%)` transition (200 ms ease-out), matching the existing animation feel of `ResizablePanel` and `PortForwardSessionsPanel`.

### Parameters

```csharp
[Parameter] public bool IsOpen { get; set; }
[Parameter] public MonitoringAlertRule? Rule { get; set; }  // null = create mode
[Parameter] public EventCallback<MonitoringAlertRule> OnSave { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```

### Drawer header

```
← Back    Create alert rule    [×]
```

or in edit mode:
```
← Back    Edit: "{Rule.Name}"    [×]
```

Same header pattern as `AksDetailPanels.razor` — close `[×]` button top-right, title center/left. The `← Back` label is a ghost button (not a full back navigation — just closes the drawer).

### Form layout

Scrollable form body inside the drawer. Field groups use `<fieldset>` with `<legend>` to visually cluster related inputs — same as the existing `AppearanceSettings.razor` pattern.

#### Common fields (always visible)

| Field | Control | Notes |
|---|---|---|
| Name | `<input type="text" class="app-native-control">` | required, max 100 chars |
| Source | `<select class="app-native-control">` | changing source resets source-specific fields and shows/hides sections |
| Severity | `<div role="radiogroup">` two radio buttons | Warning (default) / Critical |
| Interval | `<input type="number" min="10">` + `s` suffix | default 60; clamped to ≥ 10 on blur |
| Cooldown | `<input type="number" min="1">` + `min` suffix | default 5 |
| Enabled | `<input type="checkbox" role="switch">` | default true |

#### AKS section (shown when Source is an AKS variant)

```
<fieldset>
  <legend>AKS</legend>
  Namespace: [____________]  (hint: "Leave blank for all namespaces")
  [conditional: Restart threshold] [conditional: Health score %]
</fieldset>
```

- `Restart threshold` only shown for `AksPodRestartRate` — number, default 5.
- `Health score threshold %` only shown for `AksNamespaceHealthScore` — range 1–100, default 25. Use `<input type="range">` + numeric display side-by-side (same as existing slider usage).

#### Service Bus section (shown for Service Bus sources)

```
<fieldset>
  <legend>Service Bus</legend>
  Namespace: [____________]  (hint: "Alias from configured namespaces")
  Entity path: [____________]  (hint: "queue-name  or  topic/Subscriptions/sub")
  Threshold: [____________] messages
</fieldset>
```

Namespace field: plain text for now; a future enhancement can replace with a `<select>` populated from `AppStateService.ServiceBusNamespaces`.

#### Redis section

```
<fieldset>
  <legend>Redis</legend>
  Connection: [____________]
  [conditional: Memory % threshold]  [conditional: Min client count]
</fieldset>
```

#### Storage section

```
<fieldset>
  <legend>Storage</legend>
  Account: [____________]
  Container: [____________]
  Blob count threshold: [____________]
</fieldset>
```

### Validation

Inline validation using the existing `<ErrorCallout>` shared component below each field group. Errors appear on blur or on save attempt. Do NOT show errors before the field has been touched.

| Field | Rule |
|---|---|
| Name | Required; max 100 chars |
| Namespace (AKS) | No validation — blank is valid (means all namespaces) |
| Namespace alias (SB/Redis/Storage) | Required when source type is selected |
| Entity path (SB) | Required for SB sources |
| Thresholds | Must be > 0 |
| Interval | ≥ 10 s |
| Cooldown | ≥ 1 min |

### Footer

```
[Cancel]   [Save rule]
```

- `[Cancel]` ghost button — calls `OnClose`.
- `[Save rule]` primary button — disabled when form has validation errors. On click: validate → `OnSave.InvokeAsync(built rule)` → drawer closes.
- Button pair is fixed to the drawer bottom (sticky footer, same as `PortForwardStartDialog.razor` footer).

### Focus management

On open: focus the Name field (via `ElementReference` + `JS.InvokeVoidAsync("SwebKit.setFocus", _nameRef)`).  
On close: return focus to the trigger element (use `SwebKit.saveFocus` / `SwebKit.restoreFocus` pattern from `Modal.razor`).

---

## 7. `AlertHistoryPanel.razor`

### Purpose

Right-column panel showing in-session alert firings. Mirrors the existing `AlertHistoryPanel.razor` in `Components/Aks/` (which shows pod events) but operates on `AlertFiredEvent` instead of `PodHealthEvent`.

### Parameters

```csharp
[Parameter] public IReadOnlyList<AlertFiredEvent> Events { get; set; } = [];
[Parameter] public EventCallback OnClear { get; set; }
[Parameter] public EventCallback<string> OnMuteRule { get; set; }  // rule ID → snooze 30 min
```

### Header

```
Alert History   [3]       [Clear ×]
```

Count badge (hidden when empty), Clear button. Same header style as existing `AlertHistoryPanel.razor` in `Components/Aks/`.

### Event row

```
│  ◈  Pod health — prod-ns        AKS  ·  3 min ago
│     Pod api-pod-abc: CrashLoop
│                                  [Snooze 30m]
```

Left border color:
- Amber (`--color-warning`) for `Severity.Warning`
- Red (`--color-critical`) for `Severity.Critical`

Fields:
- Source icon + rule name (bold, truncated)
- Source group badge (same pill as `AlertRuleRow`)
- Relative time (tooltip = full ISO timestamp)
- Message (second line, muted, truncated at 80 chars with title tooltip)
- **Snooze chip** (`[Snooze 30m]`): inline ghost button, right-aligned — calls `OnMuteRule(evt.RuleId)`. This lets operators suppress a noisy rule without leaving the history panel.

### Empty state

```
<EmptyState Icon="✓" Title="No alerts this session" />
```

### Live updates

`MonitoringPage` passes the `_history` list as the `Events` parameter. The panel re-renders automatically via the parent `StateHasChanged` call (BL-2 compliance — no direct event subscription in this component).

---

## 8. Status Bar Integration

### Monitoring pill in `StatusBar.razor`

Add after the port-forward count button:

```razor
@if (_monitorFiringCount > 0)
{
    <button class="status-bar-btn status-bar-btn--alert" title="@_monitorFiringCount alert(s) firing — click to open Monitoring"
            aria-label="@_monitorFiringCount alerts firing"
            @onclick="NavigateToMonitoring">
        <FluentIcon Value="@(new Icons.Regular.Size16.AlertOn())" Width="12px" />
        @_monitorFiringCount alert@(_monitorFiringCount == 1 ? "" : "s")
    </button>
}
else if (_monitor.IsMonitoring)
{
    <span class="status-bar-chip status-bar-chip--monitoring" title="Monitoring active">
        <FluentIcon Value="@(new Icons.Regular.Size16.AlertOn())" Width="12px" />
    </span>
}
```

- `_monitorFiringCount` = count of rules currently in `Firing` state (updated from `IAlertMonitorService.AlertFired`).
- When firing > 0: pulsing amber button (same `.status-bar-btn` pattern as port-forward) — navigates to `/monitoring` on click.
- When monitoring is active but nothing firing: a quiet icon chip (no label, no pulse), just presence indication.
- When monitoring is off: no status bar entry — no visual noise.
- `StatusBar.razor` subscribes to `IAlertMonitorService.AlertFired` via `IAppEventBus` (or direct event) and updates `_monitorFiringCount` via `await InvokeAsync(StateHasChanged)` (BL-2).

### Design token for alert colors

Add to the existing CSS custom properties:
```css
--color-alert-warning: var(--warning-color, #b45309);
--color-alert-critical: var(--error-color, #dc2626);
--color-alert-dot-ok: #22c55e;
--color-alert-dot-cooldown: #f59e0b;
--color-alert-dot-firing: #ef4444;
--color-alert-dot-skipped: #6b7280;
```

These reuse the existing `--warning-color` and `--error-color` tokens already established by `ConfirmDialog` and `StatusBar` CSS. New variables are additive only.

---

## 9. Design Consistency Notes

| Concern | Approach |
|---|---|
| Page structure | `RoutePageHeader` + `PageToolbar` + content — identical to `IncidentTimelinePage` and `ObservabilityPage` |
| Empty states | `<EmptyState>` shared component — no one-off placeholder markup |
| Loading | `<SkeletonRows Count="4" />` during initial load — same as `PipelineTree` |
| Confirmation on delete | `<ConfirmDialog>` inline — same as message delete in `MessageListView` |
| Slide-over drawer | Right-anchored, CSS slide transition, focus trap via `SwebKit.trapFocus` — same pattern as `Modal.razor` |
| Resizable history panel | `<ResizablePanel>` with resize handle on left — same as AKS side-panel rail |
| Status dots | 8 px circles, CSS custom property colors, pulse animation via `@keyframes` — consistent with `connection-dot` in `StatusBar` |
| Badges/chips | BEM modifier classes `rule-source-badge--aks` etc.; use `--accent-color` family for fills — consistent with entity status badges in `EntityTree.razor` |
| Icon buttons | `FluentIcon Size16`, `aria-label`, no visible label — consistent with row actions in `MessageListView` and `DeploymentGrid` |
| Form controls | `app-native-control` class on all `<input>` and `<select>` — consistent with `NamespaceMonitorSelector`, `PortForwardStartDialog` |
| Status bar additions | Same `.status-bar-btn` and `.status-bar-chip` class families as port-forward button — no new CSS primitives |

---

## 10. Pitfall Compliance

| Pitfall | Mitigation |
|---|---|
| BL-1 — missing `@using` | Add `@using SwebKit.App.Components.Monitoring` to `_Imports.razor` when creating the first component in the folder. |
| BL-2 — `StateHasChanged` in async callback | `AlertFired` event handler in `MonitoringPage` and `StatusBar` both call `await InvokeAsync(StateHasChanged)`. |
| BL-3 — guard set before `await` | `MonitoringPage.OnInitializedAsync` sets `_loading = true` guard before any `await`. |
| editing-notes — `SwebKit.App.Tests.csproj` entries | Add `<RazorComponent Include="...">` for all 5 new Monitoring `.razor` files. |

---

## 11. AKS Page Cleanup

Once the Monitoring tab is delivered:

- `src/SwebKit.App/Components/Aks/AlertHistoryPanel.razor` — **replace** with a wrapper that adapts `PodHealthEvent` into `AlertFiredEvent` and delegates to the new `Monitoring/AlertHistoryPanel`, OR delete and remove the AKS-specific history panel from `AksDetailPanels.razor` entirely (since history is now global in the Monitoring tab).
- `src/SwebKit.App/Components/Aks/NamespaceMonitorSelector.razor` — **delete** (namespace monitoring config moves to the Monitoring tab rule editor).
- `src/SwebKit.App/Components/Pages/AksPage.razor` — remove any direct `IPodHealthMonitorService` injection and `NamespaceMonitorSelector` usage.
- `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs` — rewire from `IPodHealthMonitorService.PodHealthDetected` to `IAlertMonitorService.AlertFired`.
- `StatusBar.razor` — remove existing `PodHealthMonitorService`-specific event subscription if present.
