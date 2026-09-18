# Status — UX Polish

**Status:** `In Progress`

## Module board

| Module | Status | Notes |
| ------ | ------ | ----- |
| 1. AKS context switching & namespaces | Done | committed b84a098 on sw/settings-profiles-aks-shell-fixes |
| 2. Startup warm-up & resume | Done | committed 46c2168 |
| 3. Page restore & deep-link parity | Done | committed 3fabd6f; full suite green |
| 4. Consistency & polish sweep | Review | implemented + verified; awaiting user review |

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

### Module 2 — startup warm-up & resume (`web/`, `src/`)

- `useWorkspaceWarmup` (new, mounted in `AppLayout` — app startup is the trigger,
  independent of which page loads): prefetches `aks-contexts`, ctx-scoped
  `aks-namespaces`, `aks-deployments` for the restorable namespace, and
  `sb-queues`/`sb-topics` for the first Service Bus namespace. Honors the
  pre-existing `warmupConnectionsOnStartup` toggle (rendered in settings but
  never consumed by the React app until now). Footer health probes and
  selection-dependent data are deliberately not duplicated.
- Route restore: `view-pref:last-route` captured lazily at mount (before the
  save effect can overwrite it), restored via `navigate(replace)` once settings
  resolve and only while still on `/`. Every navigation — `/` included — is
  truthfully saved.
- New `UserSettings.RestoreLastWorkspaceOnStartup` (C# default `true`) +
  `restoreLastWorkspaceOnStartup` TS field + "Restore last workspace on launch"
  checkbox in General → Startup.
- `areaHealth` verified already rendered per-area in the status bar — no
  nav-dot change needed.

### Module 3 — page restore & deep-link parity (`web/`)

- `useUpdateSearchParams` extracted from `AksWorkspaceContext` into a shared
  hook (live-URL-based writes, `null` deletes); now used by AKS, Storage,
  Redis, SQL, Monitoring.
- **Storage** (`StoragePageContext`): `?account/?container/?prefix/?blob/?view`
  drive selection; `prefixHistory` derived from the prefix (deep links get full
  breadcrumbs); `storage-last-account`/`storage-last-container:<id>` persisted
  on any location change and validated on restore; palette `state.accountId`
  still works. `StoragePage` empty state → Settings → Storage CTA.
- **Redis** (`RedisPageContext`): `?tab=` for all 7 tabs; per-cache
  `redis-last-pattern:<cacheId>` restored on first resolve and on switch;
  `activeCacheId` profile write-through confirmed. `RedisPage` empty state →
  Settings → Redis CTA.
- **SQL** (`SqlPage`): `?connection` settles on the resolved id and is written
  back on change; profile `activeConnectionId` remains the persistence.
- **Monitoring** (`MonitoringPage`): `?tab=` for rules|history.
- **Service Bus** (`ServiceBusPage`): empty state → Settings → Service Bus CTA.

### Module 4 — consistency & polish sweep (`web/`)

- `SearchableSelect` shared component (`web/src/components/shared/SearchableSelect.tsx`):
  trigger + filter + full keyboard nav + Escape + outside-click + focus return,
  `aria-haspopup`/`aria-expanded`/`aria-activedescendant`, optional `sr-only` native
  `<select>` for Playwright/screen-reader parity. Adopted by Redis cache, Storage
  account, SQL connection (server subtitle), Service Bus namespace; `ContextSelector`
  is now a thin MRU/pending-label wrapper.
- Signal audit: every live `queryFn` destructures `{ signal }`; api.ts helper wrappers
  take optional `AbortSignal`; raw `fetch` in `useAksResourceYaml` passes it too.
  Zero remaining `queryFn: () =>` hits in `web/src`.
- Mutation audit: `useAksHelmRollback` → `useNotifyMutation` (was silent on both
  success and error). Apply/validate YAML, port-forward/shell, storage mutations and
  `useTogglePinnedResource` verified to notify or surface errors inline.
- Loading/empty/error: Storage container list + Redis key-browser branches →
  `QueryState`; Monitoring/SB already used shared primitives.
- Disabled controls: ~70 audited — every disabled button/input now shows a
  conditional `title` reason.
- De-flaked monitoring snooze spec (waits on `history.or(empty)` after the
  URL-driven tab switch).

## Validation

- `dotnet build` sidecar + app: 0 warnings, 0 errors.
- `SwebKit.Sidecar.Tests`: **466/466 pass** (10 new: pool explicit-context,
  context-POST failure paths, demo contexts, profile-save eviction).
- `tsc --noEmit`: clean. `vitest run`: **473/473 pass**.
- Playwright: **all AKS specs pass** — 7 new in `e2e/aks-context-switch.spec.ts`
  (demo contexts, Escape, per-context ns restore, failed/throwing switch,
  switching-stage label, first-run empty state) + 27 existing across
  `aks*.spec.ts` with zero regressions.
- Module 2: **5 new** `e2e/workspace-resume.spec.ts` (warmup fires on
  non-dashboard landing, warmup toggle off, restore at `/`, restore off,
  deep-link safety); `SwebKit.Core.Tests` 1000/1000; dashboard/layout/
  monitoring/settings specs green (one assertion updated: `/api/aks/namespaces`
  now fires exactly once — the intentional warm-up call).
- Module 3: **9 new** `e2e/page-restore.spec.ts` (storage URL-driven position +
  reload, bare-visit container restore, deep-link breadcrumbs, redis
  tab/pattern, sql connection param, monitoring tab, storage/redis settings
  CTAs); regression sweep of 98 storage/redis/sql/monitoring/sb-url-state specs
  — all green; `aks-url-state` + `workspace-resume` re-verified after the
  shared-hook refactor.
- Module 4: **3 new** `e2e/searchable-select.spec.ts` (filter+keyboard select,
  Escape + focus return, SQL server subtitles); 73-spec selector regression
  sweep green; full Playwright suite **345/345**; vitest 473/473; sidecar
  466/466; `tsc` clean.
- Aikido MCP scan: **server not installed** in this environment — flagged to
  user; run `aikido_full_scan` on the changed files once configured.

## Docs updated

- `docs/pitfalls/react-frontend.md` — new entry: query keys that omit the
  server-side identity serve one backend's data under another's name.
- `docs/pitfalls/dotnet-csharp.md` — CS-10: pooled-client cache key and factory
  argument must describe the same target.
- `docs/pitfalls/react-frontend.md` — restore-on-launch reads must precede the
  save effect's first write (lazy mount capture).

## Next

- User review of Module 4; optional stretch items (palette context-switching,
  namespace MRU badges) remain unscheduled.
