using SwebKit.Redis;
using StackExchange.Redis;

namespace SwebKit.Core.Tests;

public sealed class RedisScanResponseParserTests
{
    [Fact]
    public void Parse_PreservesTheSourceCursorForContinuation()
    {
        var response = RedisResult.Create([
            RedisResult.Create((RedisKey)"42"),
            RedisResult.Create([
                RedisResult.Create((RedisKey)"member-a"),
                RedisResult.Create((RedisKey)"member-b")
            ])
        ]);

        var page = RedisScanResponseParser.Parse(response);

        Assert.Equal(42, page.Cursor);
        Assert.Equal(["member-a", "member-b"], page.Values);
        Assert.False(page.IsComplete);
    }

    [Fact]
    public void Parse_WhenCursorIsZero_MarksThePageAsComplete()
    {
        var response = RedisResult.Create([
            RedisResult.Create((RedisKey)"0"),
            RedisResult.Create([
                RedisResult.Create((RedisKey)"member-a")
            ])
        ]);

        var page = RedisScanResponseParser.Parse(response);

        Assert.Equal(0, page.Cursor);
        Assert.True(page.IsComplete);
    }
}