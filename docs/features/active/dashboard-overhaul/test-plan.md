# Test Plan — Dashboard Overhaul

## Validation strategy

Primarily manual UI verification. Unit tests for health data aggregation logic if extracted into a service.

## Main scenarios

### Health tiles

| Scenario | Expected |
|---|---|
| All features configured and reachable | Tiles show live health data (DLQ count, unhealthy pods, etc.) |
| Feature not configured | Tile shows "Not configured" callout with Settings link |
| Feature client throws on load | Tile shows error state; other tiles unaffected |
| Load completes | Each tile shows "Last updated N seconds ago" |
| 60-second interval elapses | All tiles refresh automatically |

### Activity feed

| Scenario | Expected |
|---|---|
| App freshly opened | Feed is empty or shows placeholder |
| User peeks messages, restarts a pod, etc. | Actions appear in feed in reverse chronological order |
| More than 10 events | Oldest events scrolled out of view (or truncated at 10) |

### Pinned items

| Scenario | Expected |
|---|---|
| No pins configured | "Pin items in Settings" empty state prompt shown |
| Pins configured | Items displayed; clicking navigates to the entity |

### Unconfigured callouts

| Scenario | Expected |
|---|---|
| Service Bus has no namespaces | Callout shown on Service Bus tile |
| All features configured | No callouts shown |

## Regression risks

- Dashboard fetch errors must not crash other feature pages
- Auto-refresh must not interfere with user actions on other pages while dashboard is not visible

## Acceptance criteria

- All health tiles load within 3 seconds of page open (parallel fetch)
- Error in one tile does not block others
- Activity feed updates in real-time as actions are taken
- Pinned item click navigates correctly
- Auto-refresh fires without user interaction
