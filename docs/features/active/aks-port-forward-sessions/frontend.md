# Frontend Plan — AKS Port-Forward Sessions Panel

## Affected files

- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor` — new
- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor.css` — new
- `src/SwebKit.App/Components/Aks/PortForwardStartDialog.razor` — new
- `src/SwebKit.App/Components/Pages/AksPage.razor` — add panel toggle + dialog wiring + updated port-forward action

## `PortForwardStartDialog.razor`

Shown when user selects "Start port-forward" from pod context menu.

Fields:
- Pod name (read-only, passed as parameter)
- Namespace (read-only)
- Remote port (read-only, detected from pod spec or entered manually)
- Local port (editable, pre-filled with remote port value)

Actions:
- **Start** — calls `IPortForwardSessionService.StartAsync`, closes dialog
- **Cancel** — closes dialog

## `PortForwardSessionsPanel.razor`

Collapsible panel rendered at the bottom of `AksPage.razor` (above the footer if any), or as a slide-in right drawer.

Layout:
- Header: "Port-Forward Sessions (N)" with a collapse toggle
- Empty state: "No active port-forward sessions"
- Session list: one row per session

Session row columns:
| Column | Content |
|---|---|
| Pod | Pod name (namespace in muted text below) |
| Mapping | `localhost:{localPort} → :{remotePort}` |
| Status | Badge: Starting (grey), Active (green), Stopping (amber), Error (red) |
| Started | Relative time ("2m ago") |
| Actions | Stop button (disabled when Stopping/Stopped) |

Error rows show an expand toggle that reveals the error message.

## `AksPage.razor` changes

1. Replace the existing bare `kubectl port-forward` invocation in the pod context menu action with an event that opens `PortForwardStartDialog`
2. Add `<PortForwardSessionsPanel>` at the bottom of the AKS page template (initially collapsed)
3. Add a "Sessions" toggle button in the AKS toolbar (shown when `ActiveSessions.Count > 0`)
4. Subscribe to `IPortForwardSessionService.SessionsChanged` → `StateHasChanged()`

## Status bar wiring

`StatusBar.razor` injects `IPortForwardSessionService`, reads `ActiveSessions.Count`, subscribes to `SessionsChanged`. Clicking the count fires a `NavigateToAreaEvent("aks")` plus an event to expand the sessions panel.

## CSS notes

- Panel is a bottom-anchored collapsible strip: `position: sticky; bottom: 0; background: var(--color-surface); border-top: 1px solid var(--color-border)`
- Session row uses flexbox with `align-items: center` and column widths via flex grow
- Status badge uses the standard badge pattern already present in AKS components

## Tasks

- [ ] Create `PortForwardStartDialog.razor`
- [ ] Create `PortForwardSessionsPanel.razor` with all session states
- [ ] Update `AksPage.razor`: dialog wiring, panel render, toolbar toggle
- [ ] Subscribe to `SessionsChanged` for live panel updates
- [ ] Wire status bar count (in `status-bar-improvements`)
- [ ] Write CSS for panel and session rows
