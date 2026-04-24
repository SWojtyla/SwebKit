using Microsoft.UI.Xaml;
using SwebKit.Core.Domain;

namespace SwebKit.WinUI.ViewModels.Dashboard;

public sealed record DashboardHealthMetric(int Value, string Label, DateTimeOffset LastUpdated);

public sealed record DashboardHealthTileItem(
    string Title,
    string Glyph,
    bool IsConfigured,
    DashboardHealthMetric? Metric,
    string Summary,
    string? ErrorMessage = null)
{
    public string MetricText => !IsConfigured
        ? "-"
        : !string.IsNullOrWhiteSpace(ErrorMessage)
            ? "!"
            : Metric?.Value.ToString("N0") ?? "0";

    public string MetricLabel => !IsConfigured
        ? "Needs setup"
        : !string.IsNullOrWhiteSpace(ErrorMessage)
            ? "Needs attention"
            : Metric?.Label ?? "Awaiting data";

    public string DetailText => !IsConfigured
        ? "Open Settings to configure this workspace before relying on its dashboard health signal."
        : !string.IsNullOrWhiteSpace(ErrorMessage)
            ? ErrorMessage!
            : string.IsNullOrWhiteSpace(Summary)
                ? "Dashboard metric updated successfully."
                : Summary;

    public string TimestampText => Metric is null
        ? string.Empty
        : $"Updated {Metric.LastUpdated.LocalDateTime:g}";

    public Visibility TimestampVisibility => Metric is null ? Visibility.Collapsed : Visibility.Visible;

    public static DashboardHealthTileItem NotConfigured(string title, string glyph) =>
        new(title, glyph, false, null, string.Empty);

    public static DashboardHealthTileItem Ready(string title, string glyph, DashboardHealthMetric metric, string summary) =>
        new(title, glyph, true, metric, summary);

    public static DashboardHealthTileItem Warning(string title, string glyph, string errorMessage) =>
        new(title, glyph, true, null, string.Empty, errorMessage);
}

public sealed record DashboardReadinessAreaItem(
    string AreaKey,
    string SettingsSection,
    string Title,
    string StatusLabel,
    string Summary,
    string? Detail,
    string ActionLabel)
{
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed record DashboardPodHealthAlertItem(
    string PodName,
    string Namespace,
    string EventType,
    string StatusText,
    string TimestampText)
{
    public string DetailText => $"{EventType} · {StatusText}";
}

public sealed record DashboardActivityItem(
    string Description,
    string Icon,
    string Area,
    string TimestampText);

public sealed record DashboardFavoriteItem(
    string DisplayPath,
    string Icon,
    string Summary,
    WorkspaceSnapshot Snapshot);

public sealed record DashboardNamespaceItem(string Name);