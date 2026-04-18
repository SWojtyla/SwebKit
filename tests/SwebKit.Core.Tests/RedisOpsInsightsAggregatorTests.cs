using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class RedisOpsInsightsAggregatorTests
{
    private readonly RedisOpsInsightsAggregator _aggregator = new();

    [Fact]
    public void BuildHotKeySignals_EmptySlowlogAndNoMatchingKeys_ReturnsEmptySignalsNotPartial()
    {
        var slowLog = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Loaded);
        var loadedKeys = new List<RedisKeyInfo>();

        var result = _aggregator.BuildHotKeySignals(slowLog, loadedKeys);

        Assert.Empty(result.Signals);
        Assert.False(result.IsPartial);
        Assert.Null(result.PartialReason);
    }

    [Fact]
    public void BuildHotKeySignals_SlowlogEntryMatchingLoadedKey_EmitsSlowlogSignal()
    {
        var entries = new List<RedisSlowLogEntryInfo>
        {
            new(1, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(20), "HGETALL", "user:1001", null),
            new(2, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(15), "HGETALL", "user:1001", null),
        };
        var slowLog = new RedisSlowLogSummary(entries, false, 128, RedisInsightCapability.Loaded);
        var loadedKeys = new List<RedisKeyInfo>
        {
            new() { Key = "user:1001", Type = "hash", MemoryBytes = 2048 }
        };

        var result = _aggregator.BuildHotKeySignals(slowLog, loadedKeys);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("user:1001", signal.Key);
        Assert.Equal("Slowlog frequency", signal.SignalSource);
        Assert.Equal(2.0, signal.FrequencyScore);
    }

    [Fact]
    public void BuildHotKeySignals_LoadedKeyWithNonZeroFrequency_EmitsLfuSignal()
    {
        var slowLog = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Loaded);
        var loadedKeys = new List<RedisKeyInfo>
        {
            new() { Key = "rate-limit:api", Type = "string", MemoryBytes = 512, Frequency = 42 }
        };

        var result = _aggregator.BuildHotKeySignals(slowLog, loadedKeys);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("rate-limit:api", signal.Key);
        Assert.Equal("LFU frequency (OBJECT FREQ)", signal.SignalSource);
        Assert.Equal(42.0, signal.FrequencyScore);
    }

    [Fact]
    public void BuildHotKeySignals_UnsupportedSlowlog_ReturnsIsPartialTrueWithReason()
    {
        var slowLog = new RedisSlowLogSummary([], false, 128, RedisInsightCapability.Unsupported);
        var loadedKeys = new List<RedisKeyInfo>
        {
            new() { Key = "some:key", Type = "string" }
        };

        var result = _aggregator.BuildHotKeySignals(slowLog, loadedKeys);

        Assert.True(result.IsPartial);
        Assert.NotNull(result.PartialReason);
        Assert.NotEmpty(result.PartialReason);
    }

    [Fact]
    public void BuildHotKeySignals_KeyMatchingBothSlowlogAndLfu_EmitsSingleMergedSignal()
    {
        var entries = new List<RedisSlowLogEntryInfo>
        {
            new(1, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(25), "GET", "hot:key", null),
        };
        var slowLog = new RedisSlowLogSummary(entries, false, 128, RedisInsightCapability.Loaded);
        var loadedKeys = new List<RedisKeyInfo>
        {
            new() { Key = "hot:key", Type = "string", MemoryBytes = 1024, Frequency = 55 }
        };

        var result = _aggregator.BuildHotKeySignals(slowLog, loadedKeys);

        var keySignals = result.Signals.Where(s => s.Key == "hot:key").ToList();
        Assert.Single(keySignals);
        Assert.Equal("Slowlog frequency", keySignals[0].SignalSource);
        Assert.Contains("55", keySignals[0].Explanation);
    }
}
