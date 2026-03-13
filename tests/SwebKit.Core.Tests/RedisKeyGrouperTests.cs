using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class RedisKeyGrouperTests
{
    [Fact]
    public void BuildNamespaceTree_GroupsByColon()
    {
        var keys = new[] { "user:1001", "user:1002", "session:abc", "cache:products" };

        var tree = RedisKeyGrouper.BuildNamespaceTree(keys, ":");

        Assert.Equal(3, tree.Count);
        var userNode = Assert.Single(tree, n => n.Name == "user");
        Assert.Equal(2, userNode.Children.Count);
        Assert.Equal(2, userNode.KeyCount);
    }

    [Fact]
    public void BuildNamespaceTree_HandlesDeepNesting()
    {
        var keys = new[] { "a:b:c:d" };

        var tree = RedisKeyGrouper.BuildNamespaceTree(keys, ":");

        Assert.Single(tree);
        Assert.Equal("a", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("b", tree[0].Children[0].Name);
        Assert.Single(tree[0].Children[0].Children);
        Assert.Equal("c", tree[0].Children[0].Children[0].Name);
    }

    [Fact]
    public void BuildNamespaceTree_SortsAlphabetically()
    {
        var keys = new[] { "z:1", "a:1", "m:1" };

        var tree = RedisKeyGrouper.BuildNamespaceTree(keys, ":");

        Assert.Equal("a", tree[0].Name);
        Assert.Equal("m", tree[1].Name);
        Assert.Equal("z", tree[2].Name);
    }

    [Fact]
    public void BuildNamespaceTree_EmptySeparator_ReturnsEmpty()
    {
        var tree = RedisKeyGrouper.BuildNamespaceTree(["key"], "");

        Assert.Empty(tree);
    }

    [Fact]
    public void BuildNamespaceTree_CustomSeparator()
    {
        var keys = new[] { "tenant|order|1", "tenant|order|2", "tenant|user|1" };

        var tree = RedisKeyGrouper.BuildNamespaceTree(keys, "|");

        Assert.Single(tree);
        Assert.Equal("tenant", tree[0].Name);
        Assert.Equal(2, tree[0].Children.Count);
    }

    [Fact]
    public void BuildNamespaceTree_SetsFullPrefix()
    {
        var keys = new[] { "user:profile:1001" };

        var tree = RedisKeyGrouper.BuildNamespaceTree(keys, ":");

        Assert.Equal("user", tree[0].FullPrefix);
        Assert.Equal("user:profile", tree[0].Children[0].FullPrefix);
        Assert.Equal("user:profile:1001", tree[0].Children[0].Children[0].FullPrefix);
    }

    [Fact]
    public void ComputePrefixMemory_GroupsByTopPrefix()
    {
        var infos = new List<RedisKeyInfo>
        {
            new() { Key = "user:1001", MemoryBytes = 100 },
            new() { Key = "user:1002", MemoryBytes = 200 },
            new() { Key = "session:abc", MemoryBytes = 50 },
        };

        var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, ":");

        Assert.Equal(2, buckets.Count);
        var userBucket = Assert.Single(buckets, b => b.Prefix == "user");
        Assert.Equal(2, userBucket.KeyCount);
        Assert.Equal(300, userBucket.TotalBytes);
    }

    [Fact]
    public void ComputePrefixMemory_CalculatesPercentages()
    {
        var infos = new List<RedisKeyInfo>
        {
            new() { Key = "a:1", MemoryBytes = 75 },
            new() { Key = "b:1", MemoryBytes = 25 },
        };

        var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, ":");

        var aBucket = Assert.Single(buckets, b => b.Prefix == "a");
        Assert.Equal(75.0, aBucket.Percentage);
        var bBucket = Assert.Single(buckets, b => b.Prefix == "b");
        Assert.Equal(25.0, bBucket.Percentage);
    }

    [Fact]
    public void ComputePrefixMemory_OrdersByTotalBytesDescending()
    {
        var infos = new List<RedisKeyInfo>
        {
            new() { Key = "small:1", MemoryBytes = 10 },
            new() { Key = "large:1", MemoryBytes = 1000 },
            new() { Key = "medium:1", MemoryBytes = 100 },
        };

        var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, ":");

        Assert.Equal("large", buckets[0].Prefix);
        Assert.Equal("medium", buckets[1].Prefix);
        Assert.Equal("small", buckets[2].Prefix);
    }

    [Fact]
    public void ComputePrefixMemory_HandlesNullMemory()
    {
        var infos = new List<RedisKeyInfo>
        {
            new() { Key = "user:1", MemoryBytes = null },
        };

        var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, ":");

        Assert.Single(buckets);
        Assert.Equal(0, buckets[0].TotalBytes);
    }

    [Fact]
    public void ComputePrefixMemory_EmptyList_ReturnsEmpty()
    {
        var buckets = RedisKeyGrouper.ComputePrefixMemory([], ":");

        Assert.Empty(buckets);
    }

    [Fact]
    public void ComputePrefixMemory_KeysWithoutSeparator_GroupByFullKey()
    {
        var infos = new List<RedisKeyInfo>
        {
            new() { Key = "standalone", MemoryBytes = 42 },
        };

        var buckets = RedisKeyGrouper.ComputePrefixMemory(infos, ":");

        Assert.Single(buckets);
        Assert.Equal("standalone", buckets[0].Prefix);
    }
}
