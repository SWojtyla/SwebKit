using CommunityToolkit.Mvvm.ComponentModel;
using SwebKit.Core.Domain;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Observability;

public sealed partial class ObservabilityResourceItemViewModel : ObservableObject
{
    public ObservabilityResourceItemViewModel(ObservabilityResourceInfo resourceInfo)
    {
        ResourceInfo = resourceInfo;
    }

    public ObservabilityResourceInfo ResourceInfo { get; }

    public string ResourceId => ResourceInfo.ResourceId;

    public string Name => ResourceInfo.Name;

    public string SubscriptionName => string.IsNullOrWhiteSpace(ResourceInfo.SubscriptionName)
        ? ResourceInfo.SubscriptionId
        : ResourceInfo.SubscriptionName;

    public string ScopeLabel => $"{SubscriptionName} · {ResourceInfo.ResourceGroup}";

    public string LocationLabel => string.IsNullOrWhiteSpace(ResourceInfo.Location)
        ? "Location unavailable"
        : $"{ResourceInfo.Location} · {ResourceInfo.SubscriptionId}";

    public string DisplayPath => $"{SubscriptionName}/{ResourceInfo.ResourceGroup}/{ResourceInfo.Name}";

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public string ActiveStateLabel => IsActive ? "Active" : string.Empty;

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveStateLabel));
        OnPropertyChanged(nameof(ActiveVisibility));
    }
}

public sealed class ObservabilityTimeRangeOptionViewModel
{
    private readonly Func<TimeRange> _factory;

    public ObservabilityTimeRangeOptionViewModel(string restoreKey, string label, Func<TimeRange> factory)
    {
        RestoreKey = restoreKey;
        Label = label;
        _factory = factory;
    }

    public string RestoreKey { get; }

    public string Label { get; }

    public TimeRange CreateRange() => _factory();
}

public sealed class ObservabilityLogsModeOptionViewModel
{
    public ObservabilityLogsModeOptionViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }
}

public sealed class ObservabilityGuidedOperatorOptionViewModel
{
    public ObservabilityGuidedOperatorOptionViewModel(GuidedKqlFilterOperator @operator, string label)
    {
        Operator = @operator;
        Label = label;
    }

    public GuidedKqlFilterOperator Operator { get; }

    public string Label { get; }
}

public sealed class ObservabilityQueryPresetItemViewModel
{
    public ObservabilityQueryPresetItemViewModel(QueryPreset preset)
    {
        Preset = preset;
    }

    public QueryPreset Preset { get; }

    public string Id => Preset.Id;

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    public string Query => Preset.Query;
}

public sealed class ObservabilitySavedQueryItemViewModel
{
    public ObservabilitySavedQueryItemViewModel(SavedQuery savedQuery)
    {
        SavedQuery = savedQuery;
    }

    public SavedQuery SavedQuery { get; }

    public string Id => SavedQuery.Id;

    public string Name => string.IsNullOrWhiteSpace(SavedQuery.Name)
        ? "Untitled query"
        : SavedQuery.Name;

    public string Query => SavedQuery.Query;

    public string Summary
    {
        get
        {
            var text = SavedQuery.Query.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "No query text saved.";
            }

            return text.Length > 96 ? $"{text[..96]}..." : text;
        }
    }

    public string CreatedAtText => $"Saved {SavedQuery.CreatedAt.LocalDateTime:g}";
}

public sealed class ObservabilityFailureItemViewModel
{
    public ObservabilityFailureItemViewModel(ExceptionGroup group)
    {
        Group = group;
    }

    public ExceptionGroup Group { get; }

    public string ExceptionType => Group.ExceptionType;

    public string ProblemId => Group.ProblemId;

    public string DetailLabel => string.IsNullOrWhiteSpace(Group.ProblemId)
        ? Group.ExceptionType
        : $"{Group.ExceptionType} · {Group.ProblemId}";

    public string CountText => $"{Group.Count:N0} hits";

    public string LastSeenText => $"Last seen {Group.LastSeen.LocalDateTime:g}";

    public string Message => string.IsNullOrWhiteSpace(Group.SampleMessage)
        ? "No sample message returned for this exception group."
        : Group.SampleMessage!;

    public string StackTrace => string.IsNullOrWhiteSpace(Group.SampleStackTrace)
        ? "No stack trace returned for this exception group."
        : Group.SampleStackTrace!;

    public string? SampleOperationId => string.IsNullOrWhiteSpace(Group.SampleOperationId)
        ? null
        : Group.SampleOperationId;

    public string SampleOperationLabel => string.IsNullOrWhiteSpace(SampleOperationId)
        ? "No sample trace available"
        : $"Sample trace {SampleOperationId[..Math.Min(8, SampleOperationId.Length)]}";
}

public sealed class ObservabilityPerformanceItemViewModel
{
    public ObservabilityPerformanceItemViewModel(OperationPerformance operation)
    {
        Operation = operation;
    }

    public OperationPerformance Operation { get; }

    public string OperationName => Operation.OperationName;

    public string RequestCountText => $"{Operation.RequestCount:N0} requests";

    public string FailureRateText => $"{Operation.FailureRate:P1} failure rate";

    public string P50Text => $"P50 {Operation.P50Ms:N0} ms";

    public string P95Text => $"P95 {Operation.P95Ms:N0} ms";

    public string P99Text => $"P99 {Operation.P99Ms:N0} ms";
}

public sealed class ObservabilityLatencyPointItemViewModel
{
    public ObservabilityLatencyPointItemViewModel(LatencyDataPoint point)
    {
        Point = point;
    }

    public LatencyDataPoint Point { get; }

    public string TimeLabel => Point.Timestamp.LocalDateTime.ToString("g");

    public string PercentileSummary => $"P50 {Point.P50Ms:N0} ms · P95 {Point.P95Ms:N0} ms · P99 {Point.P99Ms:N0} ms";
}

public sealed class ObservabilityAvailabilityItemViewModel
{
    public ObservabilityAvailabilityItemViewModel(AvailabilityResult result)
    {
        Result = result;
    }

    public AvailabilityResult Result { get; }

    public string TestName => Result.TestName;

    public string StatusText => Result.Success ? "Healthy" : "Failed";

    public string SummaryText => $"{Result.Location} · {Result.Timestamp.LocalDateTime:g}";

    public string DurationText => Result.Success
        ? $"{Result.DurationMs:N0} ms"
        : string.IsNullOrWhiteSpace(Result.FailureMessage)
            ? "Failed"
            : Result.FailureMessage!;
}

public sealed class ObservabilityAvailabilityHeatmapCellViewModel
{
    private static readonly SolidColorBrush EmptyBackgroundBrush = new(ColorHelper.FromArgb(255, 38, 40, 43));
    private static readonly SolidColorBrush EmptyForegroundBrush = new(Colors.White);
    private static readonly SolidColorBrush CriticalBackgroundBrush = new(ColorHelper.FromArgb(255, 161, 43, 59));
    private static readonly SolidColorBrush CriticalForegroundBrush = new(Colors.White);
    private static readonly SolidColorBrush WarningBackgroundBrush = new(ColorHelper.FromArgb(255, 190, 127, 42));
    private static readonly SolidColorBrush WarningForegroundBrush = new(Colors.Black);
    private static readonly SolidColorBrush HealthyBackgroundBrush = new(ColorHelper.FromArgb(255, 49, 120, 78));
    private static readonly SolidColorBrush HealthyForegroundBrush = new(Colors.White);

    public ObservabilityAvailabilityHeatmapCellViewModel(string hourLabel, int passCount, int totalCount)
    {
        HourLabel = hourLabel;
        PassCount = passCount;
        TotalCount = totalCount;
    }

    public string HourLabel { get; }

    public int PassCount { get; }

    public int TotalCount { get; }

    public double AvailabilityPercent => TotalCount == 0 ? 0 : PassCount * 100d / TotalCount;

    public string ValueText => TotalCount == 0 ? "-" : $"{AvailabilityPercent:F0}%";

    public string CountText => TotalCount == 0 ? "No samples" : $"{PassCount}/{TotalCount} pass";

    public string TooltipText => TotalCount == 0
        ? $"{HourLabel}: no availability samples returned for this hour."
        : $"{HourLabel}: {PassCount}/{TotalCount} pass ({AvailabilityPercent:F1}%).";

    public Brush BackgroundBrush => TotalCount == 0
        ? EmptyBackgroundBrush
        : AvailabilityPercent < 90
            ? CriticalBackgroundBrush
            : AvailabilityPercent < 99
                ? WarningBackgroundBrush
                : HealthyBackgroundBrush;

    public Brush ForegroundBrush => TotalCount == 0
        ? EmptyForegroundBrush
        : AvailabilityPercent < 99
            ? WarningForegroundBrush
            : HealthyForegroundBrush;
}

public sealed class ObservabilityAvailabilityHeatmapRowViewModel
{
    public ObservabilityAvailabilityHeatmapRowViewModel(string testName, IReadOnlyList<ObservabilityAvailabilityHeatmapCellViewModel> cells)
    {
        TestName = testName;
        Cells = cells;
    }

    public string TestName { get; }

    public IReadOnlyList<ObservabilityAvailabilityHeatmapCellViewModel> Cells { get; }
}

public sealed class ObservabilityLogRowItemViewModel
{
    public ObservabilityLogRowItemViewModel(string primaryText, string secondaryText, string detailText, string severityText)
    {
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        DetailText = detailText;
        SeverityText = severityText;
    }

    public string PrimaryText { get; }

    public string SecondaryText { get; }

    public string DetailText { get; }

    public string SeverityText { get; }
}

public sealed class ObservabilityDependencyHealthItemViewModel
{
    public ObservabilityDependencyHealthItemViewModel(DependencyHealthEntry entry)
    {
        Entry = entry;
    }

    public DependencyHealthEntry Entry { get; }

    public string DependencyName => Entry.DependencyName;

    public string SummaryText => $"{Entry.DependencyType} · {Entry.CallCount:N0} calls";

    public string HealthText => $"{Entry.FailureRate:P1} failure rate · P95 {Entry.P95Ms:N0} ms";
}

public sealed class ObservabilityDimensionBreakdownItemViewModel
{
    public ObservabilityDimensionBreakdownItemViewModel(string dimensionKey, DimensionBreakdownEntry entry)
    {
        DimensionKey = dimensionKey;
        Entry = entry;
    }

    public string DimensionKey { get; }

    public DimensionBreakdownEntry Entry { get; }

    public string Label => $"{DimensionKey}: {Entry.Value}";

    public string SummaryText => $"{Entry.Count:N0} hits · {Entry.FailureRate:P1} failure rate";
}

public sealed class ObservabilityDeploymentAnchorItemViewModel
{
    public ObservabilityDeploymentAnchorItemViewModel(DeploymentAnchor anchor)
    {
        Anchor = anchor;
    }

    public DeploymentAnchor Anchor { get; }

    public string ReleaseName => Anchor.ReleaseName;

    public string AnchorTimeText => $"Anchored {Anchor.AnchorTime.LocalDateTime:g}";
}

public sealed class ObservabilityMetricDeltaItemViewModel
{
    public ObservabilityMetricDeltaItemViewModel(MetricDelta delta)
    {
        Delta = delta;
    }

    public MetricDelta Delta { get; }

    public string MetricLabel => Delta.MetricName switch
    {
        "FailureRate" => "Failure rate",
        "P50ResponseTimeMs" => "P50 latency",
        "P95ResponseTimeMs" => "P95 latency",
        "AvailabilityPct" => "Availability",
        _ => Delta.MetricName,
    };

    public string BeforeText => FormatMetricValue(Delta.MetricName, Delta.Before);

    public string AfterText => FormatMetricValue(Delta.MetricName, Delta.After);

    public string ChangeText => $"{(Delta.DeltaPct >= 0 ? "+" : "-")}{Math.Abs(Delta.DeltaPct):F1}%";

    public string ChangeDirectionLabel => Delta.DeltaPct >= 0 ? "Increase" : "Decrease";

    private static string FormatMetricValue(string metricName, double value) => metricName switch
    {
        "FailureRate" => $"{value * 100:F1}%",
        "AvailabilityPct" => $"{value:F1}%",
        "P50ResponseTimeMs" or "P95ResponseTimeMs" => $"{value:N0} ms",
        _ => value < 10_000 ? $"{value:F2}" : $"{value:N0}",
    };
}

public sealed class ObservabilitySloStatusEntryItemViewModel
{
    private static readonly SolidColorBrush MetBackgroundBrush = new(ColorHelper.FromArgb(255, 49, 120, 78));
    private static readonly SolidColorBrush MetForegroundBrush = new(Colors.White);
    private static readonly SolidColorBrush AtRiskBackgroundBrush = new(ColorHelper.FromArgb(255, 190, 127, 42));
    private static readonly SolidColorBrush AtRiskForegroundBrush = new(Colors.Black);
    private static readonly SolidColorBrush BreachedBackgroundBrush = new(ColorHelper.FromArgb(255, 161, 43, 59));
    private static readonly SolidColorBrush BreachedForegroundBrush = new(Colors.White);

    public ObservabilitySloStatusEntryItemViewModel(SloStatusEntry entry)
    {
        Entry = entry;
    }

    public SloStatusEntry Entry { get; }

    public string Name => Entry.Definition.Name;

    public string MetricLabel => Entry.Definition.Metric switch
    {
        SloMetric.FailureRate => "Failure rate",
        SloMetric.P95ResponseTimeMs => "P95 latency",
        SloMetric.AvailabilityPct => "Availability",
        _ => Entry.Definition.Metric.ToString(),
    };

    public string CurrentValueText => FormatValue(Entry.CurrentValue, Entry.Definition.Metric);

    public string TargetValueText => FormatValue(Entry.Definition.Target, Entry.Definition.Metric);

    public string StateLabel => Entry.State switch
    {
        SloState.Met => "Met",
        SloState.AtRisk => "At risk",
        SloState.Breached => "Breached",
        _ => Entry.State.ToString(),
    };

    public Brush StateBackgroundBrush => Entry.State switch
    {
        SloState.Met => MetBackgroundBrush,
        SloState.AtRisk => AtRiskBackgroundBrush,
        SloState.Breached => BreachedBackgroundBrush,
        _ => MetBackgroundBrush,
    };

    public Brush StateForegroundBrush => Entry.State switch
    {
        SloState.Met => MetForegroundBrush,
        SloState.AtRisk => AtRiskForegroundBrush,
        SloState.Breached => BreachedForegroundBrush,
        _ => MetForegroundBrush,
    };

    private static string FormatValue(double value, SloMetric metric) => metric switch
    {
        SloMetric.FailureRate => $"{value * 100:F2}%",
        SloMetric.P95ResponseTimeMs => $"{value:F0} ms",
        SloMetric.AvailabilityPct => $"{value:F2}%",
        _ => $"{value:F2}",
    };
}