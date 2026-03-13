# Redis

## What Is Supported

- Configure multiple Redis cache entries per environment.
- Select active cache and database index.
- Connection test for configured cache.
- Full key scan with pattern (all keys loaded at once, no pagination).
- Unified key tree view: keys organized hierarchically by configurable separator (default `-`, persisted across sessions).
- Key detail inspection by type (string, hash, list, set, zset).
- TTL read/set/remove operations.
- String/hash value updates.
- Key deletion and full database purge.
- Prefix memory analysis workflow.

## Core Runtime Flow

1. `RedisPage` reads active environment Redis config.
2. In demo mode it creates `DemoRedisClient`; otherwise `SwebKit.Redis.RedisClient`.
3. Page renders immediately with loading indicator; connection and scan run asynchronously (non-blocking navigation).
4. Scan loops through all cursor pages to load complete keyspace, then builds the namespace tree with `RedisKeyGrouper.BuildNamespaceTree`.
5. Tree nodes are either namespace prefixes (expandable) or key leaves (clickable to load details).
6. Detail pane actions dispatch typed operations through `IRedisClient`.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTreeNode.razor`
- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor`
- `src/SwebKit.App/Components/Redis/RedisPrefixMemory.razor`
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Core/Services/RedisKeyGrouper.cs`
- `src/SwebKit.Core/Models/RedisModels.cs` (NamespaceNode with IsKey/FullKey)

## Important Notes

- Runtime client uses `StackExchange.Redis` and issues raw `SCAN`/`MEMORY USAGE`/`OBJECT ENCODING` commands as needed.
- Database index is clamped to 0..15 in client setup and config form.
- Potentially destructive actions (delete/purge) are surfaced with confirmation UX in production contexts.
- Namespace separator is persisted in `RedisConfig.NamespaceSeparator` and saved via `AppStateService.SaveProfilesAsync()`.
- Page navigation (Redis and AKS) uses non-blocking async loading to avoid UI freeze.

## Validation Pointers

- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyGrouperTests.cs`
- `tests/SwebKit.Core.Tests/RedisConfigMigrationTests.cs`
- `tests/SwebKit.Core.Tests/RedisValueHelpersTests.cs`
