# Backend Plan - Redis Manager

---

title: "Backend Plan - Redis Manager"
owner: ""
status: "Not started"

---

## Goal

Provide a Redis client abstraction with connection management, key browsing, inspection, mutation, and server info — following the same patterns as the AKS feature (`IAksClient` / `KubernetesAksClient` / `DemoAksClient`).

## Impacted areas

- `src/SwebKit.Redis/` (new project)
- `src/SwebKit.Core/Abstractions/` — new `IRedisClient` interface
- `src/SwebKit.Core/Domain/` — new `RedisConfig` model
- `src/SwebKit.Core/Models/` — new Redis DTOs
- `src/SwebKit.Core/Services/` — new `DemoRedisClient`
- `tests/SwebKit.Redis.Tests/` (new project)
- `tests/SwebKit.Core.Tests/` — DemoRedisClient tests

## Design

### Architecture

```
ProjectEnvironment
  └── RedisConfig (connection string, alias, database)
        │
        ▼
  IRedisClient (interface in SwebKit.Core)
        │
   ┌────┴────┐
   │         │
RedisClient  DemoRedisClient
(SwebKit.Redis)  (SwebKit.Core)
   │
   ▼
StackExchange.Redis ConnectionMultiplexer
```

### Connection lifecycle

- `ConnectionMultiplexer` is long-lived and thread-safe — create one per connection string, cache and reuse.
- Expose a `TestConnectionAsync()` method for the settings form.
- Dispose multiplexer when switching connections or on app shutdown.

### Key browsing strategy

- Always use `SCAN` (never `KEYS`) with cursor-based pagination.
- Default page size: 100 keys per scan batch.
- Support glob pattern filter (e.g., `user:*`, `session:*`).
- Deduplicate results client-side (SCAN may return duplicates across cursor iterations).

## API / Contracts

### `RedisConfig` (Domain model)

```csharp
public class RedisConfig
{
    public string ConnectionString { get; set; } = "";
    public string? Alias { get; set; }          // Friendly name (e.g., "Dev Cache")
    public int Database { get; set; } = 0;       // DB0-DB15
}
```

Added to `ProjectEnvironment`:

```csharp
public RedisConfig? RedisConfig { get; set; }
```

### `IRedisClient` (Abstraction)

```csharp
public interface IRedisClient : IDisposable
{
    // Connection
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    // Key browsing
    Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default);

    // Key inspection
    Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default);
    Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<HashEntry>> GetHashFieldsAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<SortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default);

    // Key mutation
    Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default);
    Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default);

    // TTL management
    Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default);
    Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveTtlAsync(string key, CancellationToken ct = default);

    // Database operations
    Task FlushDatabaseAsync(CancellationToken ct = default);

    // Server info
    Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default);
}
```

### Models (in `SwebKit.Core/Models/`)

```csharp
public record KeyScanResult(long Cursor, IReadOnlyList<string> Keys, bool IsComplete);

public record RedisKeyInfo(
    string Key,
    string Type,         // string, hash, list, set, zset, stream, none
    TimeSpan? Ttl,       // null = no expiry, -1 = key doesn't exist
    long? MemoryBytes,   // from MEMORY USAGE
    string? Encoding     // from OBJECT ENCODING
);

public record HashEntry(string Field, string Value);

public record SortedSetEntry(string Member, double Score);

public record RedisServerInfo(
    string RedisVersion,
    long UptimeSeconds,
    long ConnectedClients,
    long UsedMemoryBytes,
    string UsedMemoryHuman,
    long TotalCommandsProcessed,
    double KeyspaceHitRatio,     // hits / (hits + misses)
    IReadOnlyList<DatabaseInfo> Databases
);

public record DatabaseInfo(int Index, long Keys, long Expires, long AvgTtl);
```

## Tasks

- [x] Create `SwebKit.Redis` project with `StackExchange.Redis` dependency
- [x] Add `RedisConfig` domain model
- [x] Add `RedisConfig` to `ProjectEnvironment`
- [x] Define `IRedisClient` interface in `SwebKit.Core`
- [x] Add Redis model DTOs
- [x] Implement `RedisClient` in `SwebKit.Redis` using `StackExchange.Redis`
- [x] Implement `DemoRedisClient` in `SwebKit.Core` with realistic demo data
- [x] Add unit tests for `DemoRedisClient`
- [ ] Add unit tests for any parsing/formatting helpers

## Migration and runtime changes

- New NuGet dependency: `StackExchange.Redis` (added to `SwebKit.Redis` project only)
- `ProjectEnvironment` gains optional `RedisConfig` — existing persisted state is unaffected (null by default)
- No database migrations required

## Validation

- Unit tests: Not started
- Integration tests: Deferred (requires live Redis instance)
- Manual checks: See `test-plan.md`

## Notes

- `ConnectionMultiplexer.ConnectAsync` can be slow on first connect — show loading state in UI.
- `MEMORY USAGE` may not be available on all Redis versions (requires 4.0+) — handle gracefully.
- `OBJECT ENCODING` may fail on some key types — return null on error.
- Value display should truncate large values (>10KB) with a "show full" toggle in the UI.
