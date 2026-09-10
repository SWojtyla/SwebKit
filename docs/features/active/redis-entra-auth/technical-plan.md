# Redis Settings Clarity & Entra ID Auth Mode — Technical Plan

## 1. Domain model (`src/SwebKit.Core/Domain/RedisConfig.cs`)

`RedisCacheEntry` gains `UseAad` (bool) and `CacheName` (string), mirroring `StorageConfig`'s
`UseAad`/`AccountName` shape. `RedisConfig.Validate()` branches per entry: `UseAad` requires
non-empty `CacheName`; otherwise requires non-empty `ConnectionString`, unchanged from before.

## 2. Connection building (`src/SwebKit.Redis/RedisClient.cs`)

New `BuildAadConnectionOptionsAsync(RedisCacheEntry)` alongside the existing
`BuildConnectionOptions(string)`:

- Throws before any network attempt if `CacheName` is null/empty/whitespace, same guard style as
  the existing connection-string checks.
- Builds `ConfigurationOptions { EndPoints = { "<CacheName>.redis.cache.windows.net:6380" } }`,
  then `await options.ConfigureForAzureWithTokenCredentialAsync(AzureCredentialFactory.CreateDefault())`
  — `AzureCredentialFactory` is the existing single source of truth for Entra credentials
  (`src/SwebKit.Core/Services/AzureCredentialFactory.cs`), already used by
  `AzureStorageClient`/`ServiceBusClientConnectionFactory`; reused here rather than constructing
  `DefaultAzureCredential` inline (see `docs/pitfalls/azure-sdk.md` AZ-4).
- Sets `AbortOnConnectFail = false` and `AllowAdmin = true` to match the connection-string path.

`CreateAsync` branches on `cacheEntry.UseAad` to pick this vs. the existing path. No changes
needed in `src-sidecar/Endpoints/RedisEndpoints.cs` — every endpoint already goes through
`IRedisClientFactory.CreateAsync(cache, ct)`.

## 3. Packages

Added `Microsoft.Azure.StackExchangeRedis` (`3.3.1`, current stable on NuGet) to
`Directory.Packages.props` next to `StackExchange.Redis`, referenced from
`src/SwebKit.Redis/SwebKit.Redis.csproj`.

## 4. Frontend (`web/src/lib/types.ts`, `web/src/components/settings/RedisSettings.tsx`)

`RedisCacheEntry` gains `useAad: boolean` and `cacheName: string`. `RedisSettings.tsx` adds a
per-cache radio pair ("Connection String" / "Entra ID (AAD)") identical in structure to
`StorageSettings.tsx`'s auth toggle (`redis-auth-connstring-{id}` / `redis-auth-entra-{id}` test
ids), switching between the connection-string input and a new `cacheName` input. One-line
`text-xs text-muted-foreground` helper text (the convention already used in
`DiagnosticsSettings.tsx`) was added under Namespace Separator, the connection-string/cache-name
field, and the Database/Active row.

## 5. Docs

`docs/architecture/functionalities/redis.md` gained a Credential Modes table (same shape as
`storage.md`'s) and updated code-location/test-pointer lists.
