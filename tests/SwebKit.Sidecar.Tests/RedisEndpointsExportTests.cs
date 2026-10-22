using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Delegates to a <see cref="DemoRedisClient"/>, but every <see cref="GetKeyInfoAsync"/> waits on
/// <see cref="Gate"/> and records how many reads are in flight — so a test can prove the export
/// issues its per-key round trips concurrently and can fail a chosen key.
/// </summary>
internal sealed class GatedExportRedisClient(Task gate) : IRedisClient
{
    private readonly DemoRedisClient _inner = new();
    private readonly object _lock = new();
    private int _inFlight;

    public Task Gate { get; } = gate;
    public int PeakInFlight { get; private set; }
    public string? FailKey { get; init; }

    public async Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _inFlight++;
            PeakInFlight = Math.Max(PeakInFlight, _inFlight);
        }

        try
        {
            await Gate.WaitAsync(ct);
            if (key == FailKey)
                throw new InvalidOperationException("READONLY");
            // DemoRedisClient isn't thread-safe; the fake serializes its own reads.
            lock (_lock)
                return _inner.GetKeyInfoAsync(key, ct).Result;
        }
        finally
        {
            lock (_lock)
                _inFlight--;
        }
    }

    public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) { lock (_lock) return _inner.GetKeyValueAsync(key, ct); }
    public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) { lock (_lock) return _inner.GetHashFieldsAsync(key, ct); }
    public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) { lock (_lock) return _inner.GetListItemsAsync(key, start, stop, ct); }
    public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) { lock (_lock) return _inner.GetSetMembersAsync(key, ct); }
    public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) { lock (_lock) return _inner.GetSortedSetMembersAsync(key, start, stop, ct); }

    // Not used by the export path.
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveTtlAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task FlushDatabaseAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => throw new NotSupportedException();
    public void Dispose() => _inner.Dispose();
}

public class RedisEndpointsExportTests
{
    private static readonly string[] Keys =
        ["leaderboard:daily", "user:1001", "cache:products", "missing:key", "session:abc123", "cache:categories"];

    [Fact]
    public async Task ExportKeysAsync_ReturnsEveryKey_InRequestOrder_WithTypedValues()
    {
        using var client = new DemoRedisClient();

        var data = await RedisEndpoints.ExportKeysAsync(client, Keys, CancellationToken.None);

        Assert.Equal(Keys, data.Keys);
        Assert.IsType<string>(data["user:1001"]);
        Assert.IsType<Dictionary<string, string>>(data["session:abc123"]);
        Assert.Equal(10, Assert.IsAssignableFrom<IReadOnlyList<string>>(data["cache:products"]).Count);
        Assert.IsAssignableFrom<IReadOnlyList<RedisSortedSetEntry>>(data["leaderboard:daily"]);
        Assert.Null(data["missing:key"]);
    }

    [Fact]
    public async Task ExportKeysAsync_ReadsKeysConcurrently()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new GatedExportRedisClient(gate.Task);

        var export = RedisEndpoints.ExportKeysAsync(client, Keys, CancellationToken.None);
        // Every key's first round trip is in flight before any of them completes — a serial loop
        // would have exactly one outstanding.
        Assert.True(SpinWait.SpinUntil(() => client.PeakInFlight == Keys.Length, TimeSpan.FromSeconds(5)));
        gate.SetResult();
        var data = await export;

        Assert.Equal(Keys, data.Keys);
    }

    [Fact]
    public async Task ExportKeysAsync_CapsInFlightReads()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new GatedExportRedisClient(gate.Task);
        var many = Enumerable.Range(0, RedisEndpoints.ExportConcurrency * 2).Select(i => $"k{i}").ToList();

        var export = RedisEndpoints.ExportKeysAsync(client, many, CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => client.PeakInFlight == RedisEndpoints.ExportConcurrency, TimeSpan.FromSeconds(5)));
        await Task.Delay(50);
        Assert.Equal(RedisEndpoints.ExportConcurrency, client.PeakInFlight);
        gate.SetResult();

        Assert.Equal(many, (await export).Keys);
    }

    [Fact]
    public async Task ExportKeysAsync_OneUnreadableKey_BecomesAPlaceholder_NotAFailedExport()
    {
        using var client = new GatedExportRedisClient(Task.CompletedTask) { FailKey = "user:1001" };

        var data = await RedisEndpoints.ExportKeysAsync(client, Keys, CancellationToken.None);

        Assert.Equal(Keys, data.Keys);
        Assert.Equal("<error reading key>", data["user:1001"]);
        Assert.IsType<Dictionary<string, string>>(data["session:abc123"]);
    }
}
