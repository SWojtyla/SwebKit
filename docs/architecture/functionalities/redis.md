# Redis

## What Is Supported

- Configure multiple Redis cache entries per environment.
- Select active cache and database index.
- Connection test for configured cache.
- Pattern-based key scan with progressive pagination (`Load more` support).
- Unified key tree view: keys organized hierarchically by configurable separator (default `-`, persisted across sessions).
- Key detail inspection by type (string, hash, list, set, zset).
- TTL read/set/remove operations.
- **TTL visualisation**: human-readable label (e.g. "2h 22m remaining"), colour-coded expiry progress bar (green → amber → red), live client-side countdown (1 s tick), and 30-second server-side drift correction.
- String/hash value updates.
- Key deletion and selection-first bulk delete for the currently loaded key set.
- Prefix memory analysis workflow.
- **Keyspace Health Explorer**: read-only risk analysis for no-TTL keys, oversized values, heavy prefixes, and possible hot keys, including severity counts, filtering, and key drill-through.
- Scan coverage/confidence reporting (loaded keys vs estimated keyspace) to make partial analysis explicit.

## Core Runtime Flow

1. `RedisPage` reads active environment Redis config.
2. In demo mode it creates `DemoRedisClient`; otherwise `SwebKit.Redis.RedisClient`.
3. Page renders immediately with loading indicator; connection and scan run asynchronously (non-blocking navigation).
4. Scan loops through all cursor pages to load complete keyspace, then builds the namespace tree with `RedisKeyGrouper.BuildNamespaceTree`.
5. Tree nodes are either namespace prefixes (expandable) or key leaves (clickable to load details).
6. Detail pane actions dispatch typed operations through `IRedisClient`.
7. Bulk cleanup stays selection-driven: the toolbar can `Select all loaded`, namespace rows can select or clear loaded descendants, and delete still flows through explicit confirmation of the selected keys.
8. Health analysis (on-demand) loads metadata for currently loaded keys, computes findings via `RedisKeyspaceHealthAnalyzer`, and supports drill-through to key detail.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTreeNode.razor`
- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor` — key details + TTL visualisation
- `src/SwebKit.App/Components/Redis/RedisPrefixMemory.razor`
- `src/SwebKit.App/Components/Redis/RedisKeyspaceHealthExplorer.razor`
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Core/Services/TtlFormatter.cs` — human-readable TTL formatting and bar math
- `src/SwebKit.Core/Services/RedisKeyspaceHealthAnalyzer.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Core/Services/RedisKeyGrouper.cs`
- `src/SwebKit.Core/Models/RedisModels.cs` (namespace tree + health report contracts)

## Important Notes

- Runtime client uses `StackExchange.Redis` and issues raw `SCAN`/`MEMORY USAGE`/`OBJECT ENCODING` commands as needed.
- Health metadata retrieval also uses best-effort `OBJECT FREQ` and `OBJECT IDLETIME`; unsupported commands degrade gracefully.
- Database index is clamped to 0..15 in client setup and config form.
- Potentially destructive actions remain confirmation-gated; the main Redis page no longer exposes a direct full-database purge CTA and instead keeps destructive scope visible through the selected-key count.
- Namespace separator is persisted in `RedisConfig.NamespaceSeparator` and saved via `AppStateService.SaveProfilesAsync()`.
- Page navigation (Redis and AKS) uses non-blocking async loading to avoid UI freeze.
- Health findings are invalidated on scans and key mutations to prevent stale risk output.
- `Select all loaded` and subtree helpers operate on the keys currently loaded into the page tree only; no wildcard or hidden prefix delete pass is introduced behind the UI.

## Validation Pointers

- `tests/SwebKit.Core.Tests/TtlFormatterTests.cs` (22 tests)
- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyGrouperTests.cs`
- `tests/SwebKit.Core.Tests/RedisConfigMigrationTests.cs`
- `tests/SwebKit.Core.Tests/RedisValueHelpersTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyspaceHealthAnalyzerTests.cs`
- `tests/SwebKit.App.Tests/RedisKeyspaceHealthExplorerTests.cs`
