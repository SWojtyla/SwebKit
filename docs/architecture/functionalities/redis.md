# Redis

## What Is Supported

- Configure multiple Redis cache entries per environment.
- Select active cache and database index.
- Connection test for configured cache.
- Pattern-based key scan with server-side full-keyspace `MATCH` semantics and progressive loaded-match pagination (`Load more matches` support).
- Unified key tree view: keys organized hierarchically by configurable separator (default `-`, persisted across sessions).
- Key detail inspection by type (string, hash, list, set, zset).
- Incremental set-member paging in key detail, using source-backed `SSCAN` continuation cursors.
- TTL read/set/remove operations.
- **TTL visualisation**: human-readable label (e.g. "2h 22m remaining"), colour-coded expiry progress bar (green → amber → red), live client-side countdown (1 s tick), and 30-second server-side drift correction.
- String/hash value updates.
- Key deletion and selection-first bulk delete for the currently loaded key set.
- Stronger selected-row treatment in the key tree so active rows remain clearly visible during both single-select detail inspection and multi-select bulk workflows, including namespace rows that represent a partially or fully selected loaded subtree.
- Prefix memory analysis workflow.
- **Keyspace Health Explorer**: read-only risk analysis for no-TTL keys, oversized values, heavy prefixes, and possible hot keys, including severity counts, filtering, and key drill-through.
- Scan coverage/confidence reporting (loaded keys vs estimated keyspace) to make partial analysis explicit.

## Core Runtime Flow

1. `RedisPage` reads the persisted Redis config.
2. In demo mode it creates `DemoRedisClient`; otherwise `SwebKit.Redis.RedisClient`.
3. Page renders immediately with loading indicator; connection and scan run asynchronously (non-blocking navigation).
4. Scan walks Redis cursor pages with the requested `MATCH` pattern across the full keyspace, stops after a bounded loaded-match page for the tree, buffers any SCAN overflow beyond that cap, and resumes from the same filtered cursor when the user clicks `Load more matches`.
5. The page builds the namespace tree for the currently loaded matches with `RedisKeyGrouper.BuildNamespaceTree`.
6. Tree nodes are either namespace prefixes (expandable) or key leaves (clickable to load details when browse mode is active); key-type badges are filled with lightweight batched type lookups so the initial tree does not wait on full key metadata for every match, and new scan/filter/cache contexts supersede older badge batches before stale writes reach the tree.
7. In multi-select mode, clicking a key row toggles that loaded key, clicking a namespace row toggles its loaded descendant keys, and expand/collapse remains available through the dedicated chevron control.
8. Detail pane actions dispatch typed operations through `IRedisClient`.
9. Bulk cleanup stays selection-driven: the toolbar can `Select all loaded`, namespace row toggles stay scoped to loaded descendants only, and delete still flows through explicit confirmation of the selected keys.
10. Health analysis (on-demand) loads full metadata for currently loaded keys, computes findings via `RedisKeyspaceHealthAnalyzer`, and supports drill-through to key detail.

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
- `src/SwebKit.Core/Services/RedisScanPageAccumulator.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Redis/RedisScanResponseParser.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Core/Services/RedisKeyGrouper.cs`
- `src/SwebKit.Core/Models/RedisModels.cs` (namespace tree + health report contracts)

## Important Notes

- Runtime client uses `StackExchange.Redis` and issues raw `SCAN`/`MEMORY USAGE`/`OBJECT ENCODING` commands as needed.
- Health metadata retrieval also uses best-effort `OBJECT FREQ` and `OBJECT IDLETIME`; unsupported commands degrade gracefully.
- Database index is clamped to 0..15 in client setup and config form.
- Potentially destructive actions remain confirmation-gated; the main Redis page no longer exposes a direct full-database purge CTA and instead keeps destructive scope visible through the selected-key count.
- Namespace separator is persisted in `RedisConfig.NamespaceSeparator` and saved via `AppStateService.SaveConfigAsync()`.
- Page navigation (Redis and AKS) uses non-blocking async loading to avoid UI freeze; Redis intentionally keeps the currently loaded match page bounded so large keyspaces do not saturate the render path.
- If Redis returns more keys than requested for one `SCAN COUNT`, the page shows only one loaded-match page immediately and carries the overflow forward to the next `Load more matches` action.
- Set-member paging is source-backed: `RedisClient.GetSetMembersPageAsync()` issues raw `SSCAN`, `RedisScanResponseParser` preserves the returned cursor, and `SetScanResult.Cursor` must be treated as opaque source state instead of a fabricated offset.
- `SetScanResult.IsComplete` becomes `true` only when Redis returns cursor `0`.
- Health findings are invalidated on scans and key mutations to prevent stale risk output.
- `Select all loaded` and namespace row toggles operate on the keys currently loaded into the page tree only; no wildcard or hidden prefix delete pass is introduced behind the UI.
- The toolbar copy must keep the distinction explicit: filter patterns are keyspace-wide, while the tree, badges, and bulk helpers only cover the currently loaded matches.
- Manual rescans, filter changes, and cache changes cancel or supersede older badge-loading work so stale type badges do not populate a newer tree state.

## Validation Pointers

- `tests/SwebKit.Core.Tests/TtlFormatterTests.cs` (22 tests)
- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyGrouperTests.cs`
- `tests/SwebKit.Core.Tests/RedisConfigMigrationTests.cs`
- `tests/SwebKit.Core.Tests/RedisScanResponseParserTests.cs`
- `tests/SwebKit.Core.Tests/RedisValueHelpersTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyspaceHealthAnalyzerTests.cs`
- `tests/SwebKit.App.Tests/RedisKeyspaceHealthExplorerTests.cs`
