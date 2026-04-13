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
    string? ProfilePersistenceBlockedMessage);

public sealed record ConfigurationHealthReport(
    ConfigurationCheckStatus OverallStatus,
    string Summary,
    bool IsFirstRun,
    IReadOnlyList<ConfigurationActionItem> ActionItems,
    IReadOnlyList<ConfigurationAreaHealth> Areas)
{
    public int ReadyAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.Ready);
    public int ConfiguredAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.Configured);
    public int WarningAreaCount => Areas.Count(area => area.Status is ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error);
    public int MissingAreaCount => Areas.Count(area => area.Status == ConfigurationCheckStatus.NotConfigured);
    public bool RequiresDashboardAttention => ActionItems.Count > 0 || OverallStatus is ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error;
    public IReadOnlyList<ConfigurationAreaHealth> AttentionAreas => Areas.Where(area => area.RequiresDashboardAttention).ToList();
}

public sealed record ConfigurationAreaHealth(
    string AreaKey,
    string Title,
    string SettingsSection,
    ConfigurationCheckStatus Status,
    string Summary,
    string? Detail,
    IReadOnlyList<ConfigurationActionItem> ActionItems,
    IReadOnlyList<CredentialReferenceHealth> CredentialReferences)
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