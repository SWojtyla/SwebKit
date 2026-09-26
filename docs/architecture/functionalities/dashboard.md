# Dashboard

## Purpose

The dashboard is the React application's AI Cockpit and the initial `/` route. It gives an operator one place to:

- ask the global agent a workspace-aware question;
- see sidecar and agent status;
- check connectivity for every configured service and resource;
- inspect a few live operational metrics;
- navigate the declared workspace topology;
- open proactive AI reports; and
- return to pinned feature shortcuts.

It is intentionally an operational overview, not a customizable widget framework. The removed MAUI dashboard's saved views, tile registry, footprint editor, and local event-bus activity feed are not part of the current product.

## Supported Surfaces

### Agent entry and status

- `CockpitCommandBar` queues a prompt through `useAgentPanelStore.queuePrompt` and opens the shared global agent panel. The panel owns the single `useAgentChatStream` instance, so the dashboard does not create a second conversation or stream.
- `AgentStatusStrip` shows sidecar health/version, a preview of the latest assistant reply from `useAgentConversationStore`, and a pending-approval banner. Selecting the preview or banner opens the global panel.
- The header exposes the guided demo tour, cross-feature agent demo, and demo-mode toggle.

### Service health

`ServiceGrid` renders one card for each live feature area:

- Service Bus
- AKS
- Redis
- Storage
- SQL

`useServiceHealth` probes every configured entity with `useQueries`; it does not silently sample the first namespace, cache, account, or connection. Status is summarized as `not-configured`, `checking`, `connected`, `degraded`, or `unavailable`. Per-entity rows make partial failure visible, and the queries reuse the same React Query keys as the feature pages where practical.

Each card navigates to its feature page and can persist a service-level `FavoriteResource` through `useTogglePinnedResource`. `PinnedShortcuts` renders those favorites honestly as navigation shortcuts.

### Live watch

`WatchTiles` links to live feature views and currently reports:

- AKS deployments in the configured/default-discovered namespace;
- AKS pods in that namespace;
- total containers across all configured Storage accounts; and
- mean Redis cache hit ratio across all configured caches.

When an AKS default namespace is configured, the dashboard uses it directly. Otherwise it discovers the first available namespace; it never blindly polls a literal `default` namespace.

### Workspace topology

`CockpitTopology` renders the selected configured workspace map with the shared Cytoscape-backed `TopologyGraph`. It uses the same `buildGraphElements` conversion as the Settings map editor, supports multiple map selection, colors nodes by resource area, and routes node clicks to the corresponding feature page. Map editing remains under `/settings/map`.

### Proactive insights

`DashboardPage` subscribes to the monitoring stream through `useMonitoringStream` and stores bounded, dismissible insight events with `useProactiveInsightsFeed`. `InsightsFeed` derives the persisted proactive report ID and navigates to `/monitoring?tab=reports&report=<id>`. It does not fabricate an assistant exchange; the real report-to-chat handoff lives on the Monitoring report page.

## Runtime Flow

```text
App route `/`
  → DashboardPage
      → useProfile / useDemoMode
      → useServiceHealth
          → per-resource React Query probes
          → summarized ServiceHealth map
      → useMonitoringStream
          → useProactiveInsightsFeed
      → CockpitCommandBar → agent-panel store → GlobalAgentPanel
      → ServiceGrid / WatchTiles
      → CockpitTopology → shared TopologyGraph
      → InsightsFeed → Monitoring report deep link
      → PinnedShortcuts → feature routes
```

The page is composition-only: feature-specific data loading and presentation live in focused dashboard components rather than a single page-level state object.

## State and Persistence

- React Query owns remote profile, health, metric, and pending-approval state.
- `useAgentPanelStore` owns whether the global panel is open and any queued dashboard prompt.
- `useAgentConversationStore` supplies the shared conversation preview.
- Pinned shortcuts are persisted in the active profile's `favoriteResources` collection.
- Workspace maps are persisted in `AppConfig.Maps` and edited in Settings.
- Proactive-insight dismissal is session-oriented feed state; persisted report content belongs to Monitoring.
- Demo mode is sidecar state toggled through `useToggleDemoMode`.

## Main Code Locations

- `web/src/App.tsx` — lazy `/` route registration
- `web/src/components/dashboard/DashboardPage.tsx` — cockpit composition
- `web/src/components/dashboard/CockpitCommandBar.tsx` — global-agent prompt queue
- `web/src/components/dashboard/AgentStatusStrip.tsx` — sidecar, agent preview, approvals
- `web/src/components/dashboard/ServiceGrid.tsx` — consolidated service/resource cards
- `web/src/components/dashboard/useServiceHealth.ts` — per-entity connectivity aggregation
- `web/src/components/dashboard/WatchTiles.tsx` — live metric links
- `web/src/components/dashboard/CockpitTopology.tsx` — workspace graph and navigation
- `web/src/components/dashboard/InsightsFeed.tsx` — proactive report routing
- `web/src/components/dashboard/PinnedShortcuts.tsx` — persisted shortcut links
- `web/src/lib/stores/agent-panel.ts` — open state and queued prompts
- `web/src/lib/stores/agent-conversation.ts` — shared global conversation
- `web/src/components/shared/TopologyGraph.tsx` — Cytoscape graph renderer

## Important Constraints

- Keep one agent stream owner. Dashboard entry points must queue into the global panel instead of creating another `useAgentChatStream` instance.
- Health cards must represent every configured entity and preserve the degraded state when only some probes succeed.
- New dashboard queries should share feature query keys or hooks to avoid duplicate requests.
- Keep failures independent: one slow or unavailable integration must not block the remaining cards.
- Dashboard actions must deep-link to genuine feature workflows; they must not simulate successful investigations or operations locally.
- Configuration-readiness details belong in Settings. The dashboard should provide concise status and a clear route to configure connections.

## Validation Pointers

- `web/e2e/dashboard.spec.ts` — cockpit load, navigation, demo mode, health, watches, pins, tours
- `web/e2e/dashboard-deferred.spec.ts` — pending approvals
- `web/e2e/workspace-resume.spec.ts` — dashboard/startup route restoration
- `web/e2e/navigation.spec.ts` — sidebar route coverage
- `web/src/lib/hooks/use-agent.test.ts` — pending-action feed reconciliation

Run from `web/`:

```bash
npm run typecheck
npm run test
npx playwright test e2e/dashboard.spec.ts e2e/dashboard-deferred.spec.ts
```
