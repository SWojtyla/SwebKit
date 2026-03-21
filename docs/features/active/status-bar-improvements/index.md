# Feature Overview — Status Bar Improvements

---

title: "Status Bar Improvements"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Expand the status bar from a near-empty footer into a persistent, always-visible context strip that shows the active environment, per-service connectivity, last refresh time, and active background sessions.

## Value

The status bar is always visible regardless of which feature is open. Making it information-dense gives users a constant, unobtrusive summary of the application's current state and reduces the need to navigate away to check if something is connected or broken.

## Scope

### In scope

1. **Active environment name** — Show `[Project] / [Environment]` at the left of the status bar. When `IsProduction = true`, render with a red background (matching the existing prod colour token `--color-prod`) and a "PROD" badge. This mirrors the planned TopBar production indicator.

2. **Per-service connection state** — Small icon+label indicators for each configured service (Service Bus, AKS, Redis, Storage). States: `connected` (green dot), `error` (red dot + tooltip with error message), `not configured` (grey dot). Updated after each successful or failed client operation via the event bus.

3. **Last refresh timestamp** — Show "Refreshed Ns ago" for the currently active feature area. Updated when a `RefreshRequestedEvent` completes.

4. **Active port-forward count** — Show "⇄ N port-forward(s)" when one or more AKS port-forward sessions are active. Clicking opens the port-forward sessions panel (see `aks-port-forward-sessions` feature). Hidden when count is zero.

5. **Background task indicator** — The existing task spinner is retained and refined: show task name alongside the spinner when exactly one task is running; show count when multiple.

### Out of scope

- Clickable connection state indicators that navigate to settings (too much in scope for this change)
- Per-namespace or per-cluster connection details
- Notification history accessed from the status bar

## Dependencies

- `AppStateService` — current project/environment, `IsProduction`
- `IAppEventBus` — refresh events, connection state change events
- `ITaskQueue` — background task count (already wired)
- `aks-port-forward-sessions` feature — for the port-forward count and click target
- New `IConnectionStateService` or equivalent event to broadcast service connectivity changes

## Risks

- Connection state tracking requires components to emit success/failure events after client calls — this is a cross-cutting concern that needs a lightweight convention rather than invasive changes to all feature pages.
- The status bar renders inside `MainLayout` — it must not cause full-page re-renders on every update; use `InvokeAsync(StateHasChanged)` scoped to the component only.

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- AKS port-forward sessions: `docs/features/active/aks-port-forward-sessions/`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
