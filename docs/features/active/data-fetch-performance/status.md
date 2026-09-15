---
status: Review
---

# Data-Fetch Performance — Status

- **Current phase:** Review — implemented and validated, awaiting sign-off.
- **Reported by:** Sebastien, from daily use of the Tauri build: AKS, Service Bus and Redis all
  feel slow to fetch, and *"when filtering on Redis it sometimes takes a lot of time and I don't
  know if it crashed or just takes ages."*
- **Scope agreed up front:** root causes plus over-fetching; structural rewrites deferred. No
  timing instrumentation (explicitly declined). Redis scan changed to loop server-side under a
  budget (explicitly chosen).
- **Implementation PR:** not raised yet.

## Validation

| Gate | Result |
| --- | --- |
| `dotnet build src-sidecar` | clean |
| `dotnet test tests/SwebKit.Core.Tests` | 959 passed |
| `dotnet test tests/SwebKit.Sidecar.Tests` | 352 passed |
| `dotnet test tests/SwebKit.Kubernetes.Tests` | 129 passed |
| `dotnet test tests/SwebKit.Agents.Tests` | 201 passed |
| `dotnet test tests/SwebKit.App.Tests` | 555 passed |
| `npx tsc -b` | clean |
| `npm run test:unit` | 415 passed (37 files) |
| `npx eslint` (changed files) | see below |
| `npx playwright test e2e/{redis,service-bus,aks}.spec.ts` | 61 passed |

**Lint:** 32 problems against a 30-problem baseline on the same files with the changes stashed —
same 4 errors, 2 additional warnings. Both are `react-hooks/immutability` on
`lastAdvancedCursorRef.current = …` in `RedisPageContext.tsx`, the same class as the
`hasSeededExpansionRef` mutations already in that file. A ref is the correct tool here (state
would re-render); left matching the surrounding code rather than diverging from it.

## Definition of Done

- [x] Redis and Service Bus pool their clients; neither leaks a connection per request.
- [x] Every pool invalidates on profile save.
- [x] Superseded requests are cancellable end to end.
- [x] Redis metadata sweeps gated on their tabs; key-info batched into one request.
- [x] Redis scan loops server-side under a budget; the tree no longer blanks between pages.
- [x] AKS stops shipping Helm release manifests and ConfigMap values it does not render.
- [x] AKS first paint no longer waits on the cluster namespace list.
- [x] Service Bus reads counts in bulk; the tree no longer fetches every topic's subscriptions.
- [x] Subscription entity paths encoded (correctness bug found while tracing).
- [x] "Load all" advances past one page (correctness bug found while tracing).
- [x] Unit tests for both pools, the scan loop, the invalidation split and the stats lookup.
- [x] Feature docs and pitfall entries written (AZ-6, AZ-7, three TanStack Query entries).
- [ ] Manual verification against real Azure/AKS (see `test-plan.md`) — **owner: Sebastien**.
- [ ] Aikido security scan per `docs/security/aikido-mcp-scan.md` — the MCP server timed out on
      connect during this session, so it has not run.

## Deliberately not done

Each of these was considered and left out; none is blocked.

1. **AKS cluster-scoped list calls.** `KubernetesAksClient` overrides none of the
   `IReadOnlyList<string>` overloads, so `ns="*"` fans out one call per namespace at concurrency
   6 — about 10 sequential waves on a 60-namespace cluster. `ListPodForAllNamespacesAsync` and
   friends would collapse that to one call and would also fix two things for free: `GetEventsAsync`
   applies `limit` *per namespace* and then discards ~98% of what it fetched, and the Gateway API
   version probe can issue up to 18 404s before its cache warms. **This is the largest remaining
   win** and was cut only for size — it is a substantial change to a 1400-line file.
2. **`AutomaticDecompression` on the Kubernetes client** (`new k8s.Kubernetes(config)` passes no
   handler, so list responses arrive uncompressed). A one-line change that multiplies every other
   AKS win, but it needs verifying against KubernetesClient 19.0.2's handler defaults first, which
   is not something this pass could check without a live cluster.
3. **HTTP/2 on Kestrel.** The multi-pod log view opens one `EventSource` per pod against HTTP/1.1's
   6-connections-per-origin limit, so selecting 6+ pods starves every other AKS request.
4. **Virtualizing** the AKS resource tables and the Service Bus entity tree. Both render every row;
   the Redis browser and the SB message list are already virtualized.
5. **`useInfiniteQuery` for Redis pagination.** "Load all" was fixed in place instead. The refactor
   touches `cursor`, `allKeys`, `displayKeys` and the expansion-seeding logic, which carries
   documented subtle behaviour — not worth the regression risk in the same pass.
6. **Service Bus receiver caching** and a single `/tree` endpoint. Much less valuable now that the
   client is pooled: what remains is one AMQP link setup per peek, not a connection.
7. **Redis query keys embed 500-element arrays** (`["redis", cacheId, "health", keys, separator]`),
   so each distinct key set creates a cache entry retained for `gcTime` and re-hashed per
   subscription. Now that these only run on their own tabs it is far less frequent.

## Found but not fixed — not performance

- **`AksAccessDeniedScope` is a permanent no-op.** No sidecar endpoint ever opens a scope, so
  `Record` never fires and the "limited permissions" banner can never appear. Correctness bug,
  unrelated to this work, left alone rather than fixed silently inside a performance change.
- **Docs drift.** `docs/architecture/functionalities/redis.md` and `service-bus.md` still describe
  the retired MAUI/Blazor flow and behaviour the React path does not implement.
  `RedisScanPageAccumulator`, `IRedisWarmupCache` and `IServiceBusWarmupCache` are dead on the
  Tauri path (App-only). Those files should not be treated as a spec for the current code.
