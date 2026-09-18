# Status — UX Polish

**Status:** `In Progress`

## Module board

| Module | Status | Notes |
| ------ | ------ | ----- |
| 1. AKS context switching & namespaces | Review | implemented + verified; awaiting user review |
| 2. Startup warm-up & resume | Proposed | depends on Module 1 key scoping |
| 3. Page restore & deep-link parity | Proposed | Storage/Redis/SQL/Monitoring URL params + persisted selection |
| 4. Consistency & polish sweep | Proposed | SearchableSelect, signal audit, notify audit, a11y, QueryState |

## Done

- Feature folder scaffolded (index/status/test-plan + 4 module docs).
- Research: confirmed 10 concrete defects in the AKS context-switch path (see
  `aks-context-switching.md` for file:line evidence).
- **Module 1 implemented** (backend + frontend + tests), all checks green.

### Module 1 — backend (`src-sidecar`, `src/`)

- `SidecarMonitoringConnectionPool.GetAksClient(context)` now passes the explicit
  context to the client factory (was: profile context under the requested key).
- `POST /api/aks/context` tests the target context before persisting; returns
  `{ connected, error }` on failure and leaves the saved profile untouched. Demo
  switches return `connected: true` without writing synthetic contexts to the
  real profile. No more blanket `InvalidateStaleConnections()` on switch.
- `GET /api/aks/contexts` resolves through the pool so demo mode serves
  `DemoAksClient`'s five contexts (was: always read the real kubeconfig).
- `SaveProfileAsync` snapshots connection-relevant sections and invokes
  targeted `IMonitoringConnectionPool` eviction (AKS / SB / Redis) only when
  that configuration actually changed; unrelated profile saves keep warm
  clients + namespace caches.
- `IMonitoringConnectionPool` gained `EvictAksClient(context?)`,
  `EvictServiceBusClient(name)`, `EvictRedisClient(name)`; the App
  implementation and all test fakes updated.

### Module 1 — frontend (`web/`)

- Every AKS resource query key carries the resolved context:
  `["aks-pods", ctx, ns, …]`. Bootstrap keys (`aks-contexts`, `aks-test`,
  `aks-profile`) stay unscoped; structural queries got `staleTime: 5min`.
- `namespaceToken` gate: namespaced queries hold while a context switch is in
  flight, so cluster-B requests never fire against cluster A's client.
- `handleContextChange` restores the target context's persisted namespace
  (`view-pref:aks-selected-ns:<ctx>`), falls back to the kubeconfig namespace
  hint, and clears pod/YAML/helm/log/shell/detail state + `lastRefreshedAt`.
  Failed switches (connected:false or thrown) restore the previous `ns` and
  toast an error; success toasts only on `connected: true`.
- `ContextSelector`: pending-context label ("Switching to …"), cluster·ns
  subtitles, current-context marker, MRU ordering (`view-pref:aks-context-mru`,
  cap 5), `aria-haspopup`/`aria-expanded`, Escape + ArrowUp/Down/Enter.
- `NamespaceSelector`: explicit loading/error labels (no stale names), context
  breadcrumb in the dropdown, Escape, same aria/keyboard parity.
- `AksPage`: staged loading text ("Switching context…" / "Loading namespaces…"),
  `aks-first-run` EmptyState with a settings CTA when AKS is unconfigured.
- `useCommandPalette` prefix-scans cached namespace lists (keys are now
  context-scoped).

## Validation

- `dotnet build` sidecar + app: 0 warnings, 0 errors.
- `SwebKit.Sidecar.Tests`: **466/466 pass** (10 new: pool explicit-context,
  context-POST failure paths, demo contexts, profile-save eviction).
- `tsc --noEmit`: clean. `vitest run`: **473/473 pass**.
- Playwright: **all AKS specs pass** — 7 new in `e2e/aks-context-switch.spec.ts`
  (demo contexts, Escape, per-context ns restore, failed/throwing switch,
  switching-stage label, first-run empty state) + 27 existing across
  `aks*.spec.ts` with zero regressions.
- Aikido MCP scan: **server not installed** in this environment — flagged to
  user; run `aikido_full_scan` on the changed files once configured.

## Docs updated

- `docs/pitfalls/react-frontend.md` — new entry: query keys that omit the
  server-side identity serve one backend's data under another's name.
- `docs/pitfalls/dotnet-csharp.md` — CS-10: pooled-client cache key and factory
  argument must describe the same target.

## Next

- User review of Module 1, then Module 2 (startup warm-up & resume).
