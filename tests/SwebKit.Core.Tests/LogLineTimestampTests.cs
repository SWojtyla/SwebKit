using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class LogLineTimestampTests
{
    [Fact]
    public void Split_KubernetesRfc3339NanoPrefix_SeparatesTimestampFromMessage()
    {
        var parts = LogLineTimestamp.Split("2026-09-09T10:22:30.118456789Z GET /health 200");

        Assert.Equal("GET /health 200", parts.Message);
        Assert.Equal("2026-09-09T10:22:30.118456789Z", parts.RawTimestamp);
        Assert.NotNull(parts.Timestamp);
    }

    [Fact]
    public void Split_SpaceSeparatedDemoPrefix_SeparatesTimestampFromMessage()
    {
        // The demo client's historical shape. Both wire shapes must parse or the frontend
        // parser is wrong against one of the two clients.
        var parts = LogLineTimestamp.Split("2026-09-09 10:22:30.118  [INF] started");

        Assert.Equal("[INF] started", parts.Message);
        Assert.NotNull(parts.Timestamp);
    }

    [Fact]
    public void Split_WithOffsetInsteadOfZ_IsRecognised()
    {
        var parts = LogLineTimestamp.Split("2026-09-09T10:22:30+02:00 message");

        Assert.Equal("message", parts.Message);
        Assert.NotNull(parts.Timestamp);
    }

    [Fact]
    public void Split_NoPrefix_ReturnsWholeLineAsMessage()
    {
        // Safe to call on output from a client that was never asked for timestamps.
        var parts = LogLineTimestamp.Split("[INF] no timestamp here");

        Assert.Equal("[INF] no timestamp here", parts.Message);
        Assert.Null(parts.Timestamp);
        Assert.Null(parts.RawTimestamp);
    }

    [Fact]
    public void Split_TimestampShapedButImpossibleValue_KeepsTheWholeLine()
    {
        // Matching the shape is not the same as being a date. Stripping this off would
        // silently discard part of the message.
        var parts = LogLineTimestamp.Split("2026-13-45T99:99:99Z something");

        Assert.Equal("2026-13-45T99:99:99Z something", parts.Message);
        Assert.Null(parts.Timestamp);
    }

    [Fact]
    public void Split_LineThatIsOnlyATimestamp_HasAnEmptyMessage()
    {
        Assert.Equal(string.Empty, LogLineTimestamp.Split("2026-09-09T10:22:30Z ").Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Split_EmptyInput_IsHandled(string? line)
    {
        var parts = LogLineTimestamp.Split(line);

        Assert.Equal(string.Empty, parts.Message);
        Assert.Null(parts.Timestamp);
    }

    [Fact]
    public void MatchesFilter_MatchesTheMessage()
    {
        Assert.True(LogLineTimestamp.MatchesFilter("2026-09-09T10:22:30Z GET /health", "health"));
    }

    [Fact]
    public void MatchesFilter_IgnoresTheTimestampPrefix()
    {
        // The regression this exists for: filtering on "2026" used to match every single
        // line, because the filter was applied to the raw line including its timestamp.
        Assert.False(LogLineTimestamp.MatchesFilter("2026-09-09T10:22:30Z GET /health", "2026"));
    }

    [Fact]
    public void MatchesFilter_IsCaseInsensitive()
    {
        Assert.True(LogLineTimestamp.MatchesFilter("2026-09-09T10:22:30Z GET /Health", "health"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MatchesFilter_EmptyFilter_MatchesEverything(string? filter)
    {
        Assert.True(LogLineTimestamp.MatchesFilter("anything at all", filter));
    }

    [Fact]
    public void MatchesFilter_StillWorksOnALineWithNoTimestamp()
    {
        Assert.True(LogLineTimestamp.MatchesFilter("[INF] GET /health", "health"));
        Assert.False(LogLineTimestamp.MatchesFilter("[INF] GET /health", "nope"));
    }
}
