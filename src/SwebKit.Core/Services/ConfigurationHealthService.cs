using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class ConfigurationHealthService(ICredentialStore credentialStore) : IConfigurationHealthService
{
    private const string ServiceBusSection = "servicebus";
    private const string AksSection = "aks";
    private const string RedisSection = "redis";
    private const string DevOpsSection = "devops";
    private const string StorageSection = "storage";
    private const string ObservabilitySection = "observability";
    private const string IncidentTimelineSection = "incident-timeline";

    public ConfigurationHealthReport BuildReport(ConfigurationHealthContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Config);

        if (context.UseDemoData)
        {
            var demoAreas = BuildDemoAreas();
            return new ConfigurationHealthReport(
                ConfigurationCheckStatus.Ready,
                "Demo mode marks the core operator workflows as ready with synthetic data.",
                false,
                [],
                demoAreas);
        }

        var serviceBusArea = BuildServiceBusArea(context.ServiceBusNamespaces);
        var aksArea = BuildAksArea(context.Config.AksConfig);
        var redisArea = BuildRedisArea(context.Config.RedisConfig);
        var devOpsArea = BuildDevOpsArea(context.Config.DevOpsConfig);
        var storageArea = BuildStorageArea(context.Config.StorageAccounts);
        var observabilityArea = BuildObservabilityArea(context.Config.ObservabilityConfig);
        var incidentTimelineArea = BuildIncidentTimelineArea(
            context.Config.IncidentTimeline,
            aksArea,
            serviceBusArea,
            devOpsArea);

        IReadOnlyList<ConfigurationAreaHealth> areas =
        [
            serviceBusArea,
            aksArea,
            redisArea,
            devOpsArea,
            storageArea,
            observabilityArea,
            incidentTimelineArea,
        ];

        var actionItems = areas
            .SelectMany(area => area.ActionItems)
            .ToList();

        var isFirstRun = IsFirstRun(context);
        var overallStatus = ComputeOverallStatus(context, areas, isFirstRun);
        var summary = BuildOverallSummary(context, areas, actionItems, isFirstRun, overallStatus);

        return new ConfigurationHealthReport(overallStatus, summary, isFirstRun, actionItems, areas);
    }

    private IReadOnlyList<ConfigurationAreaHealth> BuildDemoAreas() =>
    [
        CreateArea(
            ServiceBusSection,
            "Service Bus",
            ServiceBusSection,
            ConfigurationCheckStatus.Ready,
            "Demo namespaces are available for messaging workflows.",
            "Synthetic dead-letter counts and message browsing stay available without live credentials."),
        CreateArea(
            AksSection,
            "AKS",
            AksSection,
            ConfigurationCheckStatus.Ready,
            "Demo cluster defaults are available.",
            "The AKS workspace will use synthetic cluster data instead of a live kubeconfig."),
        CreateArea(
            RedisSection,
            "Redis",
            RedisSection,
            ConfigurationCheckStatus.Ready,
            "A demo cache is ready for keyspace exploration.",
            "Redis diagnostics can run without a live connection string in demo mode."),
        CreateArea(
            DevOpsSection,
            "Azure DevOps",
            DevOpsSection,
            ConfigurationCheckStatus.Ready,
            "Demo projects, pipelines, and approvals are available.",
            "Pipeline and approval surfaces stay usable without a PAT in demo mode."),
        CreateArea(
            StorageSection,
            "Storage",
            StorageSection,
            ConfigurationCheckStatus.Ready,
            "Demo storage data is ready for inspection.",
            "Blob browsing stays local to synthetic data in demo mode."),
        CreateArea(
            ObservabilitySection,
            "Observability",
            ObservabilitySection,
            ConfigurationCheckStatus.Ready,
            "Demo Application Insights resources are available.",
            "Resource discovery and queries run against synthetic telemetry in demo mode."),
        CreateArea(
            IncidentTimelineSection,
            "Incident Timeline",
            IncidentTimelineSection,
            ConfigurationCheckStatus.Ready,
            "Demo workload mappings are ready for investigation flows.",
            "Incident handoff can start from synthetic evidence without live dependencies."),
    ];

    private ConfigurationAreaHealth BuildServiceBusArea(IReadOnlyList<ServiceBusNamespace> namespaces)
    {
        if (namespaces.Count == 0)
        {
            return CreateArea(
                ServiceBusSection,
                "Service Bus",
                ServiceBusSection,
                ConfigurationCheckStatus.NotConfigured,
                "No Service Bus namespaces are configured yet.",
                "Add at least one namespace so messaging workspaces and incident mappings have a source.",
                [CreateAction(
                    "servicebus-add-namespace",
                    "Add a Service Bus namespace",
                    "Configure a namespace connection and store its connection string reference in Windows Credential Manager.",
                    ServiceBusSection,
                    "Open Service Bus settings")]);
        }

        var credentialReferences = namespaces
            .Select(namespaceConfig =>
            {
                var displayName = DisplayServiceBusNamespace(namespaceConfig);
                var isPresent = HasCredential(namespaceConfig.CredentialKey);
                return new CredentialReferenceHealth(
                    displayName,
                    CredentialReferenceSource.CredentialStore,
                    namespaceConfig.CredentialKey,
                    isPresent,
                    isPresent
                        ? "Connection-string reference is available in Windows Credential Manager."
                        : "Connection-string reference is missing from Windows Credential Manager.");
            })
            .ToList();

        var missingCredentialCount = credentialReferences.Count(reference => !reference.IsPresent);
        if (missingCredentialCount > 0)
        {
            return CreateArea(
                ServiceBusSection,
                "Service Bus",
                ServiceBusSection,
                ConfigurationCheckStatus.Warning,
                $"{namespaces.Count} namespace(s) are configured, but {missingCredentialCount} credential reference(s) are missing.",
                "Repair the missing connection-string references before using the Service Bus workspace or evidence mappings.",
                [CreateAction(
                    "servicebus-fix-credentials",
                    "Repair missing Service Bus credentials",
                    "One or more namespace credential references are missing from Windows Credential Manager.",
                    ServiceBusSection,
                    "Open Service Bus settings")],
                credentialReferences);
        }

        return CreateArea(
            ServiceBusSection,
            "Service Bus",
            ServiceBusSection,
            ConfigurationCheckStatus.Ready,
            $"{namespaces.Count} namespace(s) are configured and credential references are available.",
            namespaces.Count == 1
                ? $"Namespace '{DisplayServiceBusNamespace(namespaces[0])}' can be used immediately by messaging workspaces."
                : "Messaging workspaces can use the configured namespace credential references.",
            credentialReferences: credentialReferences);
    }

    private static ConfigurationAreaHealth BuildAksArea(AksConfig? config)
    {
        if (config is null)
        {
            return CreateArea(
                AksSection,
                "AKS",
                AksSection,
                ConfigurationCheckStatus.NotConfigured,
                "No AKS defaults are configured yet.",
                "Set a default namespace and, if needed, a kubeconfig path or context so the AKS workspace can bootstrap predictably.",
                [CreateAction(
                    "aks-configure-defaults",
                    "Add AKS defaults",
                    "Set the default namespace and optional kubeconfig settings used by the AKS workspace.",
                    AksSection,
                    "Open AKS settings")]);
        }

        var defaultNamespace = string.IsNullOrWhiteSpace(config.DefaultNamespace) ? null : config.DefaultNamespace.Trim();
        if (defaultNamespace is null)
        {
            return CreateArea(
                AksSection,
                "AKS",
                AksSection,
                ConfigurationCheckStatus.Warning,
                "AKS defaults are saved, but the default namespace is missing.",
                "Set a default namespace before relying on the AKS workspace to open with a stable scope.",
                [CreateAction(
                    "aks-fix-namespace",
                    "Set an AKS default namespace",
                    "The AKS workspace needs a stable default namespace even when kubeconfig path and context stay optional.",
                    AksSection,
                    "Open AKS settings")]);
        }

        var scopeSummary = string.IsNullOrWhiteSpace(config.KubeconfigContext)
            ? $"Uses the current kubeconfig context with the '{defaultNamespace}' namespace."
            : $"Targets kubeconfig context '{config.KubeconfigContext}' with the '{defaultNamespace}' namespace.";

        var pathSummary = string.IsNullOrWhiteSpace(config.KubeconfigPath)
            ? "Live access still depends on the kubeconfig and Azure auth available on this machine."
            : "Live access still depends on the configured kubeconfig path and the auth available on this machine.";

        return CreateArea(
            AksSection,
            "AKS",
            AksSection,
            ConfigurationCheckStatus.Configured,
            "AKS defaults are configured.",
            $"{scopeSummary} {pathSummary}");
    }

    private static ConfigurationAreaHealth BuildRedisArea(RedisConfig? config)
    {
        config?.EnsureMigrated();
        var activeCache = config?.ActiveCache;
        if (activeCache is null)
        {
            return CreateArea(
                RedisSection,
                "Redis",
                RedisSection,
                ConfigurationCheckStatus.NotConfigured,
                "No Redis cache is configured yet.",
                "Add a cache connection before using the Redis workspace.",
                [CreateAction(
                    "redis-add-cache",
                    "Add a Redis cache",
                    "Configure the active Redis cache connection used by keyspace browsing and diagnostics.",
                    RedisSection,
                    "Open Redis settings")]);
        }

        if (string.IsNullOrWhiteSpace(activeCache.ConnectionString))
        {
            return CreateArea(
                RedisSection,
                "Redis",
                RedisSection,
                ConfigurationCheckStatus.Warning,
                "The active Redis cache is selected, but its connection string is missing.",
                "Repair the active cache entry before using Redis diagnostics.",
                [CreateAction(
                    "redis-fix-cache",
                    "Repair the active Redis cache",
                    "The active Redis cache is missing its connection string.",
                    RedisSection,
                    "Open Redis settings")]);
        }

        return CreateArea(
            RedisSection,
            "Redis",
            RedisSection,
            ConfigurationCheckStatus.Ready,
            $"Redis cache '{activeCache.DisplayName}' is configured.",
            $"The active cache targets database {activeCache.Database} and can be used immediately by Redis views.");
    }

    private ConfigurationAreaHealth BuildDevOpsArea(DevOpsConfig? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.Organization))
        {
            return CreateArea(
                DevOpsSection,
                "Azure DevOps",
                DevOpsSection,
                ConfigurationCheckStatus.NotConfigured,
                "Azure DevOps is not configured yet.",
                "Add an organization and PAT reference before using pipelines, approvals, or release evidence.",
                [CreateAction(
                    "devops-connect",
                    "Connect Azure DevOps",
                    "Save an organization and PAT reference so pipeline and approval views can authenticate.",
                    DevOpsSection,
                    "Open DevOps settings")]);
        }

        if (string.IsNullOrWhiteSpace(config.PatCredentialKey))
        {
            return CreateArea(
                DevOpsSection,
                "Azure DevOps",
                DevOpsSection,
                ConfigurationCheckStatus.Warning,
                "Azure DevOps has an organization, but no PAT reference is configured.",
                "Add the PAT reference before using pipelines, approvals, or incident release evidence.",
                [CreateAction(
                    "devops-add-pat-reference",
                    "Add the Azure DevOps PAT reference",
                    "The organization is saved, but the PAT reference key is missing.",
                    DevOpsSection,
                    "Open DevOps settings")]);
        }

        var hasPat = HasCredential(config.PatCredentialKey);
        var credentialReferences = new List<CredentialReferenceHealth>
        {
            new(
                "Azure DevOps PAT",
                CredentialReferenceSource.CredentialStore,
                config.PatCredentialKey,
                hasPat,
                hasPat
                    ? "PAT reference is available in Windows Credential Manager."
                    : "PAT reference is missing from Windows Credential Manager.")
        };

        if (!hasPat)
        {
            return CreateArea(
                DevOpsSection,
                "Azure DevOps",
                DevOpsSection,
                ConfigurationCheckStatus.Warning,
                $"Azure DevOps organization '{config.Organization.Trim()}' is configured, but the PAT reference is missing.",
                "Save the PAT again before using pipelines, approvals, or release evidence.",
                [CreateAction(
                    "devops-restore-pat",
                    "Restore the Azure DevOps PAT",
                    "The saved PAT reference no longer exists in Windows Credential Manager.",
                    DevOpsSection,
                    "Open DevOps settings")],
                credentialReferences);
        }

        return CreateArea(
            DevOpsSection,
            "Azure DevOps",
            DevOpsSection,
            ConfigurationCheckStatus.Ready,
            $"Azure DevOps organization '{config.Organization.Trim()}' is configured.",
            "The PAT reference is available, so pipeline and approval surfaces can authenticate.",
            credentialReferences: credentialReferences);
    }

    private ConfigurationAreaHealth BuildStorageArea(IReadOnlyList<StorageConfig> storageAccounts)
    {
        if (storageAccounts.Count == 0)
        {
            return CreateArea(
                StorageSection,
                "Storage",
                StorageSection,
                ConfigurationCheckStatus.NotConfigured,
                "No storage account is configured yet.",
                "Add at least one storage account before using blob inspection workflows.",
                [CreateAction(
                    "storage-add-account",
                    "Add a storage account",
                    "Configure either an AAD-backed account or a connection-string reference for the Storage workspace.",
                    StorageSection,
                    "Open Storage settings")]);
        }

        var credentialReferences = new List<CredentialReferenceHealth>();
        var aadAccountCount = 0;
        var invalidAccountCount = 0;

        foreach (var account in storageAccounts)
        {
            if (account.UseAad)
            {
                aadAccountCount++;
                if (string.IsNullOrWhiteSpace(account.AccountName))
                {
                    invalidAccountCount++;
                }

                continue;
            }

            var referenceKey = account.ConnectionStringRef?.Trim();
            var isPresent = !string.IsNullOrWhiteSpace(referenceKey) && HasCredential(referenceKey);
            if (!isPresent)
            {
                invalidAccountCount++;
            }

            credentialReferences.Add(new CredentialReferenceHealth(
                DisplayStorageAccount(account),
                CredentialReferenceSource.CredentialStore,
                referenceKey,
                isPresent,
                isPresent
                    ? "Connection-string reference is available in Windows Credential Manager."
                    : "Connection-string reference is missing from Windows Credential Manager."));
        }

        if (invalidAccountCount > 0)
        {
            return CreateArea(
                StorageSection,
                "Storage",
                StorageSection,
                ConfigurationCheckStatus.Warning,
                $"{storageAccounts.Count} storage account(s) are configured, but {invalidAccountCount} account prerequisite(s) need attention.",
                "Repair missing account names or connection-string references before relying on the Storage workspace.",
                [CreateAction(
                    "storage-fix-account",
                    "Repair storage prerequisites",
                    "One or more storage accounts are missing an account name or a usable connection-string reference.",
                    StorageSection,
                    "Open Storage settings")],
                credentialReferences);
        }

        if (aadAccountCount > 0)
        {
            return CreateArea(
                StorageSection,
                "Storage",
                StorageSection,
                ConfigurationCheckStatus.Configured,
                $"{storageAccounts.Count} storage account(s) are configured.",
                aadAccountCount == storageAccounts.Count
                    ? "The configured accounts rely on Azure CLI / DefaultAzureCredential at runtime."
                    : "Connection-string-backed accounts are ready, while AAD-backed accounts still rely on Azure CLI / DefaultAzureCredential at runtime.",
                credentialReferences: credentialReferences);
        }

        return CreateArea(
            StorageSection,
            "Storage",
            StorageSection,
            ConfigurationCheckStatus.Ready,
            $"{storageAccounts.Count} storage account(s) are configured.",
            "Connection-string references are available, so blob inspection can start immediately.",
            credentialReferences: credentialReferences);
    }

    private static ConfigurationAreaHealth BuildObservabilityArea(ObservabilityConfig? config)
    {
        var selectedResource = string.IsNullOrWhiteSpace(config?.SelectedResourceName)
            ? null
            : config!.SelectedResourceName.Trim();

        return CreateArea(
            ObservabilitySection,
            "Observability",
            ObservabilitySection,
            ConfigurationCheckStatus.Configured,
            "Observability uses Azure CLI / DefaultAzureCredential for Application Insights access.",
            selectedResource is null
                ? "Run az login outside the app before browsing resources if discovery comes back empty."
                : $"Last selected resource: {selectedResource}. Azure CLI / DefaultAzureCredential still controls live access.");
    }

    private static ConfigurationAreaHealth BuildIncidentTimelineArea(
        IncidentTimelineConfig config,
        ConfigurationAreaHealth aksArea,
        ConfigurationAreaHealth serviceBusArea,
        ConfigurationAreaHealth devOpsArea)
    {
        var mappingCount = config.WorkloadMappings.Count;
        if (mappingCount == 0)
        {
            return CreateArea(
                IncidentTimelineSection,
                "Incident Timeline",
                IncidentTimelineSection,
                ConfigurationCheckStatus.NotConfigured,
                "No workload mappings are configured yet.",
                "Add at least one workload mapping so incident investigations can correlate non-AKS evidence safely.",
                [CreateAction(
                    "incident-timeline-add-mapping",
                    "Add an incident mapping",
                    "Configure a workload mapping so Incident Timeline can correlate App Insights, Service Bus, and DevOps evidence.",
                    IncidentTimelineSection,
                    "Open Incident Timeline settings")]);
        }

        var dependencyGaps = new List<string>();
        if (aksArea.Status == ConfigurationCheckStatus.NotConfigured)
        {
            dependencyGaps.Add("AKS defaults");
        }

        if (config.WorkloadMappings.Any(mapping => mapping.ServiceBusEntities.Count > 0)
            && serviceBusArea.Status is ConfigurationCheckStatus.NotConfigured or ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error)
        {
            dependencyGaps.Add("Service Bus namespace credentials");
        }

        if (config.WorkloadMappings.Any(mapping => mapping.DevOps is not null)
            && devOpsArea.Status is ConfigurationCheckStatus.NotConfigured or ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error)
        {
            dependencyGaps.Add("Azure DevOps connection");
        }

        if (dependencyGaps.Count > 0)
        {
            return CreateArea(
                IncidentTimelineSection,
                "Incident Timeline",
                IncidentTimelineSection,
                ConfigurationCheckStatus.Warning,
                $"{mappingCount} workload mapping(s) are configured, but {JoinHumanList(dependencyGaps)} still need attention.",
                "Incident investigations stay evidence-first, but these base settings must exist before non-AKS signals can load.");
        }

        return CreateArea(
            IncidentTimelineSection,
            "Incident Timeline",
            IncidentTimelineSection,
            ConfigurationCheckStatus.Configured,
            $"{mappingCount} workload mapping(s) are configured.",
            "Incident investigations can seed workload scope and preserve evidence provenance without mutating settings automatically.");
    }

    private static bool IsFirstRun(ConfigurationHealthContext context)
    {
        var hasLocalSetup = context.ServiceBusNamespaces.Count > 0
            || context.Config.AksConfig is not null
            || context.Config.RedisConfig?.ActiveCache is not null
            || !string.IsNullOrWhiteSpace(context.Config.DevOpsConfig?.Organization)
            || context.Config.StorageAccounts.Count > 0
            || context.Config.IncidentTimeline.WorkloadMappings.Count > 0;

        return !hasLocalSetup;
    }

    private static ConfigurationCheckStatus ComputeOverallStatus(
        ConfigurationHealthContext context,
        IReadOnlyList<ConfigurationAreaHealth> areas,
        bool isFirstRun)
    {
        if (context.HasProfileLoadFailure || areas.Any(area => area.Status == ConfigurationCheckStatus.Error))
        {
            return ConfigurationCheckStatus.Error;
        }

        if (areas.Any(area => area.Status == ConfigurationCheckStatus.Warning))
        {
            return ConfigurationCheckStatus.Warning;
        }

        if (isFirstRun)
        {
            return ConfigurationCheckStatus.NotConfigured;
        }

        if (areas.Any(area => area.Status == ConfigurationCheckStatus.NotConfigured))
        {
            return ConfigurationCheckStatus.Configured;
        }

        return areas.All(area => area.Status == ConfigurationCheckStatus.Ready)
            ? ConfigurationCheckStatus.Ready
            : ConfigurationCheckStatus.Configured;
    }

    private static string BuildOverallSummary(
        ConfigurationHealthContext context,
        IReadOnlyList<ConfigurationAreaHealth> areas,
        IReadOnlyList<ConfigurationActionItem> actionItems,
        bool isFirstRun,
        ConfigurationCheckStatus overallStatus)
    {
        if (context.HasProfileLoadFailure)
        {
            return string.IsNullOrWhiteSpace(context.ProfilePersistenceBlockedMessage)
                ? "Profile loading failed. Readiness reflects in-memory state only until profiles.json is repaired."
                : $"Profile loading failed. {context.ProfilePersistenceBlockedMessage}";
        }

        if (isFirstRun)
        {
            return "Start with Service Bus, AKS, Redis, DevOps, or Storage settings to seed the operator workspaces. Incident mappings can follow once the base surfaces exist.";
        }

        var readyCount = areas.Count(area => area.Status == ConfigurationCheckStatus.Ready);
        var configuredCount = areas.Count(area => area.Status == ConfigurationCheckStatus.Configured);
        var warningCount = areas.Count(area => area.Status is ConfigurationCheckStatus.Warning or ConfigurationCheckStatus.Error);
        var missingCount = areas.Count(area => area.Status == ConfigurationCheckStatus.NotConfigured);

        return overallStatus switch
        {
            ConfigurationCheckStatus.Warning => $"{warningCount} capability area(s) need attention. {actionItems.Count} setup step(s) still block full readiness.",
            ConfigurationCheckStatus.Ready => "All capability areas report local prerequisites as ready.",
            _ when missingCount > 0 => $"{readyCount} capability area(s) are ready, {configuredCount} are configured, and {missingCount} still need setup.",
            _ => $"{readyCount} capability area(s) are ready. Remaining areas rely on external identities or runtime context rather than missing local configuration."
        };
    }

    private bool HasCredential(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && !string.IsNullOrWhiteSpace(credentialStore.Get(key));

    private static string DisplayServiceBusNamespace(ServiceBusNamespace namespaceConfig) =>
        !string.IsNullOrWhiteSpace(namespaceConfig.Alias)
            ? namespaceConfig.Alias.Trim()
            : namespaceConfig.FullyQualifiedNamespace.Trim();

    private static string DisplayStorageAccount(StorageConfig account) =>
        !string.IsNullOrWhiteSpace(account.DisplayName)
            ? account.DisplayName.Trim()
            : string.IsNullOrWhiteSpace(account.AccountName)
                ? "Storage account"
                : account.AccountName.Trim();

    private static string JoinHumanList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
    };

    private static ConfigurationActionItem CreateAction(
        string key,
        string title,
        string summary,
        string settingsSection,
        string actionLabel) =>
        new(key, title, summary, settingsSection, actionLabel);

    private static ConfigurationAreaHealth CreateArea(
        string areaKey,
        string title,
        string settingsSection,
        ConfigurationCheckStatus status,
        string summary,
        string? detail = null,
        IReadOnlyList<ConfigurationActionItem>? actionItems = null,
        IReadOnlyList<CredentialReferenceHealth>? credentialReferences = null) =>
        new(
            areaKey,
            title,
            settingsSection,
            status,
            summary,
            detail,
            actionItems ?? [],
            credentialReferences ?? []);
}