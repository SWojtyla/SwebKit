using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class TtlFormatterTests
{
    // FormatHuman

    [Fact]
    public void FormatHuman_NullTtl_ReturnsNoExpiry()
    {
        Assert.Equal("No expiry", TtlFormatter.FormatHuman(null));
    }

    [Fact]
    public void FormatHuman_ZeroTtl_ReturnsExpiredMessage()
    {
        Assert.Equal("Key has no TTL / already expired", TtlFormatter.FormatHuman(TimeSpan.Zero));
    }

    [Fact]
    public void FormatHuman_NegativeTtl_ReturnsExpiredMessage()
    {
        Assert.Equal("Key has no TTL / already expired", TtlFormatter.FormatHuman(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void FormatHuman_Under60Seconds_ShowsSeconds()
    {
        Assert.Equal("45s remaining", TtlFormatter.FormatHuman(TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void FormatHuman_Exactly60Seconds_ShowsMinutesAndSeconds()
    {
        Assert.Equal("1m 0s remaining", TtlFormatter.FormatHuman(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void FormatHuman_Between1And60Minutes_ShowsMinutesAndSeconds()
    {
        // 2 min 30 s
        Assert.Equal("2m 30s remaining", TtlFormatter.FormatHuman(TimeSpan.FromSeconds(150)));
    }

    [Fact]
    public void FormatHuman_Exactly1Hour_ShowsHoursAndZeroMinutes()
    {
        Assert.Equal("1h 0m remaining", TtlFormatter.FormatHuman(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void FormatHuman_Over1Hour_ShowsHoursAndMinutes()
    {
        // 8547 s = 2 h 22 m 27 s  →  "2h 22m remaining"
        Assert.Equal("2h 22m remaining", TtlFormatter.FormatHuman(TimeSpan.FromSeconds(8547)));
    }

    // GetColor — percentage thresholds (original TTL known)

    [Theory]
    [InlineData(1000, 4000, "var(--color-success)")]   // 25 % > 20 %
    [InlineData(600, 4000, "var(--color-warning)")]    // 15 % in 5–20 %
    [InlineData(100, 4000, "var(--color-error)")]      // 2.5 % < 5 %
    public void GetColor_WithKnownOriginal_UsesPercentageThresholds(int remainingS, int originalS, string expected)
    {
        var color = TtlFormatter.GetColor(
            TimeSpan.FromSeconds(remainingS),
            TimeSpan.FromSeconds(originalS));
        Assert.Equal(expected, color);
    }

    // GetColor — absolute thresholds (original TTL unknown)

    [Theory]
    [InlineData(360, "var(--color-success)")]   // 6 min > 5 min
    [InlineData(180, "var(--color-warning)")]   // 3 min within 1–5 min
    [InlineData(30, "var(--color-error)")]      // 30 s < 1 min
    public void GetColor_WithUnknownOriginal_UsesAbsoluteThresholds(int remainingS, string expected)
    {
        var color = TtlFormatter.GetColor(TimeSpan.FromSeconds(remainingS), null);
        Assert.Equal(expected, color);
    }

    [Fact]
    public void GetColor_NullRemaining_ReturnsError()
    {
        Assert.Equal("var(--color-error)", TtlFormatter.GetColor(null, null));
    }

    [Fact]
    public void GetColor_ZeroRemaining_ReturnsError()
    {
        Assert.Equal("var(--color-error)", TtlFormatter.GetColor(TimeSpan.Zero, TimeSpan.FromSeconds(100)));
    }

    // GetBarWidthPercent

    [Fact]
    public void GetBarWidthPercent_NullRemaining_ReturnsZero()
    {
        Assert.Equal(0, TtlFormatter.GetBarWidthPercent(null, null));
    }

    [Fact]
    public void GetBarWidthPercent_ZeroRemaining_ReturnsZero()
    {
        Assert.Equal(0, TtlFormatter.GetBarWidthPercent(TimeSpan.Zero, TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void GetBarWidthPercent_HalfOfKnownOriginal_Returns50Percent()
    {
        var pct = TtlFormatter.GetBarWidthPercent(
            TimeSpan.FromSeconds(500),
            TimeSpan.FromSeconds(1000));
        Assert.Equal(50.0, pct, precision: 5);
    }

    [Fact]
    public void GetBarWidthPercent_ExceedsOriginal_ClampedTo100()
    {
        // Defensive: remaining > original (clock skew or edge case)
        var pct = TtlFormatter.GetBarWidthPercent(
            TimeSpan.FromSeconds(1200),
            TimeSpan.FromSeconds(1000));
        Assert.Equal(100.0, pct, precision: 5);
    }

    [Fact]
    public void GetBarWidthPercent_30MinWithUnknownOriginal_Returns50Percent()
    {
        // 30 min out of 3600 s cap = 50 %
        var pct = TtlFormatter.GetBarWidthPercent(TimeSpan.FromMinutes(30), null);
        Assert.Equal(50.0, pct, precision: 5);
    }

    [Fact]
    public void GetBarWidthPercent_Over1HourWithUnknownOriginal_ClampedTo100()
    {
        var pct = TtlFormatter.GetBarWidthPercent(TimeSpan.FromHours(2), null);
        Assert.Equal(100.0, pct, precision: 5);
    }
}
