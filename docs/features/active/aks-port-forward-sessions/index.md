# Feature Overview — AKS Port-Forward Sessions Panel

---

title: "AKS Port-Forward Sessions Panel"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Add a persistent port-forward sessions panel that tracks all active `kubectl port-forward` sessions started from SwebKit, allows users to stop individual sessions, and makes active forwarded ports visible from anywhere in the app.

## Value

Currently, port-forward sessions are fire-and-forget: once started, there is no record in the UI of what is running on which local port. Developers have to remember or check externally. A sessions panel closes this gap, turning port-forwarding from a hidden background action into a visible, manageable resource.

## Scope

### In scope

1. **Session tracking service** (`IPortForwardSessionService`) — Singleton service that holds the list of active port-forward sessions. Each session record contains:
   - Session ID (GUID)
   - Pod name, namespace, container (if applicable)
   - Remote port, local port
   - Started-at timestamp
   - Status: `Starting`, `Active`, `Stopping`, `Stopped`, `Error`
   - The `CancellationTokenSource` used to stop the process

2. **Sessions panel in the AKS page** — Collapsible panel (drawer-style, bottom of the AKS page or slide-in from right) listing all active sessions. Each row shows: pod name, `localhost:{localPort} → {remotePort}`, started time, Stop button.

3. **"Start port-forward" workflow improvement** — When the user triggers port-forward from a pod context menu, a small dialog asks for local port (pre-filled with remote port) and then registers the session with the service before starting the process.

4. **Status bar integration** — Active session count shown in the status bar (see `status-bar-improvements` feature). Clicking the count opens the sessions panel.

5. **Session cleanup on app exit** — All active sessions are cancelled during `MauiProgram` application lifecycle teardown.

6. **Error handling** — If the `kubectl port-forward` process exits unexpectedly, the session transitions to `Error` state with the last stderr output shown in a tooltip.

### Out of scope

- Multiple simultaneous port-forwards to the same pod/port (guard against duplicates)
- Port-forward to services or deployments (pods only for now)
- Persistent port-forward sessions across app restarts
- Automatic port conflict detection (user is responsible for choosing a free local port)

## Dependencies

- Existing `IAksClient.PortForwardAsync` — wraps the `kubectl` process; needs to accept and honour a `CancellationToken`
- New `IPortForwardSessionService` + implementation in `SwebKit.Kubernetes`
- `StatusBar.razor` — for session count (see `status-bar-improvements` feature)
- `AksPage.razor` — sessions panel render and toggle
- `IAppEventBus` — for broadcasting session state changes to the status bar

## Risks

- `kubectl port-forward` process lifetime: the process must be correctly killed (not just abandoned) when cancellation is requested; use `process.Kill(entireProcessTree: true)`.
- Race condition on session stop: user clicks Stop while the process is still in `Starting` state; the service must queue the cancellation and apply it once the process handle is available.
- Port conflicts: if the user picks a port already in use, `kubectl` will immediately exit with an error; this must be surfaced in the session row, not silently swallowed.

## Related documents

- Architecture: `docs/architecture/functionalities/aks.md` (update after implementation)
- Status bar: `docs/features/active/status-bar-improvements/`
- Pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
