# Data-Fetch Performance — Technical Plan

## Module 1 — Shared root causes

**1.1 / 1.2 Connection pools.** New `SidecarRedisConnectionPool` and
`SidecarServiceBusConnectionPool` in `src-sidecar/Services/`, both thin wrappers over the
existing generic `ClientCache<TClient>`, matching `SidecarStorageConnectionPool`. Redis keys on
`RedisCacheEntry.Id` via `GetOrAddAsync` (the connect is async); Service Bus keys on
`ServiceBusNamespace.Id.ToString()` via the sync `GetOrAdd`. Demo clients are tagged
`ConnectionOwnership.Borrowed` so the cache never disposes singletons `DemoModeService` owns.

New `IRedisConnectionPool` / `IServiceBusConnectionPool` abstractions sit beside
`IStorageConnectionPool` in `Core.Abstractions`. Every handler in `RedisEndpoints` and
`ServiceBusEndpoints` now takes the pool instead of the factory; the local `CreateClientAsync`
/ `CreateClient` helpers are gone.

Disposal works through `ClientCache`'s runtime type checks: `IRedisClient : IDisposable` and
`RedisClient.Dispose()` disposes the multiplexer; `IServiceBusClient` declares no disposal
contract but `AzureServiceBusClient` implements `IAsyncDisposable`.

**1.3 Invalidation.** `ConfigEndpoints.SaveProfileAsync` already took `IStorageConnectionPool`
to drop clients built from now-stale credentials; it takes all three and invalidates all three.

**1.4 Cancellation.** `apiSend` gained a `signal` parameter (`apiFetch` already accepted
`RequestInit`), and the query functions across `useRedis.ts`, `useServiceBus.ts` and
`useAks.ts` now pass `({ signal }) => apiFetch(url, { signal })`. The server half already
worked — ASP.NET binds `CancellationToken` to `HttpContext.RequestAborted` — it was simply
never triggered.

**1.5 Query defaults.** `main.tsx` sets `refetchOnWindowFocus: false`; the Service Bus
structural queries get a 5-minute `staleTime` and `sb-entity-stats` gets 10 seconds.

**1.6 Entity-path encoding.** A module-private `entitySegment` in `useServiceBus.ts` is applied
at all twelve call sites.

## Module 2 — Redis

**2.1** `RedisPageContext` gates `useRedisKeyspaceHealth` and `useRedisPrefixMemory` on
`activeTab === "keyspace"` / `"prefix"`; both hooks take an `enabled` option.

**2.2 Pipelined key info.** `RedisClient.GetKeyInfoAsync` issues one `IBatch` instead of six
sequential awaits, and drops the redundant `KeyExistsAsync` — `TYPE` already returns `none`.
`OBJECT FREQ` requires an LFU `maxmemory-policy` and `OBJECT IDLETIME` requires anything but,
so exactly one always fails; the verdict is now remembered per connection in two `volatile
bool` fields and the unsupported command is not sent again. That removes ~500 exceptions and
500 log writes per sweep. The four `TryGet*` helpers and the two `Try*Async` wrappers they used
are deleted — all dead once the batch path landed.

**2.3 Bulk key info.** New `POST /api/redis/{cacheId}/keys/info` reusing the existing
`LoadKeyInfosAsync` (which already caps at 500 and fans out over `Task.WhenAll`).
`useRedisKeyInfoBatch` becomes one `useQuery` keyed on the visible window and seeds the
per-key `["redis", cacheId, "keys", key, "info"]` entries so the detail panel still gets a free
hit. It now returns a `Map` rather than a results array.

**2.4 Scan loop.** New `RedisScanLoop` in `SwebKit.Redis` — deliberately separate from
`RedisClient` so its three termination conditions (full page / exhausted keyspace / elapsed
budget) are testable without a live server. It takes a `Func<TimeSpan>` for elapsed time so a
test can exhaust the budget deterministically. `ScanKeysAsync` supplies a real `Stopwatch` and a
2s budget. `KeyScanResult` gains `PagesScanned` — round trips, not keys examined, because
`SCAN ... MATCH` filters server-side and never reports how many slots it walked.
`useRedisScanKeys` gains `placeholderData: keepPreviousData`.

**2.5 "Load all".** Fixed in place rather than by moving to `useInfiniteQuery`. The effect now
waits for `isFetching` to settle and advances at most once per distinct cursor — both
conditions are load-bearing, since `keepPreviousData` means `data` is briefly the *previous*
page. `lastAdvancedCursorRef` resets in `applySearchPattern` and `handleCacheChange`.

## Module 3 — AKS

**3.1** `namespaceToken` no longer requires the namespace list to be loaded: an explicit
selection resolves immediately, and the list is consulted only to recognise "every namespace"
as `*` once it arrives.

**3.2** `TestConnectionAsync` uses `ListNamespaceAsync(limit: 1)` — still the same operation, so
a pass still proves the caller can list namespaces, without pulling the whole list a second time
per mount. `NamespaceCacheTtl` goes 30s → 5 min.

**3.3 Over-fetching.** `GetSecretsAsync` adds `labelSelector: "owner!=helm"` — `MapSecrets`
discarded those anyway, and they are the large ones. The `/secrets` endpoint calls it directly
instead of `GetSecretsAndHelmReleasesAsync`, whose Helm half it was throwing away. A new
`GetSecretsAsync(IReadOnlyList<string>)` fan-out overload sits beside the others.

ConfigMaps: `ConfigMapInfo` gains `Keys` and `DataSizeChars`; the list endpoint projects to
those and omits `Data`. New `GetConfigMapValuesAsync` + `/configmaps/{name}/values` and
`useAksConfigMapValues` serve the detail panel, mirroring how Secrets already worked. The
interface supplies a default implementation over the list path so no client breaks.
`Data` stays on the model, still populated by `IAksClient` itself, so the legacy Blazor page is
untouched.

**3.4** `HelmDetailPanel` gates notes and manifest on their tabs; both hooks take `enabled`.

**3.5** `GetContainerDetailsAsync` resolves referenced ConfigMaps with `Task.WhenAll` — the
comment claimed "batch" while the loop awaited each read in turn.

**3.7** `PodsTab` indexes metrics into a `Map` once instead of a linear `find` per pod.

**Not done: 3.6 response decompression.** Left out deliberately — see `status.md`.

## Module 4 — Service Bus

**4.1** `ListQueuesAsync` and `ListSubscriptionsAsync` read counts via
`GetQueuesRuntimePropertiesAsync` / `GetSubscriptionsRuntimePropertiesAsync` (100 per page),
run concurrently with the entity list and joined by name. The stats read swallows failures and
returns an empty map, so a principal that can list entities but not read runtime properties
still gets a tree. The scoped-connection-string path (pitfall AZ-2) is untouched and still
reads its single entity's stats itself. `PopulateStatsAsync` is gone, as are the already-dead
`TryGetQueueStatsAsync` / `TryGetSubscriptionStatsAsync`.

**4.2** `SbEntityInfo.SubscriptionDeadLetterCount` is filled for topics by
`PopulateSubscriptionRollupsAsync` (DOP 12, inside the one request, on the one pooled client),
so `EntityTree` can gate its per-topic subscription query on `isExpanded` while the collapsed
badge still renders. Once expanded, the badge prefers the freshly-fetched subscriptions.

**4.3** `invalidateServiceBusQueries` takes `{ includeTopology }`, default false; only the
explicit Refresh action passes true.

**4.4** `findCachedEntityStats` seeds `sb-entity-stats` from the already-cached
queue/topic/subscription lists via `placeholderData`. Exported for testing because it depends
on cache-key layout, which is exactly the kind of thing that silently stops matching.
