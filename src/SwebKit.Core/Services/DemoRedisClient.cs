using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory Redis-like client that supports key browsing, inspection, and mutation for demo/testing.
/// </summary>
public sealed class DemoRedisClient : IRedisClient
{
    private readonly int _database;
    private readonly Dictionary<int, Dictionary<string, DemoValue>> _databases = [];

    public DemoRedisClient(int database = 0)
    {
        _database = Math.Clamp(database, 0, 15);
        Seed();
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        PruneExpired(db);

        pageSize = Math.Max(1, pageSize);
        var start = (int)Math.Max(0, cursor);
        var keys = db.Keys
            .Where(k => MatchesPattern(k, pattern))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        if (start >= keys.Count)
        {
            return Task.FromResult(new KeyScanResult
            {
                Cursor = 0,
                Keys = [],
                IsComplete = true
            });
        }

        var page = keys.Skip(start).Take(pageSize).ToList();
        var nextCursor = start + page.Count;
        var isComplete = nextCursor >= keys.Count;

        return Task.FromResult(new KeyScanResult
        {
            Cursor = isComplete ? 0 : nextCursor,
            Keys = page,
            IsComplete = isComplete
        });
    }

    public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        return Task.FromResult(TryGetValue(db, key, out var value) ? value.Type : "none");
    }

    public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value))
        {
            return Task.FromResult(new RedisKeyInfo
            {
                Key = key,
                Type = "none",
                Ttl = null,
                MemoryBytes = null,
                Encoding = null
            });
        }

        return Task.FromResult(new RedisKeyInfo
        {
            Key = key,
            Type = value.Type,
            Ttl = GetTtl(value),
            MemoryBytes = EstimateBytes(value),
            Encoding = GetEncoding(value.Type),
            Frequency = value.Frequency,
            IdleSeconds = value.IdleSeconds
        });
    }

    public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "string")
            return Task.FromResult<string?>(null);

        return Task.FromResult(value.Value as string);
    }

    public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "hash")
            return Task.FromResult<IReadOnlyList<RedisHashField>>([]);

        var fields = ((Dictionary<string, string>)value.Value)
            .Select(kvp => new RedisHashField { Field = kvp.Key, Value = kvp.Value })
            .OrderBy(x => x.Field, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<RedisHashField>>(fields);
    }

    public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "list")
            return Task.FromResult<IReadOnlyList<string>>([]);

        var items = (List<string>)value.Value;
        var (from, to) = NormalizeRange(items.Count, start, stop);
        if (from > to)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.FromResult<IReadOnlyList<string>>(items.Skip(from).Take(to - from + 1).ToList());
    }

    public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "set")
            return Task.FromResult<IReadOnlyList<string>>([]);

        var members = ((HashSet<string>)value.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return Task.FromResult<IReadOnlyList<string>>(members);
    }

    public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "zset")
            return Task.FromResult<IReadOnlyList<RedisSortedSetEntry>>([]);

        var entries = ((List<RedisSortedSetEntry>)value.Value)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Member, StringComparer.Ordinal)
            .ToList();

        var (from, to) = NormalizeRange(entries.Count, start, stop);
        if (from > to)
            return Task.FromResult<IReadOnlyList<RedisSortedSetEntry>>([]);

        return Task.FromResult<IReadOnlyList<RedisSortedSetEntry>>(entries.Skip(from).Take(to - from + 1).ToList());
    }

    public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        db[key] = new DemoValue("string", value, ToExpiry(expiry));
        return Task.CompletedTask;
    }

    public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();

        if (!TryGetValue(db, key, out var existing) || existing.Type != "hash")
        {
            existing = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal), null);
            db[key] = existing;
        }

        ((Dictionary<string, string>)existing.Value)[field] = value;
        return Task.CompletedTask;
    }

    public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        foreach (var key in keys)
            db.Remove(key);

        return Task.CompletedTask;
    }

    public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
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

            if (!overwriteExisting && db.ContainsKey(entry.Key))
            {
                result.SkippedCount++;
                result.Warnings.Add($"Skipped existing Redis key '{entry.Key}'.");
                continue;
            }

            db.Remove(entry.Key);
            if (!TryImportEntry(db, entry, result.Warnings))
            {
                result.SkippedCount++;
                continue;
            }

            result.ImportedCount++;
        }

        return Task.FromResult(result);
    }

    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value))
            return Task.FromResult<TimeSpan?>(null);

        return Task.FromResult(GetTtl(value));
    }

    public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (TryGetValue(db, key, out var value))
            value.ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);

        return Task.CompletedTask;
    }

    public Task RemoveTtlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (TryGetValue(db, key, out var value))
            value.ExpiresAt = null;

        return Task.CompletedTask;
    }

    public Task FlushDatabaseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        db.Clear();
        return Task.CompletedTask;
    }

    public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "zset")
            return Task.CompletedTask;

        var entries = (List<RedisSortedSetEntry>)value.Value;
        var existing = entries.FirstOrDefault(e => e.Member == member);
        if (existing is not null)
            existing.Score = score;

        return Task.CompletedTask;
    }

    public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!db.Remove(oldKey, out var value))
            throw new InvalidOperationException($"Key '{oldKey}' does not exist.");

        db[newKey] = value;
        return Task.CompletedTask;
    }

    public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "hash")
            return Task.CompletedTask;

        ((Dictionary<string, string>)value.Value).Remove(field);
        return Task.CompletedTask;
    }

    public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        if (!TryGetValue(db, key, out var value) || value.Type != "set")
            return Task.FromResult(new SetScanResult([], 0, true));

        var members = ((HashSet<string>)value.Value)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var start = (int)Math.Max(0, cursor);
        if (start >= members.Count)
            return Task.FromResult(new SetScanResult([], 0, true));

        var page = members.Skip(start).Take(pageSize).ToList();
        var nextCursor = start + page.Count;
        var isComplete = nextCursor >= members.Count;

        return Task.FromResult(new SetScanResult(page, isComplete ? 0 : nextCursor, isComplete));
    }

    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = GetDb();
        PruneExpired(db);

        var totalBytes = db.Values.Sum(EstimateBytes);
        var expiring = db.Values.Count(v => v.ExpiresAt.HasValue);

        var info = new RedisServerInfo
        {
            RedisVersion = "7.2-demo",
            UptimeSeconds = 86_400,
            ConnectedClients = 3,
            UsedMemoryBytes = totalBytes,
            UsedMemoryHuman = ToHumanBytes(totalBytes),
            TotalCommandsProcessed = 1_024,
            KeyspaceHitRatio = 0.93,
            Databases =
            [
                new RedisDatabaseInfo
                {
                    Index = _database,
                    Keys = db.Count,
                    Expires = expiring,
                    AvgTtl = expiring == 0
                        ? 0
                        : (long)db.Values
                            .Where(v => v.ExpiresAt.HasValue)
                            .Select(v => Math.Max(0, (v.ExpiresAt!.Value - DateTimeOffset.UtcNow).TotalMilliseconds))
                            .DefaultIfEmpty(0)
                            .Average()
                }
            ]
        };

        return Task.FromResult(info);
    }

    public void Dispose()
    {
    }

    public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var entries = new List<RedisSlowLogEntryInfo>
        {
            new(1, now.AddSeconds(-300), TimeSpan.FromMilliseconds(48.3), "HGETALL", "user:profile:1001", "worker-1"),
            new(2, now.AddSeconds(-210), TimeSpan.FromMilliseconds(41.7), "KEYS", "*", null),
            new(3, now.AddSeconds(-120), TimeSpan.FromMilliseconds(25.4), "SMEMBERS", "cache:categories", "reader-2"),
            new(4, now.AddSeconds(-60), TimeSpan.FromMilliseconds(19.8), "HGETALL", "user:profile:1002", "worker-1"),
            new(5, now.AddSeconds(-10), TimeSpan.FromMilliseconds(12.1), "LRANGE", "cache:products 0 -1", null),
        };

        var limited = entries.Take(top).ToList();
        return Task.FromResult(new RedisSlowLogSummary(
            limited,
            limited.Count == top,
            top,
            RedisInsightCapability.Loaded));
    }

    public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(
        string? pattern = null,
        int maxChannels = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var allChannels = new List<RedisPubSubChannelInfo>
        {
            new("notifications:global", 14),
            new("notifications:user:1001", 2),
            new("events:orders", 7),
            new("events:inventory", 3),
            new("metrics:realtime", 5),
            new("heartbeat", 1),
        };

        var filtered = string.IsNullOrEmpty(pattern)
            ? allChannels
            : allChannels
                .Where(c => c.Channel.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                .ToList();

        var truncated = filtered.Count > maxChannels;
        var channels = truncated ? filtered.Take(maxChannels).ToList() : filtered;

        return Task.FromResult(new RedisPubSubSnapshot(
            channels,
            2,
            truncated,
            maxChannels,
            RedisInsightCapability.Loaded));
    }

    private Dictionary<string, DemoValue> GetDb()
    {
        if (!_databases.TryGetValue(_database, out var db))
        {
            db = new Dictionary<string, DemoValue>(StringComparer.Ordinal);
            _databases[_database] = db;
        }

        return db;
    }

    private static bool TryImportEntry(Dictionary<string, DemoValue> db, RedisImportEntry entry, ICollection<string> warnings)
    {
        var expiry = ToExpiry(entry.Ttl);
        switch (entry.Type.Trim().ToLowerInvariant())
        {
            case "string":
                db[entry.Key] = new DemoValue("string", entry.StringValue ?? string.Empty, expiry);
                return true;
            case "hash":
                if (entry.HashFields.Count == 0)
                {
                    warnings.Add($"Skipped Redis hash '{entry.Key}' because Redis cannot persist an empty hash.");
                    return false;
                }

                db[entry.Key] = new DemoValue("hash", new Dictionary<string, string>(entry.HashFields, StringComparer.Ordinal), expiry);
                return true;
            case "list":
                if (entry.ListItems.Count == 0)
                {
                    warnings.Add($"Skipped Redis list '{entry.Key}' because Redis cannot persist an empty list.");
                    return false;
                }

                db[entry.Key] = new DemoValue("list", new List<string>(entry.ListItems), expiry);
                return true;
            case "set":
                if (entry.SetMembers.Count == 0)
                {
                    warnings.Add($"Skipped Redis set '{entry.Key}' because Redis cannot persist an empty set.");
                    return false;
                }

                db[entry.Key] = new DemoValue("set", new HashSet<string>(entry.SetMembers, StringComparer.Ordinal), expiry);
                return true;
            case "zset":
                if (entry.SortedSetMembers.Count == 0)
                {
                    warnings.Add($"Skipped Redis sorted set '{entry.Key}' because Redis cannot persist an empty sorted set.");
                    return false;
                }

                db[entry.Key] = new DemoValue("zset", entry.SortedSetMembers.Select(member => new RedisSortedSetEntry
                {
                    Member = member.Member,
                    Score = member.Score
                }).ToList(), expiry);
                return true;
            default:
                throw new InvalidOperationException($"Unsupported Redis import type '{entry.Type}'.");
        }
    }

    private static bool TryGetValue(Dictionary<string, DemoValue> db, string key, out DemoValue value)
    {
        if (!db.TryGetValue(key, out value!))
            return false;

        if (value.ExpiresAt.HasValue && value.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            db.Remove(key);
            value = default!;
            return false;
        }

        return true;
    }

    private static TimeSpan? GetTtl(DemoValue value)
    {
        if (!value.ExpiresAt.HasValue)
            return null;

        var ttl = value.ExpiresAt.Value - DateTimeOffset.UtcNow;
        return ttl <= TimeSpan.Zero ? TimeSpan.Zero : ttl;
    }

    private static DateTimeOffset? ToExpiry(TimeSpan? ttl) =>
        ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null;

    private static string GetEncoding(string type) => type switch
    {
        "string" => "embstr",
        "hash" => "hashtable",
        "list" => "quicklist",
        "set" => "hashtable",
        "zset" => "skiplist",
        "stream" => "listpack",
        _ => "unknown"
    };

    private static long EstimateBytes(DemoValue value) => value.Type switch
    {
        "string" => ((string)value.Value).Length,
        "hash" => ((Dictionary<string, string>)value.Value).Sum(x => x.Key.Length + x.Value.Length),
        "list" => ((List<string>)value.Value).Sum(x => x.Length),
        "set" => ((HashSet<string>)value.Value).Sum(x => x.Length),
        "zset" => ((List<RedisSortedSetEntry>)value.Value).Sum(x => x.Member.Length + sizeof(double)),
        _ => 0
    };

    private static void PruneExpired(Dictionary<string, DemoValue> db)
    {
        var expired = db.Where(x => x.Value.ExpiresAt.HasValue && x.Value.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expired)
            db.Remove(key);
    }

    private static bool MatchesPattern(string input, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
            return true;

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.CultureInvariant);
    }

    private static (int Start, int End) NormalizeRange(int count, long start, long stop)
    {
        if (count == 0)
            return (1, 0);

        var from = start < 0 ? count + (int)start : (int)start;
        var to = stop < 0 ? count + (int)stop : (int)stop;

        from = Math.Clamp(from, 0, count - 1);
        to = Math.Clamp(to, 0, count - 1);
        return (from, to);
    }

    private static string ToHumanBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;

        if (bytes >= mb)
            return $"{bytes / mb:0.00}M";
        if (bytes >= kb)
            return $"{bytes / kb:0.00}K";

        return $"{bytes}B";
    }

    private void Seed()
    {
        var db = GetDb();

        db["user:1001"] = new DemoValue("string", "{\"id\":1001,\"name\":\"Alice\",\"email\":\"alice@example.com\"}", DateTimeOffset.UtcNow.AddHours(1), frequency: 12, idleSeconds: 8);
        db["user:1002"] = new DemoValue("string", "{\"id\":1002,\"name\":\"Bob\",\"email\":\"bob@example.com\"}", DateTimeOffset.UtcNow.AddHours(1));
        db["session:abc123"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user_id"] = "1001",
            ["ip"] = "10.0.0.1",
            ["created"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")
        }, DateTimeOffset.UtcNow.AddMinutes(30));
        db["session:def456"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user_id"] = "1002",
            ["ip"] = "10.0.0.2",
            ["created"] = DateTimeOffset.UtcNow.AddMinutes(-8).ToString("O")
        }, DateTimeOffset.UtcNow.AddMinutes(30));
        db["cache:products"] = new DemoValue("list", Enumerable.Range(1, 10).Select(x => $"product-{x}").ToList(), DateTimeOffset.UtcNow.AddMinutes(5));
        db["cache:categories"] = new DemoValue("set", new HashSet<string>(["electronics", "clothing", "food", "books"], StringComparer.Ordinal), null);
        db["leaderboard:daily"] = new DemoValue("zset", new List<RedisSortedSetEntry>
        {
            new() { Member = "alice", Score = 1500 },
            new() { Member = "bob", Score = 1200 },
            new() { Member = "charlie", Score = 900 }
        }, null);
        db["config:feature-flags"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dark_mode"] = "true",
            ["beta_api"] = "false",
            ["max_retries"] = "3"
        }, null);
        db["rate-limit:api:10.0.0.1"] = new DemoValue("string", "42", DateTimeOffset.UtcNow.AddSeconds(60), frequency: 46, idleSeconds: 2);
        db["rate-limit:api:10.0.0.2"] = new DemoValue("string", "17", DateTimeOffset.UtcNow.AddSeconds(45));
        db["lock:inventory-sync"] = new DemoValue("string", "worker-1", DateTimeOffset.UtcNow.AddSeconds(30), frequency: 18, idleSeconds: 5);
        db["lock:payment-batch"] = new DemoValue("string", "worker-2", DateTimeOffset.UtcNow.AddSeconds(120));

        // Additional namespace-rich keys for tree grouping demo
        db["user:profile:1001"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["display_name"] = "Alice",
            ["avatar_url"] = "https://example.com/alice.png",
            ["locale"] = "en-US"
        }, null);
        db["user:profile:1002"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["display_name"] = "Bob",
            ["avatar_url"] = "https://example.com/bob.png",
            ["locale"] = "fr-FR"
        }, null);
        db["user:preferences:1001"] = new DemoValue("string", "{\"theme\":\"dark\",\"notifications\":true}", null);
        db["session:ghi789"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user_id"] = "1001",
            ["ip"] = "10.0.0.3",
            ["created"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
        }, DateTimeOffset.UtcNow.AddMinutes(30));
        db["cache:homepage"] = new DemoValue("string", "<html>cached homepage</html>", DateTimeOffset.UtcNow.AddMinutes(10));
        db["cache:search:results:electronics"] = new DemoValue("list", new List<string> { "item-1", "item-2", "item-3" }, DateTimeOffset.UtcNow.AddMinutes(2));
        db["config:rate-limits"] = new DemoValue("hash", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_calls_per_minute"] = "100",
            ["websocket_connections"] = "50"
        }, null);
        db["metrics:api:latency"] = new DemoValue("zset", new List<RedisSortedSetEntry>
        {
            new() { Member = "/users", Score = 42.5 },
            new() { Member = "/orders", Score = 85.2 },
            new() { Member = "/products", Score = 23.1 }
        }, null);
        db["queue:emails:pending"] = new DemoValue("list", new List<string> { "msg-001", "msg-002", "msg-003", "msg-004" }, null);
        db["queue:notifications:pending"] = new DemoValue("list", new List<string> { "notif-001", "notif-002" }, null);
    }

    private sealed class DemoValue
    {
        public DemoValue(
            string type,
            object value,
            DateTimeOffset? expiresAt,
            long? frequency = null,
            long? idleSeconds = null)
        {
            Type = type;
            Value = value;
            ExpiresAt = expiresAt;
            Frequency = frequency;
            IdleSeconds = idleSeconds;
        }

        public string Type { get; }
        public object Value { get; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public long? Frequency { get; }
        public long? IdleSeconds { get; }
    }
}
