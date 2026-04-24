using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
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