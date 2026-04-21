using SwebKit.App.Components.Aks;

namespace SwebKit.App.Tests;

public sealed class CronNextRunTests
{
    // ── TryCalculate ──────────────────────────────────────────────────────────

    [Fact]
    public void TryCalculate_HourlyCron_NextIsAtHourBoundary()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 30, 0, TimeSpan.Zero);

        var result = CronNextRun.TryCalculate("0 * * * *", from, out var next);

        Assert.True(result);
        Assert.Equal(new DateTimeOffset(2026, 4, 21, 11, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void TryCalculate_Every5Minutes_NextIsWithin5Minutes()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 23, 0, TimeSpan.Zero);

        var result = CronNextRun.TryCalculate("*/5 * * * *", from, out var next);

        Assert.True(result);
        Assert.True((next - from).TotalMinutes <= 5);
        Assert.Equal(0, next.Minute % 5);
    }

    [Fact]
    public void TryCalculate_AtDailyPrefix_ReturnsFalse()
    {
        var result = CronNextRun.TryCalculate("@daily", DateTimeOffset.UtcNow, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryCalculate_WeekdayNineAm_NextIsWeekdayAt09()
    {
        // Tuesday 2026-04-21 08:00 UTC — next weekday 9am is same day
        var from = new DateTimeOffset(2026, 4, 21, 8, 0, 0, TimeSpan.Zero);

        var result = CronNextRun.TryCalculate("0 9 * * 1-5", from, out var next);

        Assert.True(result);
        Assert.Equal(9, next.Hour);
        Assert.Equal(0, next.Minute);
        var dow = (int)next.DayOfWeek;
        Assert.InRange(dow, 1, 5); // Mon–Fri
    }

    [Fact]
    public void TryCalculate_NullSchedule_ReturnsFalse()
    {
        Assert.False(CronNextRun.TryCalculate(null, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void TryCalculate_EmptySchedule_ReturnsFalse()
    {
        Assert.False(CronNextRun.TryCalculate("", DateTimeOffset.UtcNow, out _));
    }

    // ── FormatCountdown ───────────────────────────────────────────────────────

    [Fact]
    public void FormatCountdown_30SecondsAway_ReturnsSubMinuteFormat()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var next = from.AddSeconds(30);

        var text = CronNextRun.FormatCountdown(next, from);

        // Implementation truncates to whole minutes — 30s → "in 0m (UTC)"
        Assert.Equal("in 0m (UTC)", text);
    }

    [Fact]
    public void FormatCountdown_90SecondsAway_Returns1MinuteFormat()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var next = from.AddSeconds(90);

        var text = CronNextRun.FormatCountdown(next, from);

        // 1.5 min truncated to 1 → "in 1m (UTC)"
        Assert.Equal("in 1m (UTC)", text);
    }

    [Fact]
    public void FormatCountdown_2HoursAway_ReturnsHourFormat()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var next = from.AddHours(2).AddMinutes(15);

        var text = CronNextRun.FormatCountdown(next, from);

        Assert.Equal("in 2h 15m (UTC)", text);
    }

    [Fact]
    public void FormatCountdown_3DaysAway_ReturnsDayFormat()
    {
        var from = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var next = from.AddDays(3).AddHours(4);

        var text = CronNextRun.FormatCountdown(next, from);

        Assert.Equal("in 3d 4h (UTC)", text);
    }
}
