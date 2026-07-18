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

    [Theory]
    [InlineData("hash", "t-hash")]
    [InlineData("list", "t-list")]
    [InlineData("set", "t-set")]
    [InlineData("zset", "t-zset")]
    [InlineData("stream", "t-stream")]
    [InlineData(null, "t-unknown")]
    public void TypeToBadgeClass_MapsEachKnownType(string? type, string expected)
    {
        Assert.Equal(expected, RedisValueHelpers.TypeToBadgeClass(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskConnectionString_BlankInput_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, RedisValueHelpers.MaskConnectionString(input));
    }

    [Fact]
    public void MaskConnectionString_WithoutPassword_ReturnsUnchanged()
    {
        var value = "localhost:6379,ssl=true";
        Assert.Equal(value, RedisValueHelpers.MaskConnectionString(value));
    }

    [Fact]
    public void MaskConnectionString_IsCaseInsensitiveForPasswordKey()
    {
        var masked = RedisValueHelpers.MaskConnectionString("host:6379,PassWord=secret");
        Assert.Equal("host:6379,password=***", masked);
    }

    [Fact]
    public void TruncateValue_UnderLimit_ReturnsOriginal()
    {
        Assert.Equal("short", RedisValueHelpers.TruncateValue("short", maxLength: 100));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TruncateValue_NullOrEmpty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, RedisValueHelpers.TruncateValue(input));
    }

    [Fact]
    public void TruncateValue_NonPositiveMaxLength_ReturnsOriginal()
    {
        Assert.Equal("abc", RedisValueHelpers.TruncateValue("abc", maxLength: 0));
    }

    [Fact]
    public void FormatJsonIfValid_NonJsonText_ReturnsUnchanged()
    {
        Assert.Equal("plain text", RedisValueHelpers.FormatJsonIfValid("plain text"));
    }

    [Fact]
    public void FormatJsonIfValid_InvalidJson_ReturnsOriginal()
    {
        var broken = "{ not valid json";
        Assert.Equal(broken, RedisValueHelpers.FormatJsonIfValid(broken));
    }

    [Fact]
    public void FormatJsonIfValid_FormatsJsonArray()
    {
        var result = RedisValueHelpers.FormatJsonIfValid("[1,2,3]");
        Assert.Contains(Environment.NewLine, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FormatJsonIfValid_BlankInput_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, RedisValueHelpers.FormatJsonIfValid(input));
    }

    [Fact]
    public void IsBinaryContent_PlainText_ReturnsFalse()
    {
        Assert.False(RedisValueHelpers.IsBinaryContent("hello world\nsecond line\t tabbed"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsBinaryContent_NullOrEmpty_ReturnsFalse(string? input)
    {
        Assert.False(RedisValueHelpers.IsBinaryContent(input));
    }

    [Fact]
    public void IsBinaryContent_ManyControlChars_ReturnsTrue()
    {
        var value = new string('\0', 20) + new string('a', 20);
        Assert.True(RedisValueHelpers.IsBinaryContent(value));
    }
}
