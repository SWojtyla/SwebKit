using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Settings;

public static class SettingsSections
{
    public const string Appearance = "appearance";
    public const string ServiceBus = "servicebus";
    public const string Aks = "aks";
    public const string Redis = "redis";
    public const string DevOps = "devops";
    public const string IncidentTimeline = "incident-timeline";
    public const string Storage = "storage";
    public const string Observability = "observability";

    public static string? Normalize(string? section) => section?.Trim().ToLowerInvariant() switch
    {
        Appearance => Appearance,
        ServiceBus => ServiceBus,
        Aks => Aks,
        Redis => Redis,
        DevOps => DevOps,
        IncidentTimeline => IncidentTimeline,
        Storage => Storage,
        Observability => Observability,
        _ => null,
    };
}

public sealed record SettingsNavigationRequest
{
    public SettingsNavigationRequest(
        string? section,
        string? suggestedNamespace = null,
        IncidentWorkloadKind? suggestedWorkloadKind = null,
        string? suggestedWorkloadName = null)
    {
        Section = SettingsSections.Normalize(section) ?? SettingsSections.Appearance;
        SuggestedNamespace = string.IsNullOrWhiteSpace(suggestedNamespace) ? null : suggestedNamespace.Trim();
        SuggestedWorkloadKind = suggestedWorkloadKind;
        SuggestedWorkloadName = string.IsNullOrWhiteSpace(suggestedWorkloadName) ? null : suggestedWorkloadName.Trim();
    }

    public string Section { get; }

    public string? SuggestedNamespace { get; }

    public IncidentWorkloadKind? SuggestedWorkloadKind { get; }

    public string? SuggestedWorkloadName { get; }
}