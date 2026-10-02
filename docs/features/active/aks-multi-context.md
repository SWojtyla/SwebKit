# AKS Multi-Context Workspace

State: Review

## Goal

Let the operator select **several kubeconfig contexts** on the AKS page, load each
cluster's namespaces, and browse all selected clusters in one merged resource view —
a Pods tab showing pods from prod-eu and staging side by side, each row tagged with
its cluster, each action routed back to the cluster the row came from.

There is **no primary context**. The picker is a plain multi-select: every checked
context feeds the merged view equally, and any of them — including the configured
one — can be deselected like the rest (minimum one stays checked). The profile's
configured context (`aksConfig.kubeconfigContext`) survives only as the **default**:
it seeds the initial selection, decodes legacy bare URL tokens, and remains the
target agent tools fall back to when no context is passed.

## Why this is cheaper than it looks

The plumbing is already multi-context-capable; the work is threading and workspace
UI, not new architecture:

- `IMonitoringConnectionPool.GetAksClient(context)` is **context-keyed** — monitoring
  rules already interrogate clusters other than the configured one
  (`SidecarMonitoringConnectionPool.cs`, `AksToolContext.cs`, `PodSignalSourceBase.cs`).
- `?context=` endpoint pattern is proven on `GetNamespacesAsync` /
  `GetDeploymentsAsync`; every other endpoint just calls `GetClient(pool)` and needs
  the same one-line treatment.
- Agent tools already accept an optional `context` argument (`AksToolContext`).
- `pod_shell.rs` and `native.rs` port-forward already accept `context`/`kubeconfig`
  params and pass `--context` to kubectl; `tauri-bridge.ts` forwards them.
- Every TanStack Query key already carries the ctx discriminator
  (`["aks-pods", ctx, ns]`); `useAksNamespaces`/`useAksDeployments` already take an
  optional `context` arg; invalidation is prefix-based so refresh works for free.
- Demo mode ships five demo contexts; `selectedNsPrefKey(ctx)` already stores
  per-cluster namespace selections.

## Scope

### Phase 1 — merged read-only browsing

**Workspace state / URL:**

- New `ctxs` search param: comma-separated `encodeURIComponent` context names —
  the **whole selection** (including the configured context). An absent `ctxs`
  falls back to `[configured]`; persisted in the `aks-selected-contexts` view pref
  so it survives navigation.
- `ns` param becomes composite: `enc(ctx):enc(ns)` per entry, `enc(ctx):*` for
  "all namespaces in that cluster", entries joined with `,`. **Back-compat:**
  entries without a `:` bind to the configured default context — old deep links and pinned
  resources keep working. `parseNamespaces`/`encodeNamespaces`/
  `parseKey`/`makeKey` gain the ctx segment (`ctx:ns/name` for `pod`/`yaml`/
  `helm`/`container`/`logsNs` params).
- `namespaceToken` becomes a per-context map `{ [ctx]: "a,b" | "*" }`; queries for
  a context only run once that context has a namespace selection (or `*`).

**Pickers:**

- `ContextSelector` → plain multi-select dropdown (checkbox list reusing the
  NamespaceSelector interaction pattern): clicking a row or its checkbox toggles
  membership; the trailing button (or Ctrl/Cmd+click) selects only that context.
  The last checked context can't be deselected. Selection does **no** POST and no
  upfront connection test — failures surface lazily on that context's namespace
  query. The old primary-switch flow (`POST /api/aks/context` + connection test)
  is gone from this surface.
- `NamespaceSelector` → grouped list: one section per selected context with its
  own loading/error state (a 403 on one cluster must not blank the picker — reuse
  the `NamespacesWarning`/`nsError` pattern per context). Per-cluster "All" state;
  the hidden native `<select>` (Playwright contract) emits composite tokens.

**Queries:**

- New shared helper `useAksScopedQueries(scope, fetcher)` in `useAks.ts`: `useQueries`
  fan-out across selected contexts × per-context ns token, merging results and
  stamping each row's `.context` field (optional, client-side-only field added to
  the frontend `PodInfo`/`DeploymentInfo`/etc. types — backend models untouched).
- All resource hooks gain the optional `context` param (pods, services, helm,
  secrets, events, statefulsets, hpas, scaledjobs, cronjobs, jobs, configmaps +
  values, ingresses, gateways, httproutes, envoy, pod-metrics, containers,
  helm history/values/notes/manifest, yaml get, log stream) — matching `?context=`
  added to each sidecar endpoint.
- `GatewayClasses` is cluster-scoped → fans out per context too.
- `resolvePodsForSelector` and `refetchPods` take context.

**Sidecar:**

- Thread `string? context` through the remaining ~45 `AksEndpoints` handlers
  (reads + log SSE stream). Pattern already established: bind optional query
  param → `GetClient(pool, string.IsNullOrWhiteSpace(context) ? null : context)`.

**Tables & detail surfaces:**

- `ResourceTable` gets an auto-shown **Context column** (renders only when more
  than one context is selected — keeps single-context UI pixel-identical).
- Every detail/overlay surface carries the row's context: PodDetailPanel,
  PodLogView, MultiPodLogView (mixed-context pod set is allowed — each stream
  resolves its own context), YamlViewer (read), Helm/Secret/ConfigMap/HttpRoute/
  Container detail panels, AnalysisPanel, EventsTab, PortForwardPanel rows.

**Agent:**

- Screen state publishes `contexts: string[]` alongside today's `context` field
  (the configured default — unchanged as the agent's fallback target);
  `AksToolContext` needs no change (tools already take `context`).

**Demo mode:**

- `DemoModeService.GetAksClient(context)` currently returns one demo client for any
  context — return distinct demo clients per context name (dev/staging/prod get
  distinguishable namespace sets and workloads so the merged view is legible).

### Phase 2 — cross-cluster actions

- `?context=` on every mutation endpoint: deployment/statefulset restart+scale,
  pod delete, ingress/httproute delete, hpa/scaledjob scale+toggle+delete, cronjob
  suspend/trigger/schedule, helm rollback, yaml validate+apply.
- Mutation hooks take `vars.context` → append query param; invalidate keys stay
  prefix-based (already correct).
- `ConfirmBar` messages name the target cluster whenever it differs from the
  configured default: "Delete pod `api-xyz` in **`prod-eu`**?"
- Pod shell + port-forward pass the row's context (native layer already accepts
  it); port-forward session list shows the context.
- YAML apply validates+applies to the row's own cluster.
- Auto-refresh floor: when >1 context selected, minimum interval is 30s — N×M
  polling every 5–10s is real cost on big clusters.

## Non-goals

- Cross-cluster diff/compare view (belongs in `agent-colleague` cross-env compare).
- Multi-cluster "apply this YAML everywhere" — apply stays single-target.
- Server-side aggregate endpoints — fan-out stays client-side via `useQueries`
  (per-context errors stay isolated; per-context warm caches reused).
- Monitoring rules changes — already per-context.
- Websocket/watch-based merged event streams — polling model unchanged.

## Implementation tasks

- [x] `aks-workspace-context.ts`: composite key codecs (`ctx:ns`, `ctx:ns/name`),
      `ctxs` param parse/encode, back-compat bare-ns→default
- [x] `useAks.ts`: `context` param on all resource hooks; `useAksScopedQueries`
      fan-out helper; `.context` stamping; mutation vars.context (phase 2)
- [x] `AksEndpoints.cs`: `?context=` on all remaining handlers incl. log SSE
- [x] `ContextSelector` plain multi-select (no primary badge)
- [x] `NamespaceSelector` per-context groups, composite tokens, per-context errors
- [x] `AksWorkspaceContext`: selection state, per-context ns init/restore,
      namespaceToken map, pass ctx through pod/yaml/helm/container/logs URL params
- [x] `ResourceTable` Context column (multi-context only)
- [x] All tabs + detail/log/yaml/analysis/port-forward surfaces carry row context
- [x] Screen state: `contexts` alongside `context`
- [x] Demo: per-context demo clients
- [x] Phase 2: mutation endpoints + hooks + cluster-named ConfirmBar; shell/PF ctx
- [x] Auto-refresh 30s floor for multi-context
- [x] Tests + e2e

## Test plan

- `aks-workspace-context.test.ts` (new or extended): composite ns codec round-trip,
  bare-ns→default back-compat, `ctxs` param, `*` per-context tokens.
- `useAksScopedQueries` unit test: fan-out merge, `.context` stamping, per-context
  error isolation (one failing ctx leaves others' rows).
- `AksEndpointsTests`: `?context=` resolves the right pooled client (fake pool
  keyed by context already exists — `GetAksClient(string?)` overload is stubbed
  per-test; extend fake to record requested contexts).
- `DemoAksClient` per-context differentiation test.
- e2e `aks-multi-context.spec.ts` (new): select `aks-ecommerce-staging` alongside
  the configured dev context → grouped ns picker → merged pods show Context column →
  open pod detail on secondary row → deep link `?ctxs=&ns=` restores both →
  phase 2: delete on secondary row names that cluster in ConfirmBar.
- Regression: `aks.spec.ts`, `aks-url-state.spec.ts` stay green unchanged;
  `aks-context-switch.spec.ts` was rewritten for the multi-select model (picker,
  per-context namespace memory, last-selected guard, first-run state) — the old
  primary-switch assertions are obsolete by design.

## Validation results

- `npx tsc --noEmit` / `eslint`: clean (pre-existing warnings only).
- `vitest`: 791 passed — incl. `aks-workspace-context.test.ts` (22 codec tests:
  composite round-trip, bare-ns→default back-compat, `ctxs`, `*` tokens).
- `dotnet test`: Sidecar 840, Kubernetes 167, Core 942 — incl. explicit-context
  routing tests on `GetAksClient(context)` (`RequestedContexts` assertions).
- e2e `aks-multi-context.spec.ts`: 9/9 — select/deselect (incl. the configured context), grouped ns picker,
  Context column, deep-link restore, per-context error banner, cluster-named
  ConfirmBar on secondary rows.
- e2e regressions green: `aks.spec.ts` 12/12, `aks-context-switch.spec.ts` 7/7,
  `aks-url-state.spec.ts` 3/3, `aks-deferred.spec.ts` 6/6,
  `aks-portforward-analysis.spec.ts` 5/5, `aks-ux.spec.ts` 10/10.
  Note: runs must use isolated ports + appdata (`PLAYWRIGHT_VITE_PORT`,
  `PLAYWRIGHT_SIDECAR_PORT`, `PLAYWRIGHT_APPDATA_ROOT`) when another suite is
  in flight — a concurrent `globalSetup` kills the shared-port sidecar and
  wipes `.e2e-appdata` mid-run.
- Aikido scan: `e2e/test-config.ts` command-injection findings fixed
  (argv-style `spawnSync`, digits-only port guard); `HelmDetailPanel`
  `dangerouslySetInnerHTML` findings are false positives — `highlightYaml`
  HTML-escapes all content before wrapping in spans. One loopback health-poll
  SSRF pattern remains in the dev-only test harness (hardcoded 127.0.0.1,
  digits-only port) — accepted.

## Decisions

- **No primary context.** The selection is one peer set held in URL/view-pref
  state; the configured context is only the seed + legacy-token fallback + agent
  default, never a privileged row in the UI. Deselecting the configured context is
  a normal operation. The old click-to-switch-primary flow (POST
  `/api/aks/context`, connection test, "Switching to…" state) was removed from the
  picker — which cluster the profile points at is now a Settings concern, since
  view membership no longer implies it.
- **Client-side fan-out over aggregate endpoints.** N `useQueries` with per-context
  keys gives free per-cluster caching, partial-failure isolation, and identical
  shapes — a `/api/aks/multi` endpoint would duplicate every handler's
  serialization for zero gain.
- **Row `.context` stamped client-side**, not added to shared `AksModels` — those
  models feed monitoring/demo/agent paths that don't need the field; the type
  addition lives in `web/src/lib/types.ts` as optional.
- **Lazy failure for any context.** No upfront connection test on selection —
  the namespace query's existing error surface shows per-cluster 403/timeout
  inline; selecting a dead cluster never blocks the view.
- **Auto-refresh floor 30s in multi-context.** Traffic scales with
  contexts × namespaces; a floor beats discovering the cost on a 200-ns cluster.
