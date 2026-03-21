# Frontend Plan — Status Bar Improvements

## Affected files

- `src/SwebKit.App/Components/Layout/StatusBar.razor` — significant update
- `src/SwebKit.App/Components/Layout/StatusBar.razor.css` — new or updated scoped styles
- `src/SwebKit.Core/Services/IConnectionStateService.cs` — new interface
- `src/SwebKit.Core/Services/ConnectionStateService.cs` — new implementation
- `src/SwebKit.Core/Events/ConnectionStateChangedEvent.cs` — new event type
- `src/SwebKit.App/MauiProgram.cs` — register `IConnectionStateService`

## Layout

The status bar is a single horizontal strip at the bottom of the app shell. New layout (left to right):

```
[Project / Environment] [PROD]    [SB ●] [AKS ●] [Redis ●] [Storage ●]    [Refreshed 12s ago]    [⇄ 2 port-forwards]    [⟳ task-name]
```

- Left: environment name + optional PROD badge
- Centre-left: per-service connection dots
- Centre-right: last refresh timestamp
- Right: port-forward count (hidden when 0) + background task indicator

## `IConnectionStateService`

Tracks the last-known connection state per feature area. API:
- `void SetConnected(string area)` — call after a successful client operation
- `void SetError(string area, string message)` — call after a failed client operation
- `void SetNotConfigured(string area)` — call on page init when no connection is set up
- `IReadOnlyDictionary<string, ConnectionState> States { get; }`
- `event Action? StatesChanged`

`ConnectionState` enum: `Unknown`, `Connected`, `Error`, `NotConfigured`

Each feature page injects `IConnectionStateService` and calls `SetConnected`/`SetError` after client calls.

## Environment name + prod badge

- Read `AppState.CurrentEnvironment` (or equivalent) in `StatusBar.razor`
- If `IsProduction`: render with `background: var(--color-prod)` on the left section; show "PROD" badge
- Subscribe to `AppState` change event (or `IAppEventBus`) to re-render on environment switch

## Per-service connection dots

- Render one dot per area: Service Bus, AKS, Redis, Storage
- Only show configured areas (skip if `NotConfigured` to avoid noise when a user doesn't use all features)
- Dot colours: `--color-success` (Connected), `--color-error` (Error), `--color-text-muted` (Unknown/NotConfigured)
- Tooltip on error dot: the error message from `ConnectionStateService`

## Last refresh timestamp

- Subscribe to `RefreshRequestedEvent` on the event bus
- Record `DateTime.UtcNow` on each event
- Display as relative time ("just now", "12s ago", "2m ago")
- Update the relative label every 10 seconds using a lightweight timer

## Port-forward count

- Inject `IPortForwardSessionService` (from `aks-port-forward-sessions` feature)
- Subscribe to `SessionsChanged` event
- Show count only when > 0
- Click navigates to AKS page and opens the sessions panel (via `NavigateToAreaEvent` with a sessions-panel flag)

## Tasks

- [ ] Define `IConnectionStateService` + `ConnectionStateService`
- [ ] Register service in `MauiProgram.cs`
- [ ] Update `StatusBar.razor` layout (environment, dots, refresh, port-forward, tasks)
- [ ] Wire environment name + prod badge from `AppStateService`
- [ ] Wire connection dots from `IConnectionStateService`
- [ ] Wire refresh timestamp from `RefreshRequestedEvent`
- [ ] Wire port-forward count from `IPortForwardSessionService` (conditional on that feature)
- [ ] Refine task indicator (show name for single task)
- [ ] Write scoped CSS
- [ ] Add `SetConnected`/`SetError` calls in Service Bus, AKS, Redis, Storage pages
