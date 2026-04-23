using System.Linq;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Models;

public enum ConfigurationCheckStatus
{
    NotConfigured,
    Configured,
    Ready,
    Warning,
    Error,
    Skipped
}

public enum CredentialReferenceSource
{
    CredentialStore,
    InConfig,
    AzureCliIdentity,
    Kubeconfig,
    NotApplicable
}

public sealed record ConfigurationHealthContext(
    AppConfig Config,
    IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces,
    bool UseDemoData,
    bool HasProfileLoadFailure,
    string? ProfilePersistenceBlockedMessage,
    ConfigurationProbeSnapshot? ProbeSnapshot = null);

public sealed record ConfigurationProbeSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyDictionary<string, ConfigurationAreaProbeResult> AreaResults)
{
    public bool HasResults => AreaResults.Count > 0;
}

public sealed record ConfigurationAreaProbeResult(
    string AreaKey,
    ConfigurationCheckStatus Status,
    string Summary,
    string? Detail,
    DateTimeOffset CheckedAt,
    TimeSpan Duration);

public sealed record ConfigurationHealthReport(
    ConfigurationCheckStatus OverallStatus,
    string Summary,
    bool IsFirstRun,
    IReadOnlyList<ConfigurationActionItem> ActionItems,
    IReadOnlyList<ConfigurationAreaHealth> Areas,
    ConfigurationProbeSnapshot? ProbeSnapshot = null)
{
    public int ReadyAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.Ready);
    public int ConfiguredAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.Configured);
    public int WarningAreaCount => Areas.Count(area => area.Status is ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error);
    public int MissingAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.NotConfigured);
    public bool RequiresDashboardAttention => ActionItems.Count > 0 || OverallStatus is ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error;
    public IReadOnlyList<ConfigurationAreaHealth> AttentionAreas => Areas.Where(area => area.RequiresDashboardAttention).ToList();
    public DateTimeOffset? LastProbeCompletedAt => ProbeSnapshot?.CompletedAt;
}

public sealed record ConfigurationAreaHealth(
    string AreaKey,
    string Title,
    string SettingsSection,
    ConfigurationCheckStatus Status,
    string Summary,
    string? Detail,
    IReadOnlyList<ConfigurationActionItem> ActionItems,
    IReadOnlyList<CredentialReferenceHealth> CredentialReferences,
    bool CanRunLiveProbe = false,
    ConfigurationAreaProbeResult? LiveProbe = null)
{
    public bool RequiresDashboardAttention =>
        ActionItems.Count > 0 || Status is ConfigurationCheckStatus.NotConfigured or ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error;
}

public sealed record ConfigurationActionItem(
    string Key,
    string Title,
    string Summary,
    string SettingsSection,
    string ActionLabel);

public sealed record CredentialReferenceHealth(
    string Label,
    CredentialReferenceSource Source,
    string? ReferenceKey,
    bool IsPresent,
    string Summary);