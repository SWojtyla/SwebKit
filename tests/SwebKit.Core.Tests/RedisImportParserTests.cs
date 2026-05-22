using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class RedisImportParserTests
{
    [Fact]
    public void Parse_ObjectMap_InfersRedisTypesFromJsonShape()
    {
        var entries = RedisImportParser.Parse("""
            {
              "plain:text": "hello",
              "feature:flags": {
                "alpha": true,
                "beta": false
              },
              "queue:recent": ["one", "two"],
              "leaders": [
                { "member": "alice", "score": 11 },
                { "member": "bob", "score": 7 }
              ]
            }
            """);

        Assert.Collection(entries,
            first =>
            {
                Assert.Equal("plain:text", first.Key);
                Assert.Equal("string", first.Type);
                Assert.Equal("hello", first.StringValue);
            },
            second =>
            {
                Assert.Equal("feature:flags", second.Key);
                Assert.Equal("hash", second.Type);
                Assert.Equal("true", second.HashFields["alpha"]);
            },
            third =>
            {
                Assert.Equal("queue:recent", third.Key);
                Assert.Equal("list", third.Type);
                Assert.Equal(["one", "two"], third.ListItems);
            },
            fourth =>
            {
                Assert.Equal("leaders", fourth.Key);
                Assert.Equal("zset", fourth.Type);
                Assert.Equal(2, fourth.SortedSetMembers.Count);
                Assert.Equal("alice", fourth.SortedSetMembers[0].Member);
            });
    }

    [Fact]
    public void Parse_EntryWrapper_SupportsTtlAndExplicitSetType()
    {
        var entries = RedisImportParser.Parse("""
            {
              "entries": [
                {
                  "key": "cache:tags",
                  "type": "set",
                  "ttlSeconds": 90,
                  "value": ["red", "blue"]
                }
              ]
            }
            """);

        var entry = Assert.Single(entries);
        Assert.Equal("cache:tags", entry.Key);
        Assert.Equal("set", entry.Type);
        Assert.Equal(["red", "blue"], entry.SetMembers);
        Assert.Equal(TimeSpan.FromSeconds(90), entry.Ttl);
    }
}