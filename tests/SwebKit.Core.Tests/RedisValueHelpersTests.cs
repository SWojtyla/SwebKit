using SwebKit.Redis;

namespace SwebKit.Core.Tests;

public class RedisValueHelpersTests
{
    [Fact]
    public void MaskConnectionString_MasksPasswordSegment()
    {
        var value = "localhost:6379,password=super-secret,ssl=true";

        var masked = RedisValueHelpers.MaskConnectionString(value);

        Assert.Equal("localhost:6379,password=***,ssl=true", masked);
    }

    [Fact]
    public void TruncateValue_WhenOverLimit_AppendsMarker()
    {
        var value = new string('a', 12);

        var result = RedisValueHelpers.TruncateValue(value, maxLength: 10);

        Assert.Equal("aaaaaaaaaa\n... (truncated)", result);
    }

    [Fact]
    public void FormatJsonIfValid_FormatsJsonWithIndentation()
    {
        var json = "{\"id\":1,\"name\":\"Alice\"}";

        var result = RedisValueHelpers.FormatJsonIfValid(json);

        Assert.Contains(Environment.NewLine, result);
        Assert.Contains("\"id\": 1", result);
    }

    [Fact]
    public void TypeToBadgeClass_ReturnsKnownAndFallbackValues()
    {
        Assert.Equal("t-string", RedisValueHelpers.TypeToBadgeClass("string"));
        Assert.Equal("t-unknown", RedisValueHelpers.TypeToBadgeClass("something-else"));
    }
}
