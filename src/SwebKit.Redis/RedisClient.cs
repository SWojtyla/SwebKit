using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

namespace SwebKit.Redis;

public sealed class RedisClient : IRedisClient
{
    private readonly RedisCacheEntry _cacheEntry;
    private readonly ConnectionMultiplexer _mux;
    private readonly IDatabase _db;
    private readonly IServer _server;
    private readonly ILogger<RedisClient> _logger;

    // OBJECT FREQ needs an LFU maxmemory-policy; OBJECT IDLETIME needs anything but. Exactly one of
    // the two is always refused, so the first refusal is remembered here and that command is dropped
    // from every later key-info batch on this connection rather than throwing once per key.
    private volatile bool _objectFreqSupported = true;
    private volatile bool _objectIdleTimeSupported = true;

    private RedisClient(RedisCacheEntry cacheEntry, ConnectionMultiplexer mux, ILogger<RedisClient> logger)
    {
        _logger = logger;
        _cacheEntry = cacheEntry;
        _mux = mux;
        _db = _mux.GetDatabase(Math.Clamp(cacheEntry.Database, 0, 15));

        var endpoint = _mux.GetEndPoints().FirstOrDefault()
            ?? throw new InvalidOperationException("No Redis endpoints available.");
        _server = _mux.GetServer(endpoint);
    }

    /// <summary>
    /// Builds the multiplexer options used to connect to a cache.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="CreateAsync"/> so the admin-mode requirement is
    /// unit-testable without a live Redis server.
    /// </remarks>
    /// <param name="connectionString">The StackExchange.Redis connection string.</param>
    /// <returns>Options with admin mode enabled and connect-failure aborts disabled.</returns>
    public static ConfigurationOptions BuildConnectionOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        // INFO, FLUSHDB and SLOWLOG are admin commands; StackExchange.Redis refuses
        // them outright ("This operation is not available unless admin mode is
        // enabled: INFO") unless this is set. GetServerInfoAsync, FlushDatabaseAsync
        // and GetSlowLogAsync all depend on it, so the app is half-broken without it.
        options.AllowAdmin = true;

        return options;
    }

    /// <summary>
    /// Builds the multiplexer options for connecting to Azure Cache for Redis via Entra ID
    /// (AAD), authenticating with the app-wide <see cref="AzureCredentialFactory"/> credential
    /// instead of a password embedded in a connection string.
    /// </summary>
    /// <param name="cacheEntry">The cache entry; <see cref="RedisCacheEntry.CacheName"/> must be set.</param>
    public static async Task<ConfigurationOptions> BuildAadConnectionOptionsAsync(RedisCacheEntry cacheEntry)
    {
        if (string.IsNullOrWhiteSpace(cacheEntry.CacheName))
            throw new InvalidOperationException($"{nameof(RedisCacheEntry.CacheName)} is required when {nameof(RedisCacheEntry.UseAad)} is true.");

        var options = new ConfigurationOptions
        {
            EndPoints = { $"{cacheEntry.CacheName}.redis.cache.windows.net:6380" }
        };

        // See AzureCredentialFactory for why EnvironmentCredential is excluded.
        await options.ConfigureForAzureWithTokenCredentialAsync(AzureCredentialFactory.CreateDefault()).ConfigureAwait(false);

        options.AbortOnConnectFail = false;
        options.AllowAdmin = true;

        return options;
    }

    public static async Task<RedisClient> CreateAsync(RedisCacheEntry cacheEntry, ILogger<RedisClient>? logger = null)
    {
        logger ??= NullLogger<RedisClient>.Instance;
        var options = cacheEntry.UseAad
            ? await BuildAadConnectionOptionsAsync(cacheEntry).ConfigureAwait(false)
            : BuildConnectionOptions(cacheEntry.ConnectionString);
        var mux = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        return new RedisClient(cacheEntry, mux, logger);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.PingAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>How long a single scan request may keep pulling cursor pages before returning what it has.</summary>
    /// <remarks>
    /// Bounded by time rather than page count because the cost of a page depends on the keyspace, not on a
    /// number we can pick here. Short enough to stay responsive, long enough that a selective pattern
    /// usually returns real matches on the first request instead of an empty page.
    /// </remarks>
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Returns up to <paramref name="pageSize"/> matching keys, continuing across <c>SCAN</c> cursor pages
    /// until the page is full, the keyspace is exhausted, or <see cref="ScanBudget"/> elapses.
    /// </summary>
    /// <remarks>
    /// A single <c>SCAN</c> call walks roughly <c>COUNT</c> slots and filters by <c>MATCH</c> afterwards, so
    /// on a large keyspace with a selective pattern it almost always returns *zero* keys and a non-zero
    /// cursor. Surfacing that directly meant a blank tree and repeated manual "Load more" for what the user
    /// experiences as one search. Looping here turns those into one request, and the returned cursor still
    /// lets the caller continue from exactly where this left off.
    /// </remarks>
    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var count = Math.Max(1, pageSize);
        var startedAt = Stopwatch.StartNew();

        return RedisScanLoop.RunAsync(
            async (from, token) =>
            {
                token.ThrowIfCancellationRequested();
                var result = await _db.ExecuteAsync("SCAN", from, "MATCH", pattern, "COUNT", count).ConfigureAwait(false);
                return RedisScanResponseParser.Parse(result);
            },
            cursor,
            count,
            ScanBudget,
            () => startedAt.Elapsed,
            ct);
    }

    public async Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keyType = await _db.KeyTypeAsync(key).ConfigureAwait(false);
        return ToTypeString(keyType);
    }

    /// <summary>
    /// Reads every displayed attribute of a key in a single pipelined round trip.
    /// </summary>
    /// <remarks>
    /// This used to be six sequential awaits preceded by a redundant <c>EXISTS</c> — <c>TYPE</c> already
    /// answers "does this key exist" by returning <c>none</c>. Multiplied by the 500-key sweeps the
    /// keyspace-health and prefix-memory panels run, that was thousands of serialized commands.
    /// <para><c>OBJECT FREQ</c> and <c>OBJECT IDLETIME</c> are mutually exclusive: the former requires an
    /// LFU <c>maxmemory-policy</c>, the latter requires anything but. So on any given server exactly one
    /// always fails, and it used to fail once per key — 500 exceptions and 500 log lines per sweep. The
    /// verdict is now remembered per connection and the unsupported command is simply not sent again.</para>
    /// </remarks>
    public async Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var batch = _db.CreateBatch();
        var typeTask = batch.KeyTypeAsync(key);
        var ttlTask = batch.KeyTimeToLiveAsync(key);
        var memoryTask = batch.ExecuteAsync("MEMORY", "USAGE", key);
        var encodingTask = batch.ExecuteAsync("OBJECT", "ENCODING", key);
        var freqTask = _objectFreqSupported ? batch.ExecuteAsync("OBJECT", "FREQ", key) : null;
        var idleTask = _objectIdleTimeSupported ? batch.ExecuteAsync("OBJECT", "IDLETIME", key) : null;
        batch.Execute();

        var keyType = await AwaitOrDefaultAsync(typeTask, RedisType.None, nameof(GetKeyTypeAsync), key).ConfigureAwait(false);
        if (keyType == RedisType.None)
        {
            // Drain the rest so a batch command that faulted on a missing key never surfaces as an
            // unobserved task exception.
            await ObserveAsync(ttlTask, memoryTask, encodingTask, freqTask, idleTask).ConfigureAwait(false);
            return new RedisKeyInfo { Key = key, Type = "none" };
        }

        var ttl = await AwaitOrDefaultAsync(ttlTask, (TimeSpan?)null, nameof(GetTtlAsync), key).ConfigureAwait(false);
        var memoryBytes = ParseNullableLong(
            (await AwaitOrDefaultAsync(memoryTask, RedisResult.Create(RedisValue.Null), "MEMORY USAGE", key).ConfigureAwait(false)).ToString());
        var encodingRaw = (await AwaitOrDefaultAsync(encodingTask, RedisResult.Create(RedisValue.Null), "OBJECT ENCODING", key).ConfigureAwait(false)).ToString();

        return new RedisKeyInfo
        {
            Key = key,
            Type = ToTypeString(keyType),
            Ttl = ttl,
            MemoryBytes = memoryBytes,
            Encoding = string.IsNullOrWhiteSpace(encodingRaw) ? null : encodingRaw,
            Frequency = await ReadObjectCounterAsync(freqTask, "OBJECT FREQ", key, supported => _objectFreqSupported = supported).ConfigureAwait(false),
            IdleSeconds = await ReadObjectCounterAsync(idleTask, "OBJECT IDLETIME", key, supported => _objectIdleTimeSupported = supported).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Awaits one command of a batch, returning <paramref name="fallback"/> instead of throwing — a single
    /// unsupported command must not lose the other five results.
    /// </summary>
    private async Task<T> AwaitOrDefaultAsync<T>(Task<T> task, T fallback, string operationName, string key)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis {Operation} failed for key {Key}", operationName, key);
            return fallback;
        }
    }

    /// <summary>
    /// Reads an <c>OBJECT</c> counter, recording via <paramref name="setSupported"/> when the server refuses
    /// the command so it is never sent again on this connection.
    /// </summary>
    private async Task<long?> ReadObjectCounterAsync(Task<RedisResult>? task, string operationName, string key, Action<bool> setSupported)
    {
        if (task is null)
            return null;

        try
        {
            return ParseNullableLong((await task.ConfigureAwait(false)).ToString());
        }
        catch (Exception ex)
        {
            setSupported(false);
            _logger.LogDebug(ex, "Redis {Operation} is unsupported on this server; not requesting it again.", operationName);
            return null;
        }
    }

    /// <summary>Observes faulted batch tasks whose results are being discarded.</summary>
    private static async Task ObserveAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null) continue;
            try { await task.ConfigureAwait(false); } catch { /* result discarded; only observation matters */ }
        }
    }

    public async Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await _db.StringGetAsync(key).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public async Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fields = await _db.HashGetAllAsync(key).ConfigureAwait(false);
        return fields
            .Select(x => new RedisHashField
            {
                Field = x.Name.ToString(),
                Value = x.Value.ToString()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = await _db.ListRangeAsync(key, start, stop).ConfigureAwait(false);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = await _db.SetMembersAsync(key).ConfigureAwait(false);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = await _db.SortedSetRangeByRankWithScoresAsync(key, start, stop, Order.Descending).ConfigureAwait(false);
        return values
            .Select(v => new RedisSortedSetEntry
            {
                Member = v.Element.ToString(),
                Score = v.Score
            })
            .ToList();
    }

    public async Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.StringSetAsync(key, value, expiry, When.Always).ConfigureAwait(false);
    }

    public async Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.HashSetAsync(key, field, value).ConfigureAwait(false);
    }

    public async Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (keys.Count == 0)
            return;

        await _db.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray()).ConfigureAwait(false);
    }

    public async Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new RedisImportResult();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                result.SkippedCount++;
                result.Warnings.Add("Skipped Redis import entry with an empty key.");
                continue;
            }

            if (!overwriteExisting && await _db.KeyExistsAsync(entry.Key).ConfigureAwait(false))
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped existing Redis key '{entry.Key}'.");
                continue;
            }

            await _db.KeyDeleteAsync(entry.Key).ConfigureAwait(false);
            if (!await TryImportEntryAsync(entry, result.Warnings).ConfigureAwait(false))
            {
                result.SkippedCount++;
                continue;
            }

            result.ImportedCount++;
        }

        return result;
    }

    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
    }

    public async Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
    }

    public async Task RemoveTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyPersistAsync(key).ConfigureAwait(false);
    }

    public async Task FlushDatabaseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _server.FlushDatabaseAsync(Math.Clamp(_cacheEntry.Database, 0, 15)).ConfigureAwait(false);
    }

    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(BuildServerInfo(_server.Info(), _server.Info("keyspace")));
    }

    /// <summary>
    /// Projects the raw INFO / INFO keyspace sections onto <see cref="RedisServerInfo"/>.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="GetServerInfoAsync"/> so the field mapping and the
    /// keyspace hit-ratio arithmetic are unit-testable without a live Redis server.
    /// </remarks>
    internal static RedisServerInfo BuildServerInfo(
        IGrouping<string, KeyValuePair<string, string>>[] infoSections,
        IGrouping<string, KeyValuePair<string, string>>[] keyspaceSections)
    {
        var metrics = ParseInfo(infoSections);
        var keyspace = ParseDbStats(keyspaceSections);

        var hits = GetLong(metrics, "keyspace_hits");
        var misses = GetLong(metrics, "keyspace_misses");
        var ratio = hits + misses == 0 ? 0 : (double)hits / (hits + misses);

        return new RedisServerInfo
        {
            RedisVersion = GetString(metrics, "redis_version"),
            UptimeSeconds = GetLong(metrics, "uptime_in_seconds"),
            ConnectedClients = GetLong(metrics, "connected_clients"),
            UsedMemoryBytes = GetLong(metrics, "used_memory"),
            MaxMemoryBytes = GetLong(metrics, "maxmemory"),
            UsedMemoryHuman = GetString(metrics, "used_memory_human"),
            TotalCommandsProcessed = GetLong(metrics, "total_commands_processed"),
            KeyspaceHitRatio = ratio,
            Databases = keyspace
        };
    }

    public async Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.SortedSetAddAsync(key, member, score, SortedSetWhen.Exists).ConfigureAwait(false);
    }

    public async Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyRenameAsync(oldKey, newKey).ConfigureAwait(false);
    }

    public async Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.HashDeleteAsync(key, field).ConfigureAwait(false);
    }

    public async Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        pageSize = Math.Max(1, pageSize);
        var result = await _db.ExecuteAsync("SSCAN", key, cursor, "COUNT", pageSize).ConfigureAwait(false);
        var scanPage = RedisScanResponseParser.Parse(result);
        return new SetScanResult(scanPage.Values, scanPage.Cursor, scanPage.IsComplete);
    }

    public void Dispose()
    {
        _mux.Dispose();
    }

    private async Task<bool> TryImportEntryAsync(RedisImportEntry entry, ICollection<string> warnings)
    {
        switch (entry.Type.Trim().ToLowerInvariant())
        {
            case "string":
                await _db.StringSetAsync(entry.Key, entry.StringValue ?? string.Empty, entry.Ttl, When.Always).ConfigureAwait(false);
                return true;
            case "hash":
                if (entry.HashFields.Count == 0)
                {
                    warnings.Add($"Skipped Redis hash '{entry.Key}' because Redis cannot persist an empty hash.");
                    return false;
                }

                await _db.HashSetAsync(entry.Key, entry.HashFields.Select(field => new HashEntry(field.Key, field.Value)).ToArray()).ConfigureAwait(false);
                await ApplyExpiryAsync(entry.Key, entry.Ttl).ConfigureAwait(false);
                return true;
            case "list":
                if (entry.ListItems.Count == 0)
                {
                    warnings.Add($"Skipped Redis list '{entry.Key}' because Redis cannot persist an empty list.");
                    return false;
                }

                await _db.ListRightPushAsync(entry.Key, entry.ListItems.Select(static item => (RedisValue)item).ToArray()).ConfigureAwait(false);
                await ApplyExpiryAsync(entry.Key, entry.Ttl).ConfigureAwait(false);
                return true;
            case "set":
                if (entry.SetMembers.Count == 0)
                {
                    warnings.Add($"Skipped Redis set '{entry.Key}' because Redis cannot persist an empty set.");
                    return false;
                }

                await _db.SetAddAsync(entry.Key, entry.SetMembers.Select(static member => (RedisValue)member).ToArray()).ConfigureAwait(false);
                await ApplyExpiryAsync(entry.Key, entry.Ttl).ConfigureAwait(false);
                return true;
            case "zset":
                if (entry.SortedSetMembers.Count == 0)
                {
                    warnings.Add($"Skipped Redis sorted set '{entry.Key}' because Redis cannot persist an empty sorted set.");
                    return false;
                }

                await _db.SortedSetAddAsync(entry.Key, entry.SortedSetMembers.Select(member => new SortedSetEntry(member.Member, member.Score)).ToArray()).ConfigureAwait(false);
                await ApplyExpiryAsync(entry.Key, entry.Ttl).ConfigureAwait(false);
                return true;
            default:
                throw new InvalidOperationException($"Unsupported Redis import type '{entry.Type}'.");
        }
    }

    private Task<bool> ApplyExpiryAsync(string key, TimeSpan? expiry)
        => expiry.HasValue ? _db.KeyExpireAsync(key, expiry) : Task.FromResult(false);

    public async Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = await _server.ExecuteAsync("SLOWLOG", new object[] { "GET", top }).ConfigureAwait(false);
            var entries = ParseSlowLogEntries(result);
            return new RedisSlowLogSummary(entries, entries.Count == top, top, RedisInsightCapability.Loaded);
        }
        catch (OperationCanceledException) { throw; }
        catch (RedisCommandException ex) when (
            ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            return new RedisSlowLogSummary([], false, top, RedisInsightCapability.Unsupported);
        }
        catch (RedisServerException ex) when (IsPermissionError(ex.Message))
        {
            return new RedisSlowLogSummary([], false, top, RedisInsightCapability.PermissionLimited);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SLOWLOG GET failed; reporting the slow log as unavailable.");
            return new RedisSlowLogSummary([], false, top, RedisInsightCapability.Failed);
        }
    }

    public async Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(
        string? pattern = null,
        int maxChannels = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var channelPattern = string.IsNullOrEmpty(pattern) ? "*" : pattern;
            var channelsResult = await _server.ExecuteAsync("PUBSUB", new object[] { "CHANNELS", channelPattern }).ConfigureAwait(false);

            var allChannels = channelsResult.IsNull
                ? []
                : ((RedisResult[])channelsResult!)
                    .Select(r => r.ToString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToList();

            var truncated = allChannels.Count > maxChannels;
            var channels = truncated ? allChannels.Take(maxChannels).ToList() : allChannels;

            List<RedisPubSubChannelInfo> channelInfos;
            if (channels.Count > 0)
            {
                var numsubArgs = new List<object> { "NUMSUB" };
                numsubArgs.AddRange(channels);
                var numsubResult = await _server.ExecuteAsync("PUBSUB", numsubArgs).ConfigureAwait(false);
                channelInfos = ParseNumsubResult(numsubResult, channels);
            }
            else
            {
                channelInfos = [];
            }

            var numpatResult = await _server.ExecuteAsync("PUBSUB", new object[] { "NUMPAT" }).ConfigureAwait(false);
            var patternCount = numpatResult.IsNull ? 0L : (long)numpatResult;

            return new RedisPubSubSnapshot(channelInfos, patternCount, truncated, maxChannels, RedisInsightCapability.Loaded);
        }
        catch (OperationCanceledException) { throw; }
        catch (RedisCommandException ex) when (
            ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            return new RedisPubSubSnapshot([], 0, false, maxChannels, RedisInsightCapability.Unsupported);
        }
        catch (RedisServerException ex) when (IsPermissionError(ex.Message))
        {
            return new RedisPubSubSnapshot([], 0, false, maxChannels, RedisInsightCapability.PermissionLimited);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis PUBSUB introspection failed; reporting the pub/sub snapshot as unavailable.");
            return new RedisPubSubSnapshot([], 0, false, maxChannels, RedisInsightCapability.Failed);
        }
    }

    /// <summary>Maps a StackExchange.Redis type onto the app-facing type token.</summary>
    /// <remarks>Internal rather than private so the mapping is unit-testable.</remarks>
    internal static string ToTypeString(RedisType type) => type switch
    {
        RedisType.String => "string",
        RedisType.Hash => "hash",
        RedisType.List => "list",
        RedisType.Set => "set",
        RedisType.SortedSet => "zset",
        RedisType.Stream => "stream",
        _ => "none"
    };

    private static Dictionary<string, string> ParseInfo(IGrouping<string, KeyValuePair<string, string>>[] sections)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            foreach (var kvp in section)
            {
                map[kvp.Key] = kvp.Value;
            }
        }

        return map;
    }

    private static IReadOnlyList<RedisDatabaseInfo> ParseDbStats(IGrouping<string, KeyValuePair<string, string>>[] sections)
    {
        var result = new List<RedisDatabaseInfo>();

        foreach (var section in sections)
        {
            foreach (var kvp in section)
            {
                if (!kvp.Key.StartsWith("db", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(kvp.Key.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dbIndex))
                    continue;

                var parts = kvp.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in parts)
                {
                    var idx = part.IndexOf('=');
                    if (idx <= 0 || idx >= part.Length - 1)
                        continue;

                    values[part[..idx]] = part[(idx + 1)..];
                }

                result.Add(new RedisDatabaseInfo
                {
                    Index = dbIndex,
                    Keys = ParseLong(values.GetValueOrDefault("keys")),
                    Expires = ParseLong(values.GetValueOrDefault("expires")),
                    AvgTtl = ParseLong(values.GetValueOrDefault("avg_ttl"))
                });
            }
        }

        return result.OrderBy(x => x.Index).ToList();
    }

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static long? ParseNullableLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string GetString(Dictionary<string, string> metrics, string key) =>
        metrics.TryGetValue(key, out var value) ? value : string.Empty;

    private static long GetLong(Dictionary<string, string> metrics, string key) =>
        ParseLong(GetString(metrics, key));

    /// <remarks>Internal rather than private so SLOWLOG parsing is unit-testable.</remarks>
    internal static IReadOnlyList<RedisSlowLogEntryInfo> ParseSlowLogEntries(RedisResult result)
    {
        if (result.IsNull)
            return [];

        var rows = (RedisResult[])result!;
        var entries = new List<RedisSlowLogEntryInfo>(rows.Length);

        foreach (var row in rows)
        {
            if (row.IsNull)
                continue;

            var fields = (RedisResult[])row!;
            if (fields.Length < 4)
                continue;

            if (!long.TryParse(fields[0].ToString(), out var id))
                continue;
            if (!long.TryParse(fields[1].ToString(), out var unixSeconds))
                continue;
            if (!long.TryParse(fields[2].ToString(), out var durationMicros))
                continue;

            var cmdArgs = fields[3].IsNull ? [] : (RedisResult[])fields[3]!;
            var command = cmdArgs.Length > 0 ? cmdArgs[0].ToString() ?? string.Empty : string.Empty;
            var arguments = cmdArgs.Length > 1
                ? string.Join(" ", cmdArgs.Skip(1).Select(a => a.ToString() ?? string.Empty))
                : string.Empty;

            string? clientName = fields.Length >= 6 ? fields[5].ToString() : null;
            if (string.IsNullOrEmpty(clientName))
                clientName = null;

            entries.Add(new RedisSlowLogEntryInfo(
                id,
                DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
                TimeSpan.FromMicroseconds(durationMicros),
                command,
                arguments,
                clientName));
        }

        return entries;
    }

    /// <remarks>Internal rather than private so PUBSUB NUMSUB parsing is unit-testable.</remarks>
    internal static List<RedisPubSubChannelInfo> ParseNumsubResult(RedisResult result, List<string> channels)
    {
        if (result.IsNull)
            return channels.Select(c => new RedisPubSubChannelInfo(c, 0)).ToList();

        var pairs = (RedisResult[])result!;
        var channelInfos = new List<RedisPubSubChannelInfo>(pairs.Length / 2);
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            var channel = pairs[i].ToString() ?? string.Empty;
            _ = long.TryParse(pairs[i + 1].ToString(), out var count);
            channelInfos.Add(new RedisPubSubChannelInfo(channel, count));
        }

        return channelInfos;
    }

    /// <remarks>Internal rather than private so the error classification is unit-testable.</remarks>
    internal static bool IsPermissionError(string message) =>
        message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not allowed", StringComparison.OrdinalIgnoreCase);
}
