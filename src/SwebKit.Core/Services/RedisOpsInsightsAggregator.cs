using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Deterministic in-memory service that correlates slowlog frequency, LFU scores, and
/// idle/memory metadata into hot-key signals. Does NOT issue Redis calls.
/// </summary>
public sealed class RedisOpsInsightsAggregator
{
    private const long HighMemoryThresholdBytes = 64 * 1024;
    private const long LowIdleThresholdSeconds = 10;

    /// <summary>
    /// Produces hot-key signals by correlating slowlog command frequency with
    /// already-loaded key metadata. Does NOT issue new Redis calls.
    /// </summary>
    public RedisHotKeySummary BuildHotKeySignals(
        RedisSlowLogSummary slowLog,
        IReadOnlyList<RedisKeyInfo> loadedKeys)
    {
        ArgumentNullException.ThrowIfNull(slowLog);
        ArgumentNullException.ThrowIfNull(loadedKeys);

        var isPartial = slowLog.Capability != RedisInsightCapability.Loaded;
        var partialReason = isPartial ? BuildPartialReason(slowLog.Capability) : null;

        var slowlogFrequency = BuildSlowlogFrequencyMap(slowLog.Entries);

        var keyMap = loadedKeys
            .Where(k => !string.IsNullOrEmpty(k.Key))
            .ToDictionary(k => k.Key, StringComparer.Ordinal);

        // One signal entry per key; highest-confidence source wins the SignalSource field.
        var signals = new Dictionary<string, RedisHotKeySignal>(StringComparer.Ordinal);

        // 1. Slowlog frequency — highest confidence
        foreach (var (key, freq) in slowlogFrequency)
        {
            if (!keyMap.TryGetValue(key, out var keyInfo))
                continue;

            signals[key] = new RedisHotKeySignal(
                key,
                "Slowlog frequency",
                $"Key '{key}' appears {freq} time(s) in the slow log.",
                freq,
                keyInfo.IdleSeconds.HasValue ? (double)keyInfo.IdleSeconds.Value : null,
                keyInfo.MemoryBytes);
        }

        // 2. LFU frequency and low-idle-time signals
        foreach (var keyInfo in loadedKeys)
        {
            if (string.IsNullOrEmpty(keyInfo.Key))
                continue;

            var key = keyInfo.Key;

            if (keyInfo.Frequency.HasValue && keyInfo.Frequency.Value > 0)
            {
                if (signals.TryGetValue(key, out var existing))
                {
                    // Merge into the existing (higher-priority) slowlog signal
                    signals[key] = existing with
                    {
                        Explanation = existing.Explanation + $" LFU frequency score: {keyInfo.Frequency.Value}."
                    };
                }
                else
                {
                    signals[key] = new RedisHotKeySignal(
                        key,
                        "LFU frequency (OBJECT FREQ)",
                        $"Key '{key}' has LFU frequency score {keyInfo.Frequency.Value}.",
                        (double)keyInfo.Frequency.Value,
                        keyInfo.IdleSeconds.HasValue ? (double)keyInfo.IdleSeconds.Value : null,
                        keyInfo.MemoryBytes);
                }
            }

            if (keyInfo.IdleSeconds.HasValue &&
                keyInfo.IdleSeconds.Value < LowIdleThresholdSeconds &&
                keyInfo.MemoryBytes.HasValue &&
                keyInfo.MemoryBytes.Value >= HighMemoryThresholdBytes)
            {
                if (!signals.ContainsKey(key))
                {
                    signals[key] = new RedisHotKeySignal(
                        key,
                        "Low idle time",
                        $"Key '{key}' has only {keyInfo.IdleSeconds.Value}s idle time with {keyInfo.MemoryBytes.Value:N0} bytes in memory.",
                        null,
                        (double)keyInfo.IdleSeconds.Value,
                        keyInfo.MemoryBytes);
                }
            }
        }

        return new RedisHotKeySummary(
            [.. signals.Values],
            isPartial,
            partialReason);
    }

    private static Dictionary<string, double> BuildSlowlogFrequencyMap(IReadOnlyList<RedisSlowLogEntryInfo> entries)
    {
        var frequency = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var args = entry.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0)
                continue;

            var potentialKey = args[0];
            if (string.IsNullOrEmpty(potentialKey))
                continue;

            frequency[potentialKey] = frequency.GetValueOrDefault(potentialKey) + 1;
        }

        return frequency;
    }

    private static string BuildPartialReason(RedisInsightCapability capability) => capability switch
    {
        RedisInsightCapability.Unsupported =>
            "SLOWLOG is not supported by this Redis target. Hot-key signals are derived from key metadata only.",
        RedisInsightCapability.PermissionLimited =>
            "Insufficient permissions to read SLOWLOG. Hot-key signals are derived from key metadata only.",
        RedisInsightCapability.Failed =>
            "SLOWLOG retrieval failed. Hot-key signals are derived from key metadata only.",
        RedisInsightCapability.Partial =>
            "SLOWLOG data is partial. Hot-key signals may not reflect the full workload.",
        _ =>
            "Slowlog data is unavailable. Hot-key signals are derived from key metadata only."
    };
}
