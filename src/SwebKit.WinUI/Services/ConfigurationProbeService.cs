using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.Services;

public sealed class ConfigurationProbeService(
    IAksClientBootstrapper aksBootstrapper,
    IServiceBusNamespaceBootstrapper serviceBusBootstrapper,
    IDevOpsClientFactory devOpsClientFactory,
    IStorageClientFactory storageClientFactory,
    IRedisClientFactory redisClientFactory,
    IObservabilityResourceDiscovery observabilityDiscovery,
    IObservabilityProviderFactory observabilityProviderFactory,
    ICredentialStore credentialStore,
    ILogger<ConfigurationProbeService> logger) : IConfigurationProbeService
{
    private const string ServiceBusSection = "servicebus";
    private const string AksSection = "aks";
    private const string RedisSection = "redis";
    private const string DevOpsSection = "devops";
    private const string StorageSection = "storage";
    private const string ObservabilitySection = "observability";

    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly object _snapshotLock = new();

    private string? _fingerprint;
    private ConfigurationProbeSnapshot? _snapshot;

    public ConfigurationProbeSnapshot? GetLatest(ConfigurationHealthContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fingerprint = ComputeFingerprint(context);
        lock (_snapshotLock)
        {
            return string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal)
                ? _snapshot
                : null;
        }
    }

    public async Task<ConfigurationProbeSnapshot> RunAsync(ConfigurationHealthContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.UseDemoData)
        {
            var now = DateTimeOffset.UtcNow;
            var emptySnapshot = new ConfigurationProbeSnapshot(now, now, new Dictionary<string, ConfigurationAreaProbeResult>(StringComparer.Ordinal));
            lock (_snapshotLock)
            {
                _fingerprint = ComputeFingerprint(context);
                _snapshot = emptySnapshot;
            }

            return emptySnapshot;
        }

        var fingerprint = ComputeFingerprint(context);
        await _runLock.WaitAsync(ct);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var probeTasks = BuildProbeTasks(context, ct);
            var probeResults = probeTasks.Count == 0
                ? []
                : await Task.WhenAll(probeTasks);

            var snapshot = new ConfigurationProbeSnapshot(
                startedAt,
                DateTimeOffset.UtcNow,
                probeResults.ToDictionary(result => result.AreaKey, StringComparer.Ordinal));

            lock (_snapshotLock)
            {
                _fingerprint = fingerprint;
                _snapshot = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _runLock.Release();
        }
    }

    public void Invalidate()
    {
        lock (_snapshotLock)
        {
            _fingerprint = null;
            _snapshot = null;
        }
    }

    private List<Task<ConfigurationAreaProbeResult>> BuildProbeTasks(ConfigurationHealthContext context, CancellationToken ct)
    {
        var tasks = new List<Task<ConfigurationAreaProbeResult>>();

        if (CanProbeServiceBus(context.ServiceBusNamespaces))
        {
            tasks.Add(RunWithBudgetAsync(ServiceBusSection, token => ProbeServiceBusAsync(context.ServiceBusNamespaces, token), ct));
        }

        if (CanProbeAks(context.Config.AksConfig))
        {
            tasks.Add(RunWithBudgetAsync(AksSection, token => ProbeAksAsync(context.Config.AksConfig!, token), ct));
        }

        if (CanProbeRedis(context.Config.RedisConfig))
        {
            tasks.Add(RunWithBudgetAsync(RedisSection, token => ProbeRedisAsync(context.Config.RedisConfig!.ActiveCache!, token), ct));
        }

        if (CanProbeDevOps(context.Config.DevOpsConfig))
        {
            tasks.Add(RunWithBudgetAsync(DevOpsSection, token => ProbeDevOpsAsync(context.Config.DevOpsConfig!, token), ct));
        }

        if (CanProbeStorage(context.Config.StorageAccounts))
        {
            tasks.Add(RunWithBudgetAsync(StorageSection, token => ProbeStorageAsync(context.Config.StorageAccounts, token), ct));
        }

        tasks.Add(RunWithBudgetAsync(ObservabilitySection, token => ProbeObservabilityAsync(context.Config.ObservabilityConfig, token), ct));

        return tasks;
    }

    private async Task<ConfigurationAreaProbeResult> RunWithBudgetAsync(
        string areaKey,
        Func<CancellationToken, Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)>> probe,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var started = Stopwatch.StartNew();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(ProbeBudget);

        try
        {
            var result = await probe(probeCts.Token);
            return new ConfigurationAreaProbeResult(
                areaKey,
                result.Status,
                result.Summary,
                result.Detail,
                startedAt,
                started.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ConfigurationAreaProbeResult(
                areaKey,
                ConfigurationCheckStatus.Warning,
                "The live check timed out before it could verify runtime access.",
                "Retry the check. If it keeps timing out, confirm local network access and identity prompts outside the app.",
                startedAt,
                started.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Readiness live check failed for {AreaKey}", areaKey);
            return new ConfigurationAreaProbeResult(
                areaKey,
                ConfigurationCheckStatus.Warning,
                "The live check could not verify runtime access.",
                ex.Message,
                startedAt,
                started.Elapsed);
        }
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeServiceBusAsync(
        IReadOnlyList<ServiceBusNamespace> namespaces,
        CancellationToken ct)
    {
        var attempts = await Task.WhenAll(namespaces.Select(namespaceConfig => ProbeServiceBusNamespaceAsync(namespaceConfig, ct)));
        var failures = attempts.Where(attempt => !attempt.Success).ToList();

        if (failures.Count > 0)
        {
            return (
                ConfigurationCheckStatus.Warning,
                $"{failures.Count} of {attempts.Length} Service Bus namespace live check(s) failed.",
                string.Join(" ", failures.Select(failure => $"{failure.Label}: {failure.Detail}")));
        }

        return (
            ConfigurationCheckStatus.Ready,
            attempts.Length == 1
                ? $"Service Bus live access succeeded for '{attempts[0].Label}'."
                : $"Service Bus live access succeeded for all {attempts.Length} configured namespaces.",
            "The connection test uses the same read-only listing path as the namespace workspace.");
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeAksAsync(AksConfig config, CancellationToken ct)
    {
        var bootstrapResult = await aksBootstrapper.BootstrapAsync(new AksClientBootstrapRequest(
            ClientOverride: null,
            UseDemoData: false,
            Config: config,
            RequestedContext: config.KubeconfigContext,
            RequestedNamespace: config.DefaultNamespace), ct);

        if (bootstrapResult.Status != AksClientBootstrapStatus.Connected || bootstrapResult.Client is null)
        {
            return (
                ConfigurationCheckStatus.Warning,
                "The AKS live check could not open the configured kubecontext.",
                bootstrapResult.ErrorMessage ?? "Verify the kubeconfig path, context, and Kubernetes auth outside the app.");
        }

        var connected = await bootstrapResult.Client.TestConnectionAsync(ct);
        if (!connected)
        {
            return (
                ConfigurationCheckStatus.Warning,
                "The AKS live check could not reach the cluster API.",
                $"Context '{bootstrapResult.ActiveContext}' and namespace '{bootstrapResult.CurrentNamespace}' loaded, but the cluster API did not respond.");
        }

        return (
            ConfigurationCheckStatus.Ready,
            $"AKS live access succeeded for namespace '{bootstrapResult.CurrentNamespace}'.",
            string.IsNullOrWhiteSpace(bootstrapResult.ActiveContext)
                ? "The current kubecontext responded to a read-only namespace listing."
                : $"Kubecontext '{bootstrapResult.ActiveContext}' responded to a read-only namespace listing.");
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeRedisAsync(RedisCacheEntry cacheEntry, CancellationToken ct)
    {
        using var client = await redisClientFactory.CreateAsync(cacheEntry, ct);
        var connected = await client.TestConnectionAsync(ct);
        if (!connected)
        {
            return (
                ConfigurationCheckStatus.Warning,
                $"Redis live access failed for '{cacheEntry.DisplayName}'.",
                "The cache connection string was accepted locally, but the server did not respond to a ping.");
        }

        return (
            ConfigurationCheckStatus.Ready,
            $"Redis live access succeeded for '{cacheEntry.DisplayName}'.",
            $"The configured cache responded to a read-only ping on database {cacheEntry.Database}.");
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeDevOpsAsync(DevOpsConfig config, CancellationToken ct)
    {
        var client = devOpsClientFactory.Create(config);
        var connected = await client.TestConnectionAsync(ct);
        if (!connected)
        {
            return (
                ConfigurationCheckStatus.Warning,
                $"Azure DevOps live access failed for '{config.Organization.Trim()}'.",
                "Verify the PAT scope, organization URL, and network access before retrying.");
        }

        return (
            ConfigurationCheckStatus.Ready,
            $"Azure DevOps live access succeeded for '{config.Organization.Trim()}'.",
            "The current PAT completed a read-only projects query.");
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeStorageAsync(
        IReadOnlyList<StorageConfig> accounts,
        CancellationToken ct)
    {
        var attempts = await Task.WhenAll(accounts.Select(account => ProbeStorageAccountAsync(account, ct)));
        var failures = attempts.Where(attempt => !attempt.Success).ToList();

        if (failures.Count > 0)
        {
            return (
                ConfigurationCheckStatus.Warning,
                $"{failures.Count} of {attempts.Length} storage account live check(s) failed.",
                string.Join(" ", failures.Select(failure => $"{failure.Label}: {failure.Detail}")));
        }

        return (
            ConfigurationCheckStatus.Ready,
            attempts.Length == 1
                ? $"Storage live access succeeded for '{attempts[0].Label}'."
                : $"Storage live access succeeded for all {attempts.Length} configured accounts.",
            "Each account completed a read-only container listing.");
    }

    private async Task<(ConfigurationCheckStatus Status, string Summary, string? Detail)> ProbeObservabilityAsync(
        ObservabilityConfig? config,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config?.SelectedResourceId))
        {
            var provider = observabilityProviderFactory.Create(config.SelectedResourceId, useDemoData: false);
            await provider.RunQueryAsync("requests | take 1", TimeRange.LastHour, 1, ct);

            return (
                ConfigurationCheckStatus.Ready,
                string.IsNullOrWhiteSpace(config.SelectedResourceName)
                    ? "Observability live access succeeded for the selected Application Insights resource."
                    : $"Observability live access succeeded for '{config.SelectedResourceName}'.",
                "The current Azure CLI identity completed a read-only Application Insights query.");
        }

        await foreach (var resource in observabilityDiscovery.DiscoverResourcesAsync(ct))
        {
            return (
                ConfigurationCheckStatus.Ready,
                $"Observability discovery succeeded for '{resource.Name}'.",
                "The current Azure CLI identity discovered at least one accessible Application Insights resource.");
        }

        return (
            ConfigurationCheckStatus.Warning,
            "Observability discovery returned no Application Insights resources.",
            "Run az login or verify Reader access to at least one Application Insights resource before retrying.");
    }

    private async Task<ProbeAttempt> ProbeServiceBusNamespaceAsync(ServiceBusNamespace namespaceConfig, CancellationToken ct)
    {
        var connectionResult = await serviceBusBootstrapper.ConnectAsync(namespaceConfig, ct);
        if (connectionResult.Client is null)
        {
            return new ProbeAttempt(DisplayServiceBusNamespace(namespaceConfig), false, connectionResult.ConnectionError ?? "Connection failed.");
        }

        try
        {
            return new ProbeAttempt(DisplayServiceBusNamespace(namespaceConfig), true, null);
        }
        finally
        {
            switch (connectionResult.Client)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private async Task<ProbeAttempt> ProbeStorageAccountAsync(StorageConfig account, CancellationToken ct)
    {
        var client = storageClientFactory.Create(account);
        var connected = await client.TestConnectionAsync(ct);
        return connected
            ? new ProbeAttempt(DisplayStorageAccount(account), true, null)
            : new ProbeAttempt(DisplayStorageAccount(account), false, "The blob service did not respond to a container listing.");
    }

    private bool CanProbeServiceBus(IReadOnlyList<ServiceBusNamespace> namespaces) =>
        namespaces.Count > 0 && namespaces.All(namespaceConfig => HasCredential(namespaceConfig.CredentialKey));

    private static bool CanProbeAks(AksConfig? config) =>
        config is not null && !string.IsNullOrWhiteSpace(config.DefaultNamespace);

    private static bool CanProbeRedis(RedisConfig? config)
    {
        config?.EnsureMigrated();
        return config?.ActiveCache is { ConnectionString.Length: > 0 };
    }

    private bool CanProbeDevOps(DevOpsConfig? config) =>
        config is not null
        && !string.IsNullOrWhiteSpace(config.Organization)
        && !string.IsNullOrWhiteSpace(config.PatCredentialKey)
        && HasCredential(config.PatCredentialKey);

    private bool CanProbeStorage(IReadOnlyList<StorageConfig> accounts) =>
        accounts.Count > 0 && accounts.All(account => account.UseAad
            ? !string.IsNullOrWhiteSpace(account.AccountName)
            : HasCredential(account.ConnectionStringRef));

    private bool HasCredential(string? key) =>
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

    private static string ComputeFingerprint(ConfigurationHealthContext context)
    {
        var fingerprintShape = new
        {
            context.UseDemoData,
            ServiceBusNamespaces = context.ServiceBusNamespaces
                .Select(namespaceConfig => new
                {
                    namespaceConfig.Id,
                    namespaceConfig.Alias,
                    namespaceConfig.FullyQualifiedNamespace,
                    namespaceConfig.CredentialKey
                })
                .OrderBy(namespaceConfig => namespaceConfig.Id)
                .ToArray(),
            Aks = context.Config.AksConfig is null
                ? null
                : new
                {
                    context.Config.AksConfig.KubeconfigPath,
                    context.Config.AksConfig.KubeconfigContext,
                    context.Config.AksConfig.DefaultNamespace
                },
            Redis = context.Config.RedisConfig?.ActiveCache is null
                ? null
                : new
                {
                    context.Config.RedisConfig.ActiveCache.Id,
                    context.Config.RedisConfig.ActiveCache.DisplayName,
                    context.Config.RedisConfig.ActiveCache.Database
                },
            DevOps = context.Config.DevOpsConfig is null
                ? null
                : new
                {
                    context.Config.DevOpsConfig.Organization,
                    context.Config.DevOpsConfig.PatCredentialKey,
                    PinnedProjects = context.Config.DevOpsConfig.PinnedProjects.OrderBy(project => project).ToArray()
                },
            Storage = context.Config.StorageAccounts
                .Select(account => new
                {
                    account.Id,
                    account.DisplayName,
                    account.AccountName,
                    account.ConnectionStringRef,
                    account.UseAad
                })
                .OrderBy(account => account.Id)
                .ToArray(),
            Observability = context.Config.ObservabilityConfig is null
                ? null
                : new
                {
                    context.Config.ObservabilityConfig.SelectedResourceId,
                    context.Config.ObservabilityConfig.SelectedResourceName
                },
            IncidentTimeline = context.Config.IncidentTimeline.WorkloadMappings
                .Select(mapping => new
                {
                    mapping.Namespace,
                    mapping.WorkloadName,
                    mapping.WorkloadKind,
                    ServiceBusEntityCount = mapping.ServiceBusEntities.Count,
                    HasDevOps = mapping.DevOps is not null
                })
                .OrderBy(mapping => mapping.Namespace)
                .ThenBy(mapping => mapping.WorkloadName)
                .ToArray()
        };

        return JsonSerializer.Serialize(fingerprintShape);
    }

    private sealed record ProbeAttempt(string Label, bool Success, string? Detail);
}