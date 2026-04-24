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
        string? keyType)
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
        PrefixCountLabel = keyCount == 1 ? "1 key" : $"{keyCount} keys";
        PrefixCountVisibility = isKey ? Visibility.Collapsed : Visibility.Visible;
        InspectVisibility = isKey ? Visibility.Visible : Visibility.Collapsed;
        FilterVisibility = isKey ? Visibility.Collapsed : Visibility.Visible;
    }

    public string RowId { get; }

    public string DisplayName { get; }

    public bool IsKey { get; }

    public string? FullKey { get; }

    public string Prefix { get; }

    public int KeyCount { get; }

    public int Depth { get; }

    public string KeyType { get; }

    public Thickness IndentMargin { get; }

    public string ExpandGlyph { get; }

    public Visibility ExpandVisibility { get; }

    public Visibility SelectedVisibility { get; }

    public Visibility KeyTypeVisibility { get; }

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