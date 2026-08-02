using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Delegates every call to a real <see cref="DemoRedisClient"/> (sealed, so composition rather than
/// inheritance) except for whichever single mutation method a test configures to throw instead â€”
/// used to prove the mutation endpoints surface a client failure rather than swallowing it.
/// </summary>
internal sealed class FaultInjectingRedisClient : IRedisClient
{
    private readonly IRedisClient _inner;

    public FaultInjectingRedisClient(IRedisClient inner) => _inner = inner;

    public Exception? ThrowOnSetHashField { get; set; }
    public Exception? ThrowOnDeleteHashField { get; set; }
    public Exception? ThrowOnUpdateSortedSetScore { get; set; }
    public Exception? ThrowOnRenameKey { get; set; }
    public Exception? ThrowOnSetTtl { get; set; }
    public Exception? ThrowOnRemoveTtl { get; set; }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default) => _inner.ScanKeysAsync(pattern, cursor, pageSize, ct);
    public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => _inner.GetKeyTypeAsync(key, ct);
    public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => _inner.GetKeyInfoAsync(key, ct);
    public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => _inner.GetKeyValueAsync(key, ct);
    public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => _inner.GetHashFieldsAsync(key, ct);
    public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => _inner.GetListItemsAsync(key, start, stop, ct);
    public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => _inner.GetSetMembersAsync(key, ct);
    public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => _inner.GetSortedSetMembersAsync(key, start, stop, ct);
    public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default) => _inner.SetKeyValueAsync(key, value, expiry, ct);

    public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) =>
        ThrowOnSetHashField is not null ? Task.FromException(ThrowOnSetHashField) : _inner.SetHashFieldAsync(key, field, value, ct);

    public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) => _inner.DeleteKeysAsync(keys, ct);
    public Task<RedisImportResult> ImportAsync(IReadOnlyList<RedisImportEntry> entries, bool overwriteExisting = true, CancellationToken ct = default) => _inner.ImportAsync(entries, overwriteExisting, ct);
    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => _inner.GetTtlAsync(key, ct);

    public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        ThrowOnSetTtl is not null ? Task.FromException(ThrowOnSetTtl) : _inner.SetTtlAsync(key, ttl, ct);

    public Task RemoveTtlAsync(string key, CancellationToken ct = default) =>
        ThrowOnRemoveTtl is not null ? Task.FromException(ThrowOnRemoveTtl) : _inner.RemoveTtlAsync(key, ct);

    public Task FlushDatabaseAsync(CancellationToken ct = default) => _inner.FlushDatabaseAsync(ct);
    public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default) => _inner.GetServerInfoAsync(ct);

    public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) =>
        ThrowOnUpdateSortedSetScore is not null ? Task.FromException(ThrowOnUpdateSortedSetScore) : _inner.UpdateSortedSetScoreAsync(key, member, score, ct);

    public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default) =>
        ThrowOnRenameKey is not null ? Task.FromException(ThrowOnRenameKey) : _inner.RenameKeyAsync(oldKey, newKey, ct);

    public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) =>
        ThrowOnDeleteHashField is not null ? Task.FromException(ThrowOnDeleteHashField) : _inner.DeleteHashFieldAsync(key, field, ct);

    public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => _inner.GetSetMembersPageAsync(key, cursor, pageSize, ct);
    public Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default) => _inner.GetSlowLogAsync(top, ct);
    public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => _inner.GetPubSubSnapshotAsync(pattern, maxChannels, ct);
    public void Dispose() => _inner.Dispose();
}

/// <summary>Records which cache entries were requested and returns a configurable client.</summary>
internal sealed class FakeRedisClientFactory : IRedisClientFactory
{
    public IRedisClient Client { get; set; } = new DemoRedisClient();
    public List<RedisCacheEntry> Calls { get; } = [];

    public Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default)
    {
        Calls.Add(cacheEntry);
        return Task.FromResult(Client);
    }
}

public class RedisEndpointsMutationTests
{
    private const string CacheId = "cache-1";

    private static (ProfileRepository Profile, DemoModeService Demo, FakeRedisClientFactory Factory) Build(IRedisClient? client = null)
    {
        var profile = new ProfileRepository();
        profile.Config.RedisConfig = new RedisConfig
        {
            Caches = [new RedisCacheEntry { Id = CacheId, DisplayName = "Cache 1", ConnectionString = "localhost:6379" }]
        };
        var demo = new DemoModeService();
        var factory = new FakeRedisClientFactory();
        if (client is not null)
            factory.Client = client;
        return (profile, demo, factory);
    }

    // â”€â”€ Hash field set â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task SetHashFieldAsync_Success_WritesFieldOnUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.SetHashFieldRequest { Field = "newfield", Value = "newvalue" };

        var result = await RedisEndpoints.SetHashFieldAsync(CacheId, "session:abc123", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        var fields = await demoClient.GetHashFieldsAsync("session:abc123");
        Assert.Contains(fields, f => f.Field == "newfield" && f.Value == "newvalue");
    }

    [Fact]
    public async Task SetHashFieldAsync_CacheNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();
        var req = new RedisEndpoints.SetHashFieldRequest { Field = "f", Value = "v" };

        var result = await RedisEndpoints.SetHashFieldAsync("no-such-cache", "key", req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task SetHashFieldAsync_ClientThrows_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnSetHashField = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.SetHashFieldRequest { Field = "f", Value = "v" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.SetHashFieldAsync(CacheId, "session:abc123", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }

    // â”€â”€ Hash field delete â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task DeleteHashFieldAsync_Success_RemovesFieldFromUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.DeleteHashFieldRequest { Field = "user_id" };

        var result = await RedisEndpoints.DeleteHashFieldAsync(CacheId, "session:abc123", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        var fields = await demoClient.GetHashFieldsAsync("session:abc123");
        Assert.DoesNotContain(fields, f => f.Field == "user_id");
    }

    [Fact]
    public async Task DeleteHashFieldAsync_CacheNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();
        var req = new RedisEndpoints.DeleteHashFieldRequest { Field = "f" };

        var result = await RedisEndpoints.DeleteHashFieldAsync("no-such-cache", "key", req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task DeleteHashFieldAsync_ClientThrows_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnDeleteHashField = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.DeleteHashFieldRequest { Field = "user_id" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.DeleteHashFieldAsync(CacheId, "session:abc123", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }

    // â”€â”€ Sorted set score update â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task UpdateSortedSetScoreAsync_Success_UpdatesScoreOnUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.UpdateSortedSetScoreRequest { Member = "alice", Score = 9999 };

        var result = await RedisEndpoints.UpdateSortedSetScoreAsync(CacheId, "leaderboard:daily", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        var members = await demoClient.GetSortedSetMembersAsync("leaderboard:daily");
        Assert.Contains(members, m => m.Member == "alice" && m.Score == 9999);
    }

    [Fact]
    public async Task UpdateSortedSetScoreAsync_CacheNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();
        var req = new RedisEndpoints.UpdateSortedSetScoreRequest { Member = "alice", Score = 1 };

        var result = await RedisEndpoints.UpdateSortedSetScoreAsync("no-such-cache", "key", req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task UpdateSortedSetScoreAsync_ClientThrows_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnUpdateSortedSetScore = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.UpdateSortedSetScoreRequest { Member = "alice", Score = 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.UpdateSortedSetScoreAsync(CacheId, "leaderboard:daily", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }

    // â”€â”€ Key rename â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task RenameKeyAsync_Success_RenamesOnUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.RenameKeyRequest { NewKey = "user:1001:renamed" };

        var result = await RedisEndpoints.RenameKeyAsync(CacheId, "user:1001", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        Assert.Equal("none", await demoClient.GetKeyTypeAsync("user:1001"));
        Assert.Equal("string", await demoClient.GetKeyTypeAsync("user:1001:renamed"));
    }

    [Fact]
    public async Task RenameKeyAsync_CacheNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();
        var req = new RedisEndpoints.RenameKeyRequest { NewKey = "new" };

        var result = await RedisEndpoints.RenameKeyAsync("no-such-cache", "key", req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task RenameKeyAsync_SourceKeyMissing_ExceptionPropagates_NotSwallowed()
    {
        // DemoRedisClient.RenameKeyAsync throws InvalidOperationException when the source key doesn't
        // exist â€” proves the endpoint doesn't catch/swallow that into a false "Ok".
        var (profile, demo, factory) = Build(new DemoRedisClient());
        var req = new RedisEndpoints.RenameKeyRequest { NewKey = "whatever" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.RenameKeyAsync(CacheId, "key-that-does-not-exist", req, profile, factory, demo, CancellationToken.None));
    }

    [Fact]
    public async Task RenameKeyAsync_ClientThrows_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnRenameKey = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.RenameKeyRequest { NewKey = "user:new" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.RenameKeyAsync(CacheId, "user:1001", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }

    // â”€â”€ TTL set/remove â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task SetTtlAsync_TtlSecondsProvided_SetsTtlOnUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.SetTtlRequest { TtlSeconds = 120, RemoveTtl = false };

        var result = await RedisEndpoints.SetTtlAsync(CacheId, "lock:payment-batch", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        var ttl = await demoClient.GetTtlAsync("lock:payment-batch");
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 100, 121);
    }

    [Fact]
    public async Task SetTtlAsync_RemoveTtl_ClearsTtlOnUnderlyingClient()
    {
        var demoClient = new DemoRedisClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new RedisEndpoints.SetTtlRequest { RemoveTtl = true };

        var result = await RedisEndpoints.SetTtlAsync(CacheId, "user:1001", req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        Assert.Null(await demoClient.GetTtlAsync("user:1001"));
    }

    [Fact]
    public async Task SetTtlAsync_CacheNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();
        var req = new RedisEndpoints.SetTtlRequest { RemoveTtl = true };

        var result = await RedisEndpoints.SetTtlAsync("no-such-cache", "key", req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task SetTtlAsync_ClientThrows_OnSet_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnSetTtl = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.SetTtlRequest { TtlSeconds = 60 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.SetTtlAsync(CacheId, "user:1001", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }

    [Fact]
    public async Task SetTtlAsync_ClientThrows_OnRemove_ExceptionPropagates()
    {
        var faulty = new FaultInjectingRedisClient(new DemoRedisClient()) { ThrowOnRemoveTtl = new InvalidOperationException("redis down") };
        var (profile, demo, factory) = Build(faulty);
        var req = new RedisEndpoints.SetTtlRequest { RemoveTtl = true };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RedisEndpoints.SetTtlAsync(CacheId, "user:1001", req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("redis down", ex.Message);
    }
}
