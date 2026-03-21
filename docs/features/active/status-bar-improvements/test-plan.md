# Test Plan — Status Bar Improvements

## Validation strategy

Manual UI verification. Unit test for environment name / prod state rendering logic if extracted.

## Main scenarios

### Environment name and prod badge

| Scenario | Expected |
|---|---|
| Non-production environment selected | Environment name shown; no red background |
| Production environment selected | Red background (`--color-prod`), "PROD" badge visible |
| No project/environment configured | Status bar shows neutral placeholder |

### Per-service connection state

| Scenario | Expected |
|---|---|
| Service Bus connected successfully | Green dot next to "Service Bus" |
| Service Bus auth failure | Red dot; hover tooltip shows error message |
| AKS not configured | Grey dot next to "AKS" |

### Last refresh

| Scenario | Expected |
|---|---|
| User presses F5 or clicks Refresh | Timestamp resets to "Refreshed just now" |
| Time passes | Timestamp shows "Refreshed Ns ago" |

### Port-forward count

| Scenario | Expected |
|---|---|
| No active sessions | Port-forward indicator hidden |
| 1 active session | "⇄ 1 port-forward" shown |
| Session stopped | Count decrements; hidden at zero |
| Click count | Port-forward sessions panel opens |

### Background task indicator

| Scenario | Expected |
|---|---|
| Single task running | Spinner + task name shown |
| Multiple tasks running | Spinner + "N tasks running" shown |
| No tasks | Indicator hidden |

## Regression risks

- Status bar re-renders must not cause full `MainLayout` re-render
- Port-forward count depends on `aks-port-forward-sessions` — must degrade gracefully if that feature is not yet complete

## Acceptance criteria

- Prod badge always visible when production environment is active
- Connection state updates within 2 seconds of a client call completing
- Port-forward count reflects live session state
- No visible layout shift when status bar content changes
