using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Core.Tests.Fakes;

namespace SwebKit.Core.Tests;

public class RedisCredentialMigrationTests
{
    [Fact]
    public void MigrateCaches_PlaintextEntry_MovesSecretToStoreAndClearsField()
    {
        var store = new FakeCredentialStore();
        var config = new RedisConfig
        {
            Caches =
            [
                new RedisCacheEntry { Id = "a", ConnectionString = "host:6379,password=pw" },
            ],
        };

        var changed = RedisCredentialMigration.MigrateCaches(config, store);

        Assert.True(changed);
        var entry = config.Caches[0];
        Assert.Empty(entry.ConnectionString);
        Assert.StartsWith("sw-secret:redis:a:", entry.CredentialKey);
        Assert.Equal("host:6379,password=pw", store.Get(entry.CredentialKey));
    }

    [Fact]
    public void MigrateCaches_LegacyTopLevelConnectionString_MigratesTwiceRemoved()
    {
        // Pre-multi-cache profiles carried the connection string on RedisConfig itself —
        // EnsureMigrated folds it into an entry, and that entry's secret then moves to the store.
        var store = new FakeCredentialStore();
        var config = new RedisConfig { ConnectionString = "legacy:6379" };

        Assert.True(RedisCredentialMigration.MigrateCaches(config, store));

        var entry = Assert.Single(config.Caches);
        Assert.Null(config.ConnectionString);
        Assert.Empty(entry.ConnectionString);
        Assert.Equal("legacy:6379", store.Get(entry.CredentialKey));
    }

    [Fact]
    public void MigrateCaches_SkipsAadAndAlreadyMigrated()
    {
        var store = new FakeCredentialStore();
        var config = new RedisConfig
        {
            Caches =
            [
                new RedisCacheEntry { Id = "aad", UseAad = true, CacheName = "cache", ConnectionString = "ignored:6379" },
                new RedisCacheEntry { Id = "done", CredentialKey = "sw-secret:redis:done:abc" },
            ],
        };

        Assert.False(RedisCredentialMigration.MigrateCaches(config, store));
        Assert.Empty(store.ListKeys());
    }

    [Fact]
    public void ResolveConnectionString_PrefersStore_FallsBackToInline()
    {
        var store = new FakeCredentialStore();
        store.Save("key-1", "from-store:6379");

        Assert.Equal("from-store:6379",
            RedisCredentialMigration.ResolveConnectionString(
                new RedisCacheEntry { CredentialKey = "key-1", ConnectionString = "inline:6379" }, store));

        // Store miss on a set key → inline still works (unmigrated read path, store unavailable).
        Assert.Equal("inline:6379",
            RedisCredentialMigration.ResolveConnectionString(
                new RedisCacheEntry { CredentialKey = "missing-key", ConnectionString = "inline:6379" }, store));

        Assert.Equal("inline:6379",
            RedisCredentialMigration.ResolveConnectionString(
                new RedisCacheEntry { ConnectionString = "inline:6379" }, store));
    }

    [Fact]
    public void DeleteOrphanedKeys_RemovesRotatedAndDeletedKeys_KeepsLiveOnes()
    {
        var store = new FakeCredentialStore();
        store.Save("kept", "a");
        store.Save("rotated", "b");
        store.Save("removed", "c");

        var before = new List<RedisCacheEntry>
        {
            new() { Id = "x", CredentialKey = "kept" },
            new() { Id = "y", CredentialKey = "rotated" },
            new() { Id = "z", CredentialKey = "removed" },
        };
        var after = new List<RedisCacheEntry>
        {
            new() { Id = "x", CredentialKey = "kept" },
            new() { Id = "y", CredentialKey = "new-key" },
        };

        RedisCredentialMigration.DeleteOrphanedKeys(before, after, store);

        Assert.Equal("a", store.Get("kept"));
        Assert.Null(store.Get("rotated"));
        Assert.Null(store.Get("removed"));
    }
}
