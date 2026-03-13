# Redis

## What Is Supported

- Configure multiple Redis cache entries per environment.
- Select active cache and database index.
- Connection test for configured cache.
- Key scan with pattern, cursor paging, and load-more behavior.
- Namespace tree grouping for discovered keys.
- Key detail inspection by type (string, hash, list, set, zset).
- TTL read/set/remove operations.
- String/hash value updates.
- Key deletion and full database purge.
- Server info and prefix memory analysis workflows.

## Core Runtime Flow

1. `RedisPage` reads active environment Redis config.
2. In demo mode it creates `DemoRedisClient`; otherwise `SwebKit.Redis.RedisClient`.
3. Scan flow uses cursor-based `SCAN` and progressively enriches key metadata.
4. Detail pane actions dispatch typed operations through `IRedisClient`.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`
- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor`
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Core/Services/RedisKeyGrouper.cs`

## Important Notes

- Runtime client uses `StackExchange.Redis` and issues raw `SCAN`/`MEMORY USAGE`/`OBJECT ENCODING` commands as needed.
- Database index is clamped to 0..15 in client setup and config form.
- Potentially destructive actions (delete/purge) are surfaced with confirmation UX in production contexts.

## Validation Pointers

- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyGrouperTests.cs`
- `tests/SwebKit.Core.Tests/RedisConfigMigrationTests.cs`
- `tests/SwebKit.Core.Tests/RedisValueHelpersTests.cs`
