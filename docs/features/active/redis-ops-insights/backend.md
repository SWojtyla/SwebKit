# Backend Plan - redis-ops-insights

---

title: "Backend Plan - redis-ops-insights"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Add bounded, cancellation-aware Redis diagnostics contracts that expose TTL posture, slowlog summaries, hot-key evidence, and Pub/Sub channel snapshots without turning SwebKit into a continuous monitoring agent.

## Impacted areas

- Existing source and service paths:
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Core/Models/RedisModels.cs`
- `src/SwebKit.Core/Services/RedisKeyspaceHealthAnalyzer.cs`
- `src/SwebKit.Core/Services/RedisScanPageAccumulator.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- Likely new or expanded support files:
- `src/SwebKit.Core/Services/RedisTtlForecastAnalyzer.cs`
- `src/SwebKit.Core/Services/RedisOpsInsightsAggregator.cs`
- `tests/SwebKit.Core.Tests/RedisClientTests.cs`
- `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`
- `tests/SwebKit.Core.Tests/RedisKeyspaceHealthAnalyzerTests.cs`
- Likely new focused tests such as `RedisTtlForecastAnalyzerTests.cs` and `RedisOpsInsightsAggregatorTests.cs`.

## Design

The backend stays additive and layered on the existing Redis page contract.

### 1. TTL posture is computed from the loaded scan context

Wave 1 should not introduce a second hidden full-keyspace scan. Instead:

- Reuse the existing loaded key set from `RedisPage`.
- Fetch or reuse `RedisKeyInfo` metadata with bounded concurrency.
- Produce a `RedisTtlDistributionReport` that includes:
- Bucket counts such as `No TTL`, `< 5m`, `5m-30m`, `30m-6h`, `6h-24h`, and `> 24h`.
- Forecast counts for the next `5m`, `30m`, `6h`, and `24h` windows.
- Coverage fields mirroring the existing health report model (`LoadedKeyCount`, `EstimatedKeyCount`, `CoveragePercent`, `ConfidenceLabel`).

This keeps the feature honest about scope and avoids duplicate coverage semantics.

### 2. Slowlog and hot-key signals are additive and read-only

Wave 2 adds new read-only client methods rather than changing existing scan behavior:

- `GetSlowLogAsync(top, ct)` for the most recent bounded set of slowlog entries.
- Optional command-summary or server-info helpers if the UI needs grouped command stats.

Hot-key evidence should not rely on one signal alone. The aggregator should combine:

- Existing `OBJECT FREQ` and `OBJECT IDLETIME` data from `GetKeyInfoAsync`.
- Existing key memory and TTL metadata.
- Slowlog repetition by command, key, and prefix.

Each emitted finding should explain the signal source so the UI can distinguish "suspected hot key from LFU" from "repeated slow `HGETALL` on the same prefix".

### 3. Pub/Sub visibility is a snapshot, not a live tap

Wave 3 adds a bounded snapshot contract such as `RedisPubSubSnapshot`:

- Active channel list (bounded and optionally prefix-filtered).
- Subscriber counts per channel.
- Pattern subscription count (`NUMPAT`).
- Optional totals and truncation metadata.

The client should use Redis `PUBSUB CHANNELS`, `PUBSUB NUMSUB`, and `PUBSUB NUMPAT` without creating subscriptions or streaming payloads.

### 4. Capability-based degradation is part of the contract

Some Redis environments will not support or expose all server commands. The backend should return explicit capability or outcome fields such as:

- `Loaded`
- `Partial`
- `Unsupported`
- `PermissionLimited`
- `Failed`

That avoids treating unsupported slowlog or Pub/Sub introspection as exceptional page failures.

## API / Contracts

- Likely additions to `RedisModels.cs`:
- `RedisTtlDistributionReport`
- `RedisTtlBucket`
- `RedisExpiryForecastWindow`
- `RedisSlowLogEntryInfo`
- `RedisSlowLogSummary`
- `RedisHotKeySignal`
- `RedisPubSubSnapshot`
- `RedisPubSubChannelInfo`
- Likely additive methods on `IRedisClient`:
- `Task<IReadOnlyList<RedisSlowLogEntryInfo>> GetSlowLogAsync(int top = 128, CancellationToken ct = default)`
- `Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default)`
- Existing `GetKeyInfoAsync` remains the primary per-key metadata seam; only add batch helpers if later profiling proves the existing path too chatty.
- Backward compatibility notes:
- Existing scan, detail, TTL mutation, and delete flows remain unchanged.
- New diagnostics contracts are optional and page-local to Redis.

## Tasks

### Wave 1 - TTL posture and forecast [dotnet-expert]

- [ ] Define TTL distribution and forecast DTOs in `src/SwebKit.Core/Models/RedisModels.cs`.
- [ ] Implement a bounded forecast analyzer in `src/SwebKit.Core/Services` that consumes loaded key metadata and emits coverage-aware results.
- [ ] Reuse or extend current health coverage fields so the frontend does not invent a second coverage model.
- [ ] Add focused unit tests for bucket math, no-TTL handling, and forecast windows.

### Wave 2 - Slowlog and hot-key evidence [dotnet-expert]

- [ ] Extend `IRedisClient`, `RedisClient`, and `DemoRedisClient` with slowlog access and clear unsupported-command handling.
- [ ] Add a hot-key aggregation service that merges `OBJECT FREQ`, idle time, memory, and slowlog repetition into one explanation-friendly result.
- [ ] Ensure all exceptions preserve `OperationCanceledException` passthrough and downgrade unsupported server commands to a visible capability state.
- [ ] Add tests for supported, unsupported, and permission-limited paths.

### Wave 3 - Pub/Sub snapshot [dotnet-expert]

- [ ] Add Pub/Sub snapshot DTOs and bounded client methods.
- [ ] Add prefix filtering and truncation metadata so the UI can keep the list readable.
- [ ] Add demo fixtures and deterministic tests for idle and active channel states.
- [ ] Update Redis functionality documentation after implementation lands.

## Migration and runtime changes

- No persistent configuration migration is required for Wave 1 or Wave 2.
- Optional UI preferences such as the selected ops-insights tab or last forecast window can be stored later in `UiStateRepository`, but they are not required to ship the first implementation slice.
- Runtime behavior remains manual and operator-triggered; no background polling, subscriptions, or always-on refresh loops are introduced.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Verify slowlog and Pub/Sub capability degradation on environments where those commands are restricted.
- Verify cancellation on cache switch or rescan.
- Verify the analyzer does not imply full-keyspace certainty when only a partial scan is loaded.

## Notes

- Apply `docs/pitfalls/dotnet-csharp.md` guidance: do not swallow `OperationCanceledException` under broad Redis command handling.
- The backend should prefer additive helper services over enlarging `RedisPage` with direct data-shaping logic.
- Bounded diagnostics are a product constraint, not an optimization detail.
