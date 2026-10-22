# Dashboard service status honesty

State: In Progress

## Goal

Stop the dashboard from claiming states that aren't true:

- **AKS** showed "Connected" after a ~1s API ping while the namespace list (the
  real gate for the AKS page, ~18s cold on a big cluster) was still loading.
  "Connected" must mean _usable_: ping OK **and** namespaces resolved.
- **SQL and Redis** eager-probed every configured entity at app start
  (`useServiceHealth` is mounted in `AppLayout`, so it runs app-wide, not just
  on the dashboard). These are on-demand connections — there is no persistent
  connection to keep warm — so "Unavailable"/"Degraded" was noise that also
  pinned the header status dot red permanently.

## Scope

- `useServiceHealth` (`web/src/components/dashboard/`): new connectivity states
  `connecting` (AKS ping OK, namespaces in flight) and `on-demand` (configured
  but never probed — SQL, Redis; also AKS when
  `warmupConnectionsOnStartup` is off).
- SQL/Redis: drop the per-entity probe queries entirely; cards list entity
  names with neutral dots. The settings pages' explicit "Test connection"
  buttons keep working (same sidecar endpoints, same query keys).
- AKS: `connected` requires the `["aks-namespaces", ctx]` query to have
  resolved — shared key with `useWorkspaceWarmup`'s prefetch, so it piggybacks
  on the one in-flight request rather than adding a second.
- `WatchTiles` Cache Hit Rate: subscribe to `["redis", id, "info"]` with
  `enabled: false` — shows ratios for caches actually browsed this session,
  "-" otherwise. Never fires a request for the tile.
- Service Bus + Storage keep live probes (feeds the Containers tile; unchanged).

## Non-goals

- No sidecar changes; all probe endpoints stay.
- AKS page toolbar "Connected/Disconnected" pill keeps its ping semantics.
- No probing opt-in per service beyond the existing warmup setting.

## Implementation tasks

- [x] Plan file
- [x] `useServiceHealth.ts` rework + exported pure `deriveAksConnectivity`
- [x] `ServiceGrid.tsx` status meta/icons for new states
- [x] `AppLayout.tsx` popover labels + statusTone
- [x] `WatchTiles.tsx` redis hit ratio via cache-only subscriptions
- [x] `useServiceHealth.test.ts` unit tests for the AKS state machine
- [x] `layout.spec.ts`: Redis/SQL expect "On demand"
- [x] Validate: web build, vitest, dashboard/layout playwright specs
- [x] `docs/pitfalls/react-frontend.md` entry (TanStack Query section)

## Validation results

- `npm run build` (tsc -b && vite build) — clean.
- `npx vitest run useServiceHealth.test.ts` — 8/8 pass.
- `npx playwright test e2e/layout.spec.ts e2e/dashboard.spec.ts` — 19/21 on the
  first run; the 2 failures (demo-tour URL race on `?ns=`, pinned-rail timing)
  are pre-existing flakes unrelated to this change — both pass on rerun.

## Test plan

- Unit: `deriveAksConnectivity` — not-configured, warmup-off → on-demand,
  test pending → checking, test fail → unavailable + detail, test OK + ns
  pending → connecting ("Loading namespaces…"), test OK + ns error → degraded,
  test OK + ns loaded → connected ("N namespaces").
- E2E (demo mode): status popover shows Redis/SQL "On demand", AKS still
  reaches "Connected" (demo namespaces resolve).
- Manual: against a real profile — AKS card shows "Connecting…" until the AKS
  page's namespace selector resolves; SQL card shows "6 connections / On
  demand" with neutral dots and zero `/api/sql/*/test` calls.

