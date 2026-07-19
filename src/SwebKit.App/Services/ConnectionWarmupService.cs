using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public interface IConnectionWarmupService
{
    Task WarmAsync(IReadOnlyList<string> priorityAreas, CancellationToken ct = default);
    void InvalidateCaches();
}

public sealed class ConnectionWarmupService(
    AppStateService appState,
    UserSettingsRepository userSettings,
    IAksClientBootstrapper aksBootstrapper,
    IAksWarmupCache aksCache,
    IRedisWarmupCache redisCache,
    IServiceBusNamespaceBootstrapper sbBootstrapper,
    IServiceBusWarmupCache sbCache,
    ILogger<ConnectionWarmupService>? logger = null) : IConnectionWarmupService
{
    private const int PerAreaTimeoutSeconds = 10;

    public Task WarmAsync(IReadOnlyList<string> priorityAreas, CancellationToken ct = default)
    {
        if (!userSettings.Settings.WarmupConnectionsOnStartup)
            return Task.CompletedTask;

        // Push entirely to the thread pool so that:
        // (a) the synchronous preamble of each warmup method does not run on the UI thread, and
        // (b) async continuations (when network calls complete) also stay off the UI thread.
        return Task.Run(async () =>
        {
            var tasks = BuildWarmupTasks(priorityAreas, ct);
            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);
        }, ct);
    }

    public void InvalidateCaches()
    {
        aksCache.Invalidate();
        redisCache.Invalidate();
        sbCache.Invalidate();
    }

    private List<Task> BuildWarmupTasks(IReadOnlyList<string> priorityAreas, CancellationToken ct)
    {
        var tasks = new List<Task>();

        var aksConfig = appState.Config.AksConfig;
        if (aksConfig is not null && (priorityAreas.Count == 0 || priorityAreas.Contains("aks")))
            tasks.Add(WarmAksAsync(aksConfig, ct));

        var redisCaches = appState.Config.RedisConfig?.Caches;
        if (redisCaches is { Count: > 0 } && (priorityAreas.Count == 0 || priorityAreas.Contains("redis")))
            tasks.Add(WarmRedisAsync(redisCaches, ct));

        var sbNamespaces = appState.ServiceBusNamespaces;
        if (sbNamespaces.Count > 0 && (priorityAreas.Count == 0 || priorityAreas.Contains("service-bus")))
            tasks.Add(WarmServiceBusAsync(sbNamespaces, ct));

        return tasks;
    }

    private async Task WarmAksAsync(AksConfig config, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerAreaTimeoutSeconds));
        try
        {
            var result = await aksBootstrapper.BootstrapAsync(
                new AksClientBootstrapRequest(
                    ClientOverride: null,
                    UseDemoData: false,
                    Config: config,
                    RequestedContext: config.KubeconfigContext,
                    RequestedNamespace: string.IsNullOrWhiteSpace(config.DefaultNamespace)
                        ? "default"
                        : config.DefaultNamespace),
                timeoutCts.Token);

            if (result.Status == AksClientBootstrapStatus.Connected && result.Client is not null)
                aksCache.Store(result);
        }
        catch (OperationCanceledException)
        {
            // Timeout or app-level cancellation — expected
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AKS warmup failed");
        }
    }

    private async Task WarmRedisAsync(IReadOnlyList<RedisCacheEntry> entries, CancellationToken ct)
    {
        var perEntry = entries.Select(entry => WarmRedisEntryAsync(entry, ct));
        await Task.WhenAll(perEntry);
    }

    private async Task WarmRedisEntryAsync(RedisCacheEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.ConnectionString))
            return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerAreaTimeoutSeconds));
        try
        {
            var client = await SwebKit.Redis.RedisClient.CreateAsync(entry);
            await client.TestConnectionAsync(timeoutCts.Token);
            redisCache.Store(entry.Id, client);
        }
        catch (OperationCanceledException)
        {
            // Timeout or app-level cancellation — expected
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Redis warmup failed for cache {CacheId}", entry.Id);
        }
    }

    private async Task WarmServiceBusAsync(IReadOnlyList<ServiceBusNamespace> namespaces, CancellationToken ct)
    {
        var perNs = namespaces.Select(ns => WarmServiceBusNamespaceAsync(ns, ct));
        await Task.WhenAll(perNs);
    }

    private async Task WarmServiceBusNamespaceAsync(ServiceBusNamespace ns, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerAreaTimeoutSeconds));
        try
        {
            var result = await sbBootstrapper.ConnectAsync(ns, timeoutCts.Token);
            if (result.Client is not null)
                sbCache.Store(ns.Id, result.Client);
        }
        catch (OperationCanceledException)
        {
            // Timeout or app-level cancellation — expected
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Service Bus warmup failed for namespace {NamespaceId}", ns.Id);
        }
    }
}
