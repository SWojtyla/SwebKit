using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class DemoRedisClientTests
{
    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue()
    {
        using var client = new DemoRedisClient();

        var ok = await client.TestConnectionAsync();

        Assert.True(ok);
    }

    [Fact]
    public async Task ScanKeysAsync_WithPattern_FiltersKeys()
    {
        using var client = new DemoRedisClient();

        var result = await client.ScanKeysAsync("user:*");

        Assert.NotEmpty(result.Keys);
        Assert.All(result.Keys, key => Assert.StartsWith("user:", key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanKeysAsync_WithSmallPageSize_Paginates()
    {
        using var client = new DemoRedisClient();

        var first = await client.ScanKeysAsync("*", cursor: 0, pageSize: 2);
        var second = await client.ScanKeysAsync("*", cursor: first.Cursor, pageSize: 2);

        Assert.Equal(2, first.Keys.Count);
        Assert.NotEqual(0, first.Cursor);
        Assert.Equal(2, second.Keys.Count);
    }

    [Fact]
    public async Task GetKeyInfoAsync_ForStringKey_ReturnsExpectedType()
    {
        using var client = new DemoRedisClient();

        var info = await client.GetKeyInfoAsync("user:1001");

        Assert.Equal("string", info.Type);
        Assert.NotNull(info.Ttl);
        Assert.True(info.MemoryBytes > 0);
    }

    [Fact]
    public async Task GetHashFieldsAsync_ForHashKey_ReturnsFields()
    {
        using var client = new DemoRedisClient();

        var fields = await client.GetHashFieldsAsync("session:abc123");

        Assert.NotEmpty(fields);
        Assert.Contains(fields, f => f.Field == "user_id");
    }

    [Fact]
    public async Task GetListItemsAsync_ForListKey_ReturnsRange()
    {
        using var client = new DemoRedisClient();

        var items = await client.GetListItemsAsync("cache:products", 0, 2);

        Assert.Equal(3, items.Count);
        Assert.Equal("product-1", items[0]);
    }

    [Fact]
    public async Task GetSetMembersAsync_ForSetKey_ReturnsMembers()
    {
        using var client = new DemoRedisClient();

        var members = await client.GetSetMembersAsync("cache:categories");

        Assert.NotEmpty(members);
        Assert.Contains("books", members);
    }

    [Fact]
    public async Task GetSortedSetMembersAsync_ForZSetKey_ReturnsOrderedScores()
    {
        using var client = new DemoRedisClient();

        var entries = await client.GetSortedSetMembersAsync("leaderboard:daily");

        Assert.NotEmpty(entries);
        Assert.Equal("alice", entries[0].Member);
        Assert.True(entries[0].Score >= entries[1].Score);
    }

    [Fact]
    public async Task SetKeyValueAsync_ThenReadBack_ReturnsUpdatedValue()
    {
        using var client = new DemoRedisClient();

        await client.SetKeyValueAsync("test:key", "hello", TimeSpan.FromMinutes(2));
        var value = await client.GetKeyValueAsync("test:key");
        var ttl = await client.GetTtlAsync("test:key");

        Assert.Equal("hello", value);
        Assert.NotNull(ttl);
    }

    [Fact]
    public async Task SetHashFieldAsync_ThenReadBack_ReturnsUpdatedField()
    {
        using var client = new DemoRedisClient();

        await client.SetHashFieldAsync("config:feature-flags", "new_flag", "true");
        var fields = await client.GetHashFieldsAsync("config:feature-flags");

        var item = Assert.Single(fields, f => f.Field == "new_flag");
        Assert.Equal("true", item.Value);
    }

    [Fact]
    public async Task DeleteKeysAsync_RemovesKeys()
    {
        using var client = new DemoRedisClient();

        await client.DeleteKeysAsync(["user:1001"]);
        var info = await client.GetKeyInfoAsync("user:1001");

        Assert.Equal("none", info.Type);
    }

    [Fact]
    public async Task SetTtlAsync_AndRemoveTtlAsync_UpdatesExpiryState()
    {
        using var client = new DemoRedisClient();

        await client.SetKeyValueAsync("ttl:key", "value");
        await client.SetTtlAsync("ttl:key", TimeSpan.FromSeconds(90));
        var withTtl = await client.GetTtlAsync("ttl:key");

        await client.RemoveTtlAsync("ttl:key");
        var noTtl = await client.GetTtlAsync("ttl:key");

        Assert.NotNull(withTtl);
        Assert.Null(noTtl);
    }

    [Fact]
    public async Task FlushDatabaseAsync_EmptiesKeyspace()
    {
        using var client = new DemoRedisClient();

        await client.FlushDatabaseAsync();
        var scan = await client.ScanKeysAsync("*");

        Assert.True(scan.IsComplete);
        Assert.Empty(scan.Keys);
    }

    [Fact]
    public async Task GetServerInfoAsync_ReturnsDatabaseStats()
    {
        using var client = new DemoRedisClient();

        var info = await client.GetServerInfoAsync();

        Assert.False(string.IsNullOrWhiteSpace(info.RedisVersion));
        Assert.NotEmpty(info.Databases);
        Assert.True(info.Databases[0].Keys > 0);
    }
}
