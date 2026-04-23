using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using StackExchange.Redis;

namespace SwebKit.Redis;

public sealed class RedisClient : IRedisClient
{
    private readonly RedisCacheEntry _cacheEntry;
    private readonly ConnectionMultiplexer _mux;
    private readonly IDatabase _db;
    private readonly IServer _server;
    private readonly ILogger<RedisClient> _logger;

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

    public static async Task<RedisClient> CreateAsync(RedisCacheEntry cacheEntry, ILogger<RedisClient>? logger = null)
    {
        logger ??= NullLogger<RedisClient>.Instance;
        var options = ConfigurationOptions.Parse(cacheEntry.ConnectionString);
        options.AbortOnConnectFail = false;
        var mux = await ConnectionMultiplexer.ConnectAsync(options);
        return new RedisClient(cacheEntry, mux, logger);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.PingAsync();
        return true;
    }

    public async Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        pageSize = Math.Max(1, pageSize);
        var result = await _db.ExecuteAsync("SCAN", cursor, "MATCH", pattern, "COUNT", pageSize);
        var scanPage = RedisScanResponseParser.Parse(result);

        return new KeyScanResult
        {
            Cursor = scanPage.Cursor,
            Keys = scanPage.Values,
            IsComplete = scanPage.IsComplete
        };
    }

    public async Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keyType = await _db.KeyTypeAsync(key);
        return ToTypeString(keyType);
    }

    public async Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var exists = await _db.KeyExistsAsync(key);
        if (!exists)
        {
            return new RedisKeyInfo
            {
                Key = key,
                Type = "none"
            };
        }

        var keyType = await _db.KeyTypeAsync(key);
        var ttl = await _db.KeyTimeToLiveAsync(key);
        var memoryBytes = await TryGetMemoryUsageAsync(key);
        var encoding = await TryGetEncodingAsync(key);
        var frequency = await TryGetFrequencyAsync(key);
        var idleSeconds = await TryGetIdleSecondsAsync(key);

        return new RedisKeyInfo
        {
            Key = key,
            Type = ToTypeString(keyType),
            Ttl = ttl,
            MemoryBytes = memoryBytes,
            Encoding = encoding,
            Frequency = frequency,
            IdleSeconds = idleSeconds
        };
    }

    public async Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await _db.StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }

    public async Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fields = await _db.HashGetAllAsync(key);
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
        var values = await _db.ListRangeAsync(key, start, stop);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = await _db.SetMembersAsync(key);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = await _db.SortedSetRangeByRankWithScoresAsync(key, start, stop, Order.Descending);
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
        await _db.StringSetAsync(key, value, expiry);
    }

    public async Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.HashSetAsync(key, field, value);
    }

    public async Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (keys.Count == 0)
            return;

        await _db.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
    }

    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.KeyTimeToLiveAsync(key);
    }

    public async Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyExpireAsync(key, ttl);
    }

    public async Task RemoveTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyPersistAsync(key);
    }

    public async Task FlushDatabaseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _server.FlushDatabaseAsync(Math.Clamp(_cacheEntry.Database, 0, 15));
    }

    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var metrics = ParseInfo(_server.Info());
        var keyspace = ParseDbStats(_server.Info("keyspace"));

        var hits = GetLong(metrics, "keyspace_hits");
        var misses = GetLong(metrics, "keyspace_misses");
        var ratio = hits + misses == 0 ? 0 : (double)hits / (hits + misses);

        var info = new RedisServerInfo
        {
            RedisVersion = GetString(metrics, "redis_version"),
            UptimeSeconds = GetLong(metrics, "uptime_in_seconds"),
            ConnectedClients = GetLong(metrics, "connected_clients"),
            UsedMemoryBytes = GetLong(metrics, "used_memory"),
            UsedMemoryHuman = GetString(metrics, "used_memory_human"),
            TotalCommandsProcessed = GetLong(metrics, "total_commands_processed"),
            KeyspaceHitRatio = ratio,
            Databases = keyspace
        };

        return Task.FromResult(info);
    }

    public async Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.SortedSetAddAsync(key, member, score, SortedSetWhen.Exists);
    }

    public async Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.KeyRenameAsync(oldKey, newKey);
    }

    public async Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _db.HashDeleteAsync(key, field);
    }

    public async Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        pageSize = Math.Max(1, pageSize);
        var result = await _db.ExecuteAsync("SSCAN", key, cursor, "COUNT", pageSize);
        var scanPage = RedisScanResponseParser.Parse(result);
        return new SetScanResult(scanPage.Values, scanPage.Cursor, scanPage.IsComplete);
    }

    public void Dispose()
    {
        _mux.Dispose();
    }

    public async Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = await _server.ExecuteAsync("SLOWLOG", new object[] { "GET", top });
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
        catch (Exception)
        {
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
            var channelsResult = await _server.ExecuteAsync("PUBSUB", new object[] { "CHANNELS", channelPattern });

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
                var numsubResult = await _server.ExecuteAsync("PUBSUB", numsubArgs);
                channelInfos = ParseNumsubResult(numsubResult, channels);
            }
            else
            {
                channelInfos = [];
            }

            var numpatResult = await _server.ExecuteAsync("PUBSUB", new object[] { "NUMPAT" });
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
        catch (Exception)
        {
            return new RedisPubSubSnapshot([], 0, false, maxChannels, RedisInsightCapability.Failed);
        }
    }

    private Task<long?> TryGetMemoryUsageAsync(string key) =>
        TryValueAsync(async () =>
        {
            var result = await _db.ExecuteAsync("MEMORY", "USAGE", key);
            return ParseLong(result.ToString());
        }, nameof(TryGetMemoryUsageAsync), key);

    private Task<string?> TryGetEncodingAsync(string key) =>
        TryAsync(async () =>
        {
            var result = await _db.ExecuteAsync("OBJECT", "ENCODING", key);
            var value = result.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }, nameof(TryGetEncodingAsync), key);

    private Task<long?> TryGetFrequencyAsync(string key) =>
        TryValueAsync(async () =>
        {
            var result = await _db.ExecuteAsync("OBJECT", "FREQ", key);
            var parsed = ParseNullableLong(result.ToString());
            if (!parsed.HasValue)
                throw new InvalidOperationException("Redis OBJECT FREQ did not return a numeric result.");

            return parsed.Value;
        }, nameof(TryGetFrequencyAsync), key);

    private Task<long?> TryGetIdleSecondsAsync(string key) =>
        TryValueAsync(async () =>
        {
            var result = await _db.ExecuteAsync("OBJECT", "IDLETIME", key);
            var parsed = ParseNullableLong(result.ToString());
            if (!parsed.HasValue)
                throw new InvalidOperationException("Redis OBJECT IDLETIME did not return a numeric result.");

            return parsed.Value;
        }, nameof(TryGetIdleSecondsAsync), key);

    private async Task<T?> TryAsync<T>(Func<Task<T?>> operation, string operationName, string key) where T : class
    {
        try { return await operation(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis {Operation} failed for key {Key}", operationName, key);
            return null;
        }
    }

    private async Task<T?> TryValueAsync<T>(Func<Task<T>> operation, string operationName, string key) where T : struct
    {
        try { return await operation(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis {Operation} failed for key {Key}", operationName, key);
            return (T?)null;
        }
    }

    private static string ToTypeString(RedisType type) => type switch
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

    private static IReadOnlyList<RedisSlowLogEntryInfo> ParseSlowLogEntries(RedisResult result)
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

    private static List<RedisPubSubChannelInfo> ParseNumsubResult(RedisResult result, List<string> channels)
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

    private static bool IsPermissionError(string message) =>
        message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not allowed", StringComparison.OrdinalIgnoreCase);
}
