using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Redis;

namespace SwebKit.Core.Tests;

public class RedisClientTests
{
    // RedisClient connects to Redis on construction via ConnectionMultiplexer.Connect.
    // With AbortOnConnectFail = false, StackExchange.Redis does NOT throw on unreachable hosts —
    // it returns a disconnected multiplexer. Tests here cover inputs that fail before the
    // network attempt (null/empty/malformed connection string).

    [Fact]
    public async Task CreateAsync_NullConnectionString_Throws()
    {
        var entry = new RedisCacheEntry
        {
            ConnectionString = null!,
            Database = 0
        };

        await Assert.ThrowsAnyAsync<Exception>(() => RedisClient.CreateAsync(entry));
    }

    [Fact]
    public async Task CreateAsync_EmptyConnectionString_Throws()
    {
        var entry = new RedisCacheEntry
        {
            ConnectionString = string.Empty,
            Database = 0
        };

        await Assert.ThrowsAnyAsync<Exception>(() => RedisClient.CreateAsync(entry));
    }

    [Fact]
    public async Task CreateAsync_WhitespaceConnectionString_Throws()
    {
        var entry = new RedisCacheEntry
        {
            ConnectionString = "   ",
            Database = 0
        };

        await Assert.ThrowsAnyAsync<Exception>(() => RedisClient.CreateAsync(entry));
    }

    [Fact]
    public async Task CreateAsync_WithLogger_NullConnectionString_Throws()
    {
        var entry = new RedisCacheEntry
        {
            ConnectionString = null!,
            Database = 0
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => RedisClient.CreateAsync(entry, NullLogger<RedisClient>.Instance));
    }

    // ── RedisConfig.Validate() ──
    // These cover the validation guard that callers must pass before constructing RedisClient.

    [Fact]
    public void RedisConfig_Validate_NoCaches_Throws()
    {
        var config = new RedisConfig();

        var ex = Assert.Throws<InvalidOperationException>(config.Validate);
        Assert.Contains("at least one cache", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedisConfig_Validate_EmptyConnectionString_Throws()
    {
        var config = new RedisConfig();
        config.Caches.Add(new RedisCacheEntry { DisplayName = "Test", ConnectionString = string.Empty });

        var ex = Assert.Throws<InvalidOperationException>(config.Validate);
        Assert.Contains(nameof(RedisCacheEntry.ConnectionString), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RedisConfig_Validate_ValidEntry_DoesNotThrow()
    {
        var config = new RedisConfig();
        config.Caches.Add(new RedisCacheEntry { DisplayName = "Test", ConnectionString = "localhost:6379" });

        var ex = Record.Exception(config.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void RedisConfig_Validate_MultipleEntries_AllRequireConnectionString()
    {
        var config = new RedisConfig();
        config.Caches.Add(new RedisCacheEntry { DisplayName = "A", ConnectionString = "localhost:6379" });
        config.Caches.Add(new RedisCacheEntry { DisplayName = "B", ConnectionString = string.Empty });

        var ex = Assert.Throws<InvalidOperationException>(config.Validate);
        Assert.Contains("'B'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
