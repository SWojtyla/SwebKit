# Redis

## What Is Supported

- Configure multiple caches with connection-string or Entra authentication.
- Import Redis connection lists in Settings and test each connection.
- Select/persist the active cache and database index.
- Scan the full keyspace with Redis `MATCH` semantics and progressive cursor-backed loading.
- Group loaded keys into a namespace tree using a configurable separator.
- Browse/edit string, hash, list, set, and sorted-set values.
- Page list/set members from the source instead of fabricating client offsets.
- Read/set/remove TTL; show a live human-readable countdown and expiry severity.
- Rename, copy/export, delete, and bulk-delete explicitly selected loaded keys.
- Inspect server info, slow log, Pub/Sub, prefix memory, operational insights, and keyspace-health findings.
- Drill from prefix/health/slow-log findings back to key detail.
- Run against demo data without a real Redis server.

## Credential Modes

| Mode | Configuration | Connection |
| --- | --- | --- |
| Connection string | credential-store reference / endpoint config | StackExchange.Redis parsed options |
| Entra | Azure cache name | TLS endpoint with `DefaultAzureCredential` via Microsoft Azure StackExchange.Redis extensions |

The Entra path targets classic Azure Cache for Redis endpoints. Database index is clamped to `0..15`.

## Frontend Architecture

`RedisPageContext` is split into six churn-scoped contexts:

- connection/cache selection;
- URL-backed navigation;
- query/mutation facades;
- key browser/search/tree/selection;
- high-churn value editor state; and
- refresh/confirmation operations.

This keeps an editor keystroke from rerendering the virtualized key tree and confines auto-refresh ticks to operation consumers. Query and mutation objects use `web/src/lib/queryFacade.ts` where consumers need stable facade identity.

The page tabs are:

- Keys (`KeyBrowserPanel` + `KeyDetailPanel`)
- Server info
- Slow log
- Keyspace health
- Prefix memory
- Operations
- Pub/Sub

The active tab is URL-backed. Per-cache search patterns and active cache selection are persisted so a return visit restores the operator's working scope.

## Scan and Selection Semantics

```text
RedisPageProvider
  → useRedisScan(cache, pattern, cursor)
  → sidecar /api/redis/{cacheId}/scan
  → IRedisConnectionPool
  → RedisClient SCAN MATCH
  → cursor + loaded matching keys
  → namespace tree over loaded keys
```

Redis may return more items than requested for one SCAN count. The UI keeps a bounded loaded page, carries overflow forward, and advances from the opaque server cursor. `Select all loaded` and namespace selection operate only on keys currently represented in the tree; no hidden wildcard delete occurs.

A guarded load-all loop advances once per distinct cursor and stops on cursor zero. Filter/cache changes reset cursor, loaded keys, selection, and expansion state.

## Key Details and Analysis

- Type-specific hooks load values and perform mutations.
- Set paging uses `SSCAN`; cursor zero alone means complete.
- Secret/large values remain explicit user-driven reads and copy/export actions.
- Keyspace health loads best-effort metadata (`MEMORY USAGE`, encoding, frequency/idle time where supported) and reports scan coverage so partial analysis is visible.
- Mutations/scans invalidate stale health findings.
- Redis cannot store empty hash/list/set/zset values; import skips these with warnings rather than creating placeholders.

## Main Code Locations

- `web/src/components/redis/RedisPage.tsx`
- `web/src/components/redis/RedisPageContext.tsx`
- `web/src/components/redis/tabs/KeyBrowserPanel.tsx`
- `web/src/components/redis/tabs/KeyDetailPanel.tsx`
- `web/src/components/redis/tabs/KeyspaceTab.tsx`
- `web/src/components/redis/tabs/PrefixTab.tsx`
- `web/src/components/redis/tabs/OpsTab.tsx`
- `web/src/components/redis/PubSubPanel.tsx`
- `web/src/components/settings/RedisSettings.tsx`
- `web/src/lib/hooks/useRedis.ts`
- `src-sidecar/Endpoints/RedisEndpoints.cs`
- `src-sidecar/Services/SidecarRedisConnectionPool.cs`
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Core/Services/RedisConnectionImportParser.cs`
- `src/SwebKit.Core/Services/RedisKeyspaceHealthAnalyzer.cs`
- `src/SwebKit.Core/Services/RedisScanPageAccumulator.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Redis/RedisScanResponseParser.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`

## Validation Pointers

- `web/e2e/redis.spec.ts`
- `web/e2e/redis-deferred.spec.ts`
- `web/src/components/redis/RedisPageContext.test.ts`
- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisImportParserTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyGrouperTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyspaceHealthAnalyzerTests.cs`
- `tests/SwebKit.Core.Tests/RedisScanResponseParserTests.cs`
- `tests/SwebKit.Sidecar.Tests/RedisEndpointsMutationTests.cs`
- `tests/SwebKit.Sidecar.Tests/SidecarRedisConnectionPoolTests.cs`
