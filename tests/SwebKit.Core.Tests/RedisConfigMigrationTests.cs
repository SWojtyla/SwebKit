using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class RedisConfigMigrationTests
{
    [Fact]
    public void EnsureMigrated_WithLegacyFields_CreatesCache()
    {
        var config = new RedisConfig
        {
            ConnectionString = "localhost:6379",
            Alias = "Dev Cache",
            Database = 3
        };

        config.EnsureMigrated();

        Assert.Single(config.Caches);
        var cache = config.Caches[0];
        Assert.Equal("Dev Cache", cache.DisplayName);
        Assert.Equal("localhost:6379", cache.ConnectionString);
        Assert.Equal(3, cache.Database);
        Assert.Equal(cache.Id, config.ActiveCacheId);
    }

    [Fact]
    public void EnsureMigrated_ClearsLegacyFields()
    {
        var config = new RedisConfig
        {
            ConnectionString = "localhost:6379",
            Alias = "Old",
            Database = 1
        };

        config.EnsureMigrated();

        Assert.Null(config.ConnectionString);
        Assert.Null(config.Alias);
        Assert.Null(config.Database);
    }

    [Fact]
    public void EnsureMigrated_WithExistingCaches_DoesNotDuplicate()
    {
        var config = new RedisConfig();
        config.Caches.Add(new RedisCacheEntry { DisplayName = "Existing" });
        config.ConnectionString = "should-be-ignored";

        config.EnsureMigrated();

        Assert.Single(config.Caches);
        Assert.Equal("Existing", config.Caches[0].DisplayName);
    }

    [Fact]
    public void EnsureMigrated_WithEmptyConnectionString_DoesNotCreateCache()
    {
        var config = new RedisConfig { ConnectionString = "" };

        config.EnsureMigrated();

        Assert.Empty(config.Caches);
    }

    [Fact]
    public void EnsureMigrated_WithNoLegacyAlias_UsesDefault()
    {
        var config = new RedisConfig { ConnectionString = "localhost:6379" };

        config.EnsureMigrated();

        Assert.Equal("Default", config.Caches[0].DisplayName);
    }

    [Fact]
    public void ActiveCache_ReturnsSelectedCache()
    {
        var config = new RedisConfig();
        var c1 = new RedisCacheEntry { DisplayName = "A" };
        var c2 = new RedisCacheEntry { DisplayName = "B" };
        config.Caches.AddRange([c1, c2]);
        config.ActiveCacheId = c2.Id;

        Assert.Equal("B", config.ActiveCache?.DisplayName);
    }

    [Fact]
    public void ActiveCache_FallsBackToFirst_WhenIdInvalid()
    {
        var config = new RedisConfig();
        config.Caches.Add(new RedisCacheEntry { DisplayName = "First" });
        config.ActiveCacheId = "nonexistent";

        Assert.Equal("First", config.ActiveCache?.DisplayName);
    }

    [Fact]
    public void RedisCacheEntry_GeneratesUniqueIds()
    {
        var a = new RedisCacheEntry();
        var b = new RedisCacheEntry();

        Assert.NotEqual(a.Id, b.Id);
    }
}
