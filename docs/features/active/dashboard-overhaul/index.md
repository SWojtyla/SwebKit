# Feature Overview — Dashboard Overhaul

---

title: "Dashboard Overhaul"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Transform the home dashboard from a static link grid into a meaningful landing page that surfaces health signals, recent activity, and quick-access pinned items at a glance.

## Value

A developer opening SwebKit should immediately see what needs attention — dead-lettered messages, unhealthy pods, pending approvals — without navigating into each feature. The dashboard should reduce the time-to-first-insight from "open app, click around" to "open app, see problems".

## Scope

### In scope

1. **Health summary tiles** — One tile per feature area (Service Bus, AKS, Redis, Releases). Each tile shows a key health signal:
   - Service Bus: total DLQ message count across all watched namespaces
   - AKS: number of pods not in Running/Completed state across active context
   - Redis: number of keys near expiry (TTL < 5 minutes) if Redis is connected
   - Releases: count of deployments pending approval
   - Tiles show a loading state while data is fetched and a "not configured" state when no connection is set up.

2. **Recent activity feed** — A scrollable list of the last ~10 actions performed in the session (e.g. "Peeked 12 messages from orders-dlq", "Restarted deployment api-server", "Opened log stream for pod xyz"). Sourced from `IAppEventBus` events, held in memory for the session only.

3. **Quick-access pinned items** — Show pinned entities from `AppStateService` (pinned Service Bus queues/topics, favourite AKS contexts). Clicking navigates directly to the entity. Empty state prompts to configure pins in Settings.

4. **Unconfigured area callouts** — If a feature area has no connection configured, display an inline callout on its tile with a "Configure in Settings" link rather than a health metric.

### Out of scope

- Persistent activity history across sessions
- Customisable tile layout or drag-and-drop
- Metrics charts or trend graphs (separate future feature)
- Per-namespace or per-cluster breakdown tiles

## Dependencies

- `AppStateService` — project/environment config, pinned entities
- Existing feature clients (`IServiceBusClient`, `IAksClient`, `IRedisClient`) — called read-only for health snapshot data
- `IAppEventBus` — activity feed events
- `UiStateRepository` — optionally persist last health snapshot time

## Risks

- Health data fetches at page load could add latency; must be non-blocking and run in parallel
- Stale health data: tiles should show a "last updated N seconds ago" timestamp and auto-refresh every 60 seconds
- If any feature client throws, its tile must show an error state without crashing the page

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Settings: `docs/architecture/functionalities/settings-and-configuration.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
