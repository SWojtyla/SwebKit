using System.Globalization;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Redis;

public sealed class RedisTreeRowViewModel
{
    public RedisTreeRowViewModel(
        string rowId,
        string displayName,
        bool isKey,
        string? fullKey,
        string prefix,
        int keyCount,
        int depth,
        bool canExpand,
        bool isExpanded,
        bool isSelected,
        string? keyType,
        IReadOnlyList<string> selectionKeys,
        bool isSelectionMode,
        int selectedKeyCount)
    {
        RowId = rowId;
        DisplayName = displayName;
        IsKey = isKey;
        FullKey = fullKey;
        Prefix = prefix;
        KeyCount = keyCount;
        Depth = depth;
        KeyType = keyType ?? string.Empty;
        IndentMargin = new Thickness(depth * 16, 0, 0, 0);
        ExpandGlyph = canExpand ? (isExpanded ? "▾" : "▸") : string.Empty;
        ExpandVisibility = canExpand ? Visibility.Visible : Visibility.Collapsed;
        SelectedVisibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        KeyTypeVisibility = isKey && !string.IsNullOrWhiteSpace(KeyType)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectionKeys = selectionKeys;
        IsFullySelected = selectionKeys.Count > 0 && selectedKeyCount == selectionKeys.Count;
        IsPartiallySelected = selectedKeyCount > 0 && selectedKeyCount < selectionKeys.Count;
        SelectionStatusText = selectionKeys.Count == 0
            ? string.Empty
            : IsFullySelected
                ? (selectionKeys.Count == 1 ? "Selected" : $"{selectionKeys.Count} selected")
                : IsPartiallySelected
                    ? $"{selectedKeyCount} of {selectionKeys.Count} selected"
                    : string.Empty;
        SelectionStatusVisibility = isSelectionMode && !string.IsNullOrWhiteSpace(SelectionStatusText)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectionActionLabel = IsFullySelected ? "Clear" : "Select";
        SelectionActionVisibility = isSelectionMode && selectionKeys.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PrefixCountLabel = keyCount == 1 ? "1 key" : $"{keyCount} keys";
        PrefixCountVisibility = isKey || isSelectionMode ? Visibility.Collapsed : Visibility.Visible;
        InspectVisibility = isSelectionMode || !isKey ? Visibility.Collapsed : Visibility.Visible;
        FilterVisibility = isSelectionMode || isKey ? Visibility.Collapsed : Visibility.Visible;
    }

    public string RowId { get; }

    public string DisplayName { get; }

    public bool IsKey { get; }

    public string? FullKey { get; }

    public string Prefix { get; }

    public int KeyCount { get; }

    public int Depth { get; }

    public string KeyType { get; }

    public IReadOnlyList<string> SelectionKeys { get; }

    public Thickness IndentMargin { get; }

    public string ExpandGlyph { get; }

    public Visibility ExpandVisibility { get; }

    public Visibility SelectedVisibility { get; }

    public Visibility KeyTypeVisibility { get; }

    public bool IsFullySelected { get; }

    public bool IsPartiallySelected { get; }

    public string SelectionStatusText { get; }

    public Visibility SelectionStatusVisibility { get; }

    public string SelectionActionLabel { get; }

    public Visibility SelectionActionVisibility { get; }

    public string PrefixCountLabel { get; }

    public Visibility PrefixCountVisibility { get; }

    public Visibility InspectVisibility { get; }

    public Visibility FilterVisibility { get; }
}

public sealed class RedisHashFieldItemViewModel
{
    public RedisHashFieldItemViewModel(RedisHashField field)
    {
        Field = field.Field;
        Value = field.Value;
    }

    public string Field { get; }

    public string Value { get; }
}

public sealed class RedisSortedSetEntryItemViewModel
{
    public RedisSortedSetEntryItemViewModel(RedisSortedSetEntry entry)
    {
        Member = entry.Member;
        Score = entry.Score;
        ScoreText = entry.Score.ToString("G", CultureInfo.InvariantCulture);
    }

    public string Member { get; }

    public double Score { get; }

    public string ScoreText { get; }
}

public sealed class RedisHealthFindingItemViewModel
{
    public RedisHealthFindingItemViewModel(RedisHealthFinding finding)
    {
        Finding = finding;
        SeverityText = finding.Severity.ToString();
        RiskTypeText = finding.RiskType switch
        {
            RedisHealthRiskType.NoTtl => "No TTL",
            RedisHealthRiskType.OversizedValue => "Oversized Value",
            RedisHealthRiskType.HeavyPrefix => "Heavy Prefix",
            RedisHealthRiskType.PossibleHotKey => "Possible Hot Key",
            RedisHealthRiskType.HotKeySignalUnavailable => "Hot-Key Signal",
            _ => finding.RiskType.ToString(),
        };
        MetricsText = BuildMetricsText(finding);
        CanOpenKey = !string.IsNullOrWhiteSpace(finding.DrillKey);
        OpenKeyVisibility = CanOpenKey ? Visibility.Visible : Visibility.Collapsed;
    }

    public RedisHealthFinding Finding { get; }

    public string SeverityText { get; }

    public string RiskTypeText { get; }

    public string MetricsText { get; }

    public bool CanOpenKey { get; }

    public Visibility OpenKeyVisibility { get; }

    private static string BuildMetricsText(RedisHealthFinding finding)
    {
        var parts = new List<string>(4);

        if (finding.MemoryBytes.HasValue)
        {
            parts.Add(FormatBytes(finding.MemoryBytes.Value));
        }

        if (finding.KeyCount.HasValue)
        {
            parts.Add($"{finding.KeyCount.Value} keys");
        }

        if (finding.SharePercent.HasValue)
        {
            parts.Add($"{finding.SharePercent.Value:0.#}% share");
        }

        if (finding.Frequency.HasValue)
        {
            parts.Add($"freq={finding.Frequency.Value}");
        }

        if (finding.IdleSeconds.HasValue)
        {
            parts.Add($"idle={finding.IdleSeconds.Value}s");
        }

        return parts.Count == 0 ? "-" : string.Join(" | ", parts);
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

public sealed class PrefixMemoryBucketItemViewModel
{
    public PrefixMemoryBucketItemViewModel(PrefixMemoryBucket bucket)
    {
        Prefix = bucket.Prefix;
        Percentage = bucket.Percentage;
        PercentageText = $"{bucket.Percentage:0.#}%";
        TotalBytesText = FormatBytes(bucket.TotalBytes);
        KeyCountText = bucket.KeyCount == 1 ? "1 key" : $"{bucket.KeyCount} keys";
    }

    public string Prefix { get; }

    public double Percentage { get; }

    public string PercentageText { get; }

    public string TotalBytesText { get; }

    public string KeyCountText { get; }

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

public sealed class RedisSlowLogEntryItemViewModel
{
    public RedisSlowLogEntryItemViewModel(RedisSlowLogEntryInfo entry)
    {
        Entry = entry;
        ExecutedAtText = entry.ExecutedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        DurationText = $"{entry.Duration.TotalMilliseconds:0.#} ms";
        CommandText = entry.Command;
        ArgumentsText = string.IsNullOrWhiteSpace(entry.Arguments) ? "-" : entry.Arguments;
        ClientNameText = string.IsNullOrWhiteSpace(entry.ClientName) ? "Unknown client" : entry.ClientName!;
    }

    public RedisSlowLogEntryInfo Entry { get; }

    public string ExecutedAtText { get; }

    public string DurationText { get; }

    public string CommandText { get; }

    public string ArgumentsText { get; }

    public string ClientNameText { get; }
}

public sealed class RedisHotKeySignalItemViewModel
{
    public RedisHotKeySignalItemViewModel(RedisHotKeySignal signal)
    {
        Signal = signal;
        MetricsText = BuildMetricsText(signal);
    }

    public RedisHotKeySignal Signal { get; }

    public string MetricsText { get; }

    private static string BuildMetricsText(RedisHotKeySignal signal)
    {
        var parts = new List<string>(3);

        if (signal.FrequencyScore.HasValue)
        {
            parts.Add($"score={signal.FrequencyScore.Value:0.#}");
        }

        if (signal.IdleSeconds.HasValue)
        {
            parts.Add($"idle={signal.IdleSeconds.Value:0.#}s");
        }

        if (signal.MemoryBytes.HasValue)
        {
            parts.Add(FormatBytes(signal.MemoryBytes.Value));
        }

        return parts.Count == 0 ? "-" : string.Join(" | ", parts);
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

public sealed class RedisPubSubChannelItemViewModel
{
    public RedisPubSubChannelItemViewModel(RedisPubSubChannelInfo channel)
    {
        Channel = channel.Channel;
        SubscriberCountText = channel.SubscriberCount == 1
            ? "1 subscriber"
            : $"{channel.SubscriberCount} subscribers";
    }

    public string Channel { get; }

    public string SubscriberCountText { get; }
}