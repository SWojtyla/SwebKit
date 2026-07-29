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

    // ── BuildConnectionOptions() ──
    // GetServerInfoAsync/FlushDatabaseAsync/GetSlowLogAsync issue admin commands, which
    // StackExchange.Redis rejects with "This operation is not available unless admin mode
    // is enabled" unless AllowAdmin is set. These lock that in without a live server.

    [Fact]
    public void BuildConnectionOptions_EnablesAdminMode()
    {
        var options = RedisClient.BuildConnectionOptions("localhost:6379");

        Assert.True(options.AllowAdmin);
    }

    [Fact]
    public void BuildConnectionOptions_DoesNotAbortOnConnectFail()
    {
        var options = RedisClient.BuildConnectionOptions("localhost:6379");

        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void BuildConnectionOptions_PreservesEndpointFromConnectionString()
    {
        var options = RedisClient.BuildConnectionOptions("cache.example.net:6380,ssl=true");

        Assert.Single(options.EndPoints);
        Assert.Contains("cache.example.net", options.EndPoints[0].ToString(), StringComparison.Ordinal);
        Assert.True(options.Ssl);
    }

    [Fact]
    public void BuildConnectionOptions_AdminModeWins_WhenConnectionStringDisablesIt()
    {
        // An explicit allowAdmin=false in stored config would otherwise silently
        // re-break the server-info endpoint.
        var options = RedisClient.BuildConnectionOptions("localhost:6379,allowAdmin=false");

        Assert.True(options.AllowAdmin);
    }

    [Fact]
    public void BuildConnectionOptions_NullConnectionString_Throws()
    {
        Assert.ThrowsAny<Exception>(() => RedisClient.BuildConnectionOptions(null!));
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
