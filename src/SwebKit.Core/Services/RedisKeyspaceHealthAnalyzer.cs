using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Deterministic in-memory analyzer that turns Redis key metadata into health findings.
/// </summary>
public sealed class RedisKeyspaceHealthAnalyzer
{
    public RedisKeyspaceHealthReport Analyze(
        IReadOnlyList<RedisKeyInfo> keyInfos,
        long? estimatedKeyCount = null,
        RedisHealthScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(keyInfos);

        options ??= new RedisHealthScanOptions();
        var thresholds = options.Thresholds ?? new RedisHealthThresholds();
        var separator = string.IsNullOrEmpty(options.Separator) ? "-" : options.Separator;

        var normalizedInfos = keyInfos
            .Where(static info => !string.IsNullOrWhiteSpace(info.Key))
            .Where(static info => !string.Equals(info.Type, "none", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var findings = new List<RedisHealthFinding>();
        var keysWithHotSignal = 0;
        var keysWithoutHotSignal = 0;

        foreach (var info in normalizedInfos)
        {
            AppendNoTtlFinding(findings, info, thresholds);
            AppendOversizedFinding(findings, info, thresholds);

            if (TryBuildHotKeyFinding(info, thresholds, out var hotKeyFinding, out var hasHotSignal))
            {
                findings.Add(hotKeyFinding);
            }

            if (hasHotSignal)
            {
                keysWithHotSignal++;
            }
            else
            {
                keysWithoutHotSignal++;
            }
        }

        foreach (var bucket in RedisKeyGrouper.ComputePrefixMemory(normalizedInfos, separator))
        {
            var severity = GetPrefixSeverity(bucket, thresholds);
            if (!severity.HasValue)
            {
                continue;
            }

            findings.Add(new RedisHealthFinding
            {
                EntityType = RedisHealthEntityType.Prefix,
                RiskType = RedisHealthRiskType.HeavyPrefix,
                Severity = severity.Value,
                Target = bucket.Prefix,
                Reason = $"Prefix '{bucket.Prefix}' concentrates {bucket.KeyCount} keys and {bucket.Percentage:0.#}% of sampled memory.",
                KeyCount = bucket.KeyCount,
                MemoryBytes = bucket.TotalBytes,
                SharePercent = bucket.Percentage,
            });
        }

        if (options.IncludeSignalUnavailableFinding && keysWithHotSignal == 0 && normalizedInfos.Count > 0)
        {
            findings.Add(new RedisHealthFinding
            {
                EntityType = RedisHealthEntityType.Keyspace,
                RiskType = RedisHealthRiskType.HotKeySignalUnavailable,
                Severity = RedisHealthSeverity.Info,
                Target = "keyspace",
                Reason = "Hot-key signals are unavailable for this Redis target (OBJECT FREQ/IDLETIME not exposed).",
            });
        }

        var orderedFindings = findings
            .OrderByDescending(GetSeverityWeight)
            .ThenBy(static finding => finding.RiskType)
            .ThenBy(static finding => finding.Target, StringComparer.Ordinal)
            .ToList();

        if (options.MaxFindings > 0 && orderedFindings.Count > options.MaxFindings)
        {
            orderedFindings = orderedFindings.Take(options.MaxFindings).ToList();
        }

        var loadedKeyCount = normalizedInfos.Count;
        var normalizedEstimate = NormalizeEstimate(estimatedKeyCount);
        var coveragePercent = ComputeCoveragePercent(loadedKeyCount, normalizedEstimate);

        return new RedisKeyspaceHealthReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            LoadedKeyCount = loadedKeyCount,
            EstimatedKeyCount = normalizedEstimate,
            CoveragePercent = coveragePercent,
            IsPartialCoverage = normalizedEstimate.HasValue && loadedKeyCount < normalizedEstimate.Value,
            ConfidenceLabel = GetConfidenceLabel(loadedKeyCount, normalizedEstimate, coveragePercent),
            HotKeySignalsAvailable = keysWithHotSignal > 0,
            KeysWithHotKeySignal = keysWithHotSignal,
            KeysWithoutHotKeySignal = keysWithoutHotSignal,
            CriticalCount = orderedFindings.Count(static finding => finding.Severity == RedisHealthSeverity.Critical),
            WarningCount = orderedFindings.Count(static finding => finding.Severity == RedisHealthSeverity.Warning),
            InfoCount = orderedFindings.Count(static finding => finding.Severity == RedisHealthSeverity.Info),
            KeyFindingCount = orderedFindings.Count(static finding => finding.EntityType == RedisHealthEntityType.Key),
            PrefixFindingCount = orderedFindings.Count(static finding => finding.EntityType == RedisHealthEntityType.Prefix),
            Findings = orderedFindings,
        };
    }

    private static void AppendNoTtlFinding(List<RedisHealthFinding> findings, RedisKeyInfo info, RedisHealthThresholds thresholds)
    {
        if (info.Ttl.HasValue)
        {
            return;
        }

        var memory = info.MemoryBytes ?? 0;
        var severity = memory >= thresholds.NoTtlCriticalBytes
            ? RedisHealthSeverity.Critical
            : RedisHealthSeverity.Warning;

        findings.Add(new RedisHealthFinding
        {
            EntityType = RedisHealthEntityType.Key,
            RiskType = RedisHealthRiskType.NoTtl,
            Severity = severity,
            Target = info.Key,
            DrillKey = info.Key,
            MemoryBytes = info.MemoryBytes,
            Reason = info.MemoryBytes.HasValue
                ? $"Key has no TTL and currently uses {FormatBytes(info.MemoryBytes.Value)}."
                : "Key has no TTL and can accumulate without expiry.",
        });
    }

    private static void AppendOversizedFinding(List<RedisHealthFinding> findings, RedisKeyInfo info, RedisHealthThresholds thresholds)
    {
        if (!info.MemoryBytes.HasValue)
        {
            return;
        }

        var memory = info.MemoryBytes.Value;
        RedisHealthSeverity? severity = null;
        if (memory >= thresholds.OversizedCriticalBytes)
        {
            severity = RedisHealthSeverity.Critical;
        }
        else if (memory >= thresholds.OversizedWarningBytes)
        {
            severity = RedisHealthSeverity.Warning;
        }

        if (!severity.HasValue)
        {
            return;
        }

        findings.Add(new RedisHealthFinding
        {
            EntityType = RedisHealthEntityType.Key,
            RiskType = RedisHealthRiskType.OversizedValue,
            Severity = severity.Value,
            Target = info.Key,
            DrillKey = info.Key,
            MemoryBytes = memory,
            Reason = $"Key size is {FormatBytes(memory)}, above oversized threshold.",
        });
    }

    private static bool TryBuildHotKeyFinding(
        RedisKeyInfo info,
        RedisHealthThresholds thresholds,
        out RedisHealthFinding finding,
        out bool hasHotSignal)
    {
        finding = new RedisHealthFinding();
        hasHotSignal = info.Frequency.HasValue || info.IdleSeconds.HasValue;

        if (!hasHotSignal)
        {
            return false;
        }

        RedisHealthSeverity? severity = null;
        var reasons = new List<string>(2);

        if (info.Frequency.HasValue)
        {
            var frequency = info.Frequency.Value;
            if (frequency >= thresholds.HotKeyCriticalFrequency)
            {
                severity = RedisHealthSeverity.Critical;
            }
            else if (frequency >= thresholds.HotKeyWarningFrequency)
            {
                severity ??= RedisHealthSeverity.Warning;
            }

            reasons.Add($"freq={frequency}");
        }

        if (info.IdleSeconds.HasValue)
        {
            var idleSeconds = info.IdleSeconds.Value;
            if (idleSeconds <= thresholds.HotKeyCriticalIdleSeconds)
            {
                severity = RedisHealthSeverity.Critical;
            }
            else if (idleSeconds <= thresholds.HotKeyWarningIdleSeconds)
            {
                severity ??= RedisHealthSeverity.Warning;
            }

            reasons.Add($"idle={idleSeconds}s");
        }

        if (!severity.HasValue)
        {
            return false;
        }

        finding = new RedisHealthFinding
        {
            EntityType = RedisHealthEntityType.Key,
            RiskType = RedisHealthRiskType.PossibleHotKey,
            Severity = severity.Value,
            Target = info.Key,
            DrillKey = info.Key,
            MemoryBytes = info.MemoryBytes,
            Frequency = info.Frequency,
            IdleSeconds = info.IdleSeconds,
            Reason = $"Possible hot key signal detected ({string.Join(", ", reasons)}).",
        };

        return true;
    }

    private static RedisHealthSeverity? GetPrefixSeverity(PrefixMemoryBucket bucket, RedisHealthThresholds thresholds)
    {
        if (bucket.Percentage >= thresholds.HeavyPrefixCriticalPercent || bucket.KeyCount >= thresholds.HeavyPrefixCriticalKeyCount)
        {
            return RedisHealthSeverity.Critical;
        }

        if (bucket.Percentage >= thresholds.HeavyPrefixWarningPercent || bucket.KeyCount >= thresholds.HeavyPrefixWarningKeyCount)
        {
            return RedisHealthSeverity.Warning;
        }

        return null;
    }

    private static int GetSeverityWeight(RedisHealthFinding finding) => finding.Severity switch
    {
        RedisHealthSeverity.Critical => 3,
        RedisHealthSeverity.Warning => 2,
        _ => 1,
    };

    private static long? NormalizeEstimate(long? estimatedKeyCount)
    {
        if (!estimatedKeyCount.HasValue || estimatedKeyCount.Value <= 0)
        {
            return null;
        }

        return estimatedKeyCount.Value;
    }

    private static double ComputeCoveragePercent(int loadedKeyCount, long? estimatedKeyCount)
    {
        if (loadedKeyCount <= 0)
        {
            return 0;
        }

        if (!estimatedKeyCount.HasValue)
        {
            return 100;
        }

        return Math.Clamp((double)loadedKeyCount / estimatedKeyCount.Value * 100, 0, 100);
    }

    private static string GetConfidenceLabel(int loadedKeyCount, long? estimatedKeyCount, double coveragePercent)
    {
        if (loadedKeyCount == 0)
        {
            return "No data";
        }

        if (!estimatedKeyCount.HasValue)
        {
            return "Estimated";
        }

        if (coveragePercent >= 95)
        {
            return "High";
        }

        if (coveragePercent >= 60)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.##} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.##} KB";
        }

        return $"{bytes} B";
    }
}
