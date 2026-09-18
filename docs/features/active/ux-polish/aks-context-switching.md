# Module 1 — AKS Context Switching & Namespace Experience

Flagship module. Fixes the confirmed defects in the context-switch path and makes switching
clusters feel instant via per-cluster query scoping and immediate namespace restore.

## Confirmed defects (with evidence)

| # | Defect | Location |
|---|--------|----------|
| B1 | `handleContextChange` computes the next `ns` from the **old** cluster's namespace list (`namespaces?.includes(defaultNamespace) ? … : namespaces?.[0]`); ghost namespace on the new cluster, init effect can't recover (`nsParam` already set). | `web/src/components/aks/shared/AksWorkspaceContext.tsx` `handleContextChange` |
| B2 | `useAksNamespaces` keyed `["aks-namespaces"]` — old-cluster entries render while the new list fetches (~seconds–18s). | `web/src/lib/hooks/useAks.ts` |
| B3 | Mutation `onSuccess` toasts "AKS context switched" even for `{connected:false,error}`; thrown POST has no `onError` → silent failure. | `AksWorkspaceContext.tsx` |
| B4 | Resource query keys not context-scoped → cluster A rows visible labeled as cluster B mid-switch; back-switch never hits cache. | `web/src/lib/hooks/useAks.ts` |
| B5 | `SidecarMonitoringConnectionPool.GetAksClient(context)` caches under `context` but builds with `aksConfig.KubeconfigContext` — explicit-context callers (monitoring rules) get a client for the wrong cluster. | `src-sidecar/Services/SidecarMonitoringConnectionPool.cs` |
| B6 | Init effect waits for the full namespace list (~18s cold) before applying the persisted per-context selection — explicit `ns` alone is enough to fetch. | `AksWorkspaceContext.tsx` init effect |
| B7 | `POST /api/aks/context` calls `InvalidateStaleConnections()` → disposes all pooled clients incl. their 5-min namespace caches → A→B→A re-pays cold cost. | `src-sidecar/Endpoints/AksEndpoints.cs` |
| B8 | `/api/aks/contexts` reads the real kubeconfig even in demo mode; `DemoAksClient.GetContextsAsync` (5 demo contexts) bypassed → empty demo selector. | `AksEndpoints.cs` |
| B9 | `PUT /api/config/profiles` never invalidates the pool — kubeconfig-path edits leave stale clients until a context POST or restart. | `src-sidecar/Endpoints/ConfigEndpoints.cs` |
| B10 | `lastRefreshedAt` not reset on switch — "updated Ns ago" describes the previous cluster. | `AksWorkspaceContext.tsx` |

## Backend changes

### `SidecarMonitoringConnectionPool.cs`

- `GetAksClient(string? context)`: factory builds with `context ?? aksConfig.KubeconfigContext`
  (fixes B5). Cache key stays `context ?? aksConfig.KubeconfigContext ?? "default"`.
- No other pool changes — per-context clients now survive context switches correctly.

### `AksEndpoints.cs`

- `POST /api/aks/context`: remove the blanket `pool.InvalidateStaleConnections()` (B7).
  Clients are correctly keyed once B5 is fixed; eviction on config change moves to profile save.
- `GET /api/aks/contexts`: when `demo.IsDemoMode`, resolve via `GetClient(pool).GetContextsAsync()`
  so demo contexts are served (B8); keep kubeconfig read otherwise.
- `GET /api/aks/namespaces`: optional `?refresh=1` query flag → call a non-cached path
  (`KubernetesAksClient` cache bypass) when the front-end does an explicit full refresh.

### `ConfigEndpoints.cs` (`SaveProfileAsync`)

- Snapshot connection-relevant sections (`AksConfig`, `ServiceBusNamespaces`, `RedisConfig`)
  before save; compare after; `pool.InvalidateStaleConnections()` only when they changed (B9).
  Keeps warm caches through unrelated saves (favorites, topology).

## Frontend changes

### `web/src/lib/hooks/useAks.ts` — context-scoped keys (B4)

- Add internal `useAksContextKey()`: `useProfile().data?.config.aksConfig?.kubeconfigContext ?? "default"`.
- Every namespaced/cluster-scoped query key becomes `[key, ctx, …rest]`:
  `["aks-namespaces", ctx]`, `["aks-pods", ctx, ns, labelSelector]`, `["aks-deployments", ctx, ns]`, …
- `useAksContexts`, `useAksTestConnection` stay unscoped (kubeconfig-file read / profile-current probe).
- `useAksSetContext` `onSuccess`: `qc.setQueryData(["profile"], …)` writing `kubeconfigContext`
  immediately (keys re-key without waiting for profile refetch) + existing invalidations + `onError` notify.
- `aks-namespaces`/`aks-contexts` get ~5min `staleTime` (structural queries, pitfalls doc).
- Prefix invalidation (`["aks-pods"]`) and `isAksResourceQueryKey` head-checks keep working.

### `AksWorkspaceContext.tsx`

- `handleContextChange`: stop consulting `namespaces`; set `ns` = persisted
  `view-pref:aks-selected-ns:<newCtx>` → `defaultNamespace` arg → `null` (B1). Reset
  `lastRefreshedAt` (B10). `onSuccess(data)`: `data.connected` → success toast; else error
  toast with `data.error`, still refetch (B3). `onError` → error toast + profile invalidation.
- Init effect (B6): when no `ns` param and none persisted-applied yet, apply the per-context
  persisted selection **immediately** (don't wait for `namespaces`); when the list lands,
  validate selection against it and replace with `defaultNamespace ?? namespaces[0]` if empty.
- Namespace selector gets `isLoading || contextLoading` → "Loading namespaces…" display (B2
  display side; data side handled by ctx-scoped key).

### `ContextSelector.tsx`

- Row subtitle shows `ctx.cluster` + `ctx.namespace` (`cluster · ns: ecommerce`).
- Mark kubeconfig `isCurrent`; MRU ordering via `view-pref:aks-context-mru` (cap 5).
- `aria-haspopup`/`aria-expanded`, Escape closes, ArrowUp/Down + Enter keyboard nav.
- While `isLoading`, button label shows the target context ("Switching to <ctx>…") — needs a
  `pendingContext` prop from the workspace.

### `NamespaceSelector.tsx`

- Loading display label instead of stale names; `aria` + keyboard parity with context selector.

### `AksPage.tsx`

- Toolbar indicator covers the full switch: `contextLoading || (nsLoading && !namespaceToken-ready)`
  with stage text ("Switching context…" → "Loading namespaces…").
- First-run: profile loaded + no `aksConfig` + not demo → `EmptyState` + "Configure kubeconfig"
  CTA → `/settings` (AKS tab via `state`).
- Optional: subtle `currentContext` chip in the content header (cluster breadcrumb).

## Acceptance criteria

- Switch A→B: `ns` = B's persisted/default; selector shows loading then B's list; skeletons then B's data; never A's rows mid-switch.
- Switch back within ~5min: namespaces + resources render from cache, background refetch only.
- Unreachable cluster: error toast with reason, no fake success; POST throw: error toast.
- Demo: 5 demo contexts listed; switch works.
- `GetAksClient("other")` yields a client for "other" (unit test).
- Playwright specs extended for switch restore, stale-free switch, error paths, first-run state.
