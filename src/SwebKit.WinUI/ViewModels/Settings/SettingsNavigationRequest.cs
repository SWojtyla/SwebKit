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
    public SettingsNavigationRequest(string? section)
    {
        Section = SettingsSections.Normalize(section) ?? SettingsSections.Appearance;
    }

    public string Section { get; }
}