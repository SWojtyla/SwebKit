# Test Plan — AKS Port-Forward Sessions Panel

## Validation strategy

Unit tests for `IPortForwardSessionService` state machine. Manual verification for process lifecycle and UI behaviour.

## Unit tests

- `AddSession` adds a session with `Starting` status
- `UpdateStatus` transitions session to `Active`, `Stopping`, `Stopped`, `Error` correctly
- `StopSession` sets status to `Stopping` and invokes cancellation
- `GetActiveSessions` returns only non-stopped/error sessions
- Concurrent session additions are thread-safe

## Main scenarios

### Session start

| Scenario | Expected |
|---|---|
| User opens port-forward dialog from pod context menu | Dialog shows remote port pre-filled, local port editable |
| User confirms | Session registered as `Starting`; `kubectl port-forward` process launched |
| Process connects successfully | Session transitions to `Active`; shown in sessions panel |

### Sessions panel

| Scenario | Expected |
|---|---|
| No active sessions | Panel shows empty state |
| 1+ active sessions | Each row shows pod name, port mapping, started time, Stop button |
| User clicks Stop | Session transitions to `Stopping` → `Stopped`; row removed from active list |

### Error handling

| Scenario | Expected |
|---|---|
| Local port already in use | Session transitions to `Error`; row shows error state with stderr message |
| kubectl process exits unexpectedly | Session transitions to `Error`; error message shown in tooltip |

### Status bar integration

| Scenario | Expected |
|---|---|
| Session becomes Active | Status bar count increments |
| Session stops | Status bar count decrements; hidden at zero |
| Click count in status bar | Sessions panel opens / scrolls into view |

### App exit cleanup

| Scenario | Expected |
|---|---|
| App closed with active sessions | All `kubectl` processes are killed (not orphaned) |

## Regression risks

- Process tree kill must not leave orphaned `kubectl` processes on Windows
- CancellationToken must propagate from service to the process wrapper

## Acceptance criteria

- Port-forward session visible in panel after starting
- Stop button terminates the kubectl process within 2 seconds
- Error state surfaced in UI within 3 seconds of process exit
- No orphaned processes after app exit
- Status bar count reflects live session count
