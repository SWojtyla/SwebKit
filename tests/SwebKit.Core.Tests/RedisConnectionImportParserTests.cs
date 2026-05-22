using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class RedisConnectionImportParserTests
{
    [Fact]
    public void Parse_ArrayImport_BuildsRedisCacheEntriesAndDominantSeparator()
    {
        var result = RedisConnectionImportParser.Parse("""
            [
              {
                "host": "redis-dev.redis.cache.windows.net",
                "port": "6380",
                "auth": "secret-a",
                "username": "",
                "connectionName": "Dev Cache",
                "separator": "-"
              },
              {
                "host": "redis-tst.redis.cache.windows.net",
                "port": "6380",
                "auth": "secret-b",
                "username": "",
                "name": "Test Cache",
                "separator": "_"
              },
              {
                "host": "redis-prd.redis.cache.windows.net",
                "port": "6380",
                "auth": "secret-c",
                "separator": "-"
              }
            ]
            """);

        Assert.Equal(3, result.Caches.Count);
        Assert.Equal("-", result.SuggestedSeparator);
        Assert.True(result.HasMixedSeparators);
        Assert.Equal("Dev Cache", result.Caches[0].DisplayName);
        Assert.Contains("redis-dev.redis.cache.windows.net:6380", result.Caches[0].ConnectionString, StringComparison.Ordinal);
        Assert.Contains("password=secret-a", result.Caches[0].ConnectionString, StringComparison.Ordinal);
        Assert.Contains("ssl=True", result.Caches[0].ConnectionString, StringComparison.Ordinal);
        Assert.Contains("abortConnect=False", result.Caches[0].ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WrappedObjectImport_UsesFallbackDisplayName()
    {
        var result = RedisConnectionImportParser.Parse("""
            {
              "connections": [
                {
                  "host": "redis-dev.redis.cache.windows.net",
                  "port": "6379",
                  "auth": "secret-a",
                  "username": "app-user"
                }
              ]
            }
            """);

        var cache = Assert.Single(result.Caches);
        Assert.Equal("redis-dev.redis.cache.windows.net:6379", cache.DisplayName);
        Assert.Contains("user=app-user", cache.ConnectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("ssl=True", cache.ConnectionString, StringComparison.Ordinal);
        Assert.False(result.HasMixedSeparators);
    }
}