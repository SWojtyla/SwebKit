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
    IRedisWarmupCache redisCache) : IConnectionWarmupService
{
    private const int PerAreaTimeoutSeconds = 10;

    public async Task WarmAsync(IReadOnlyList<string> priorityAreas, CancellationToken ct = default)
    {
        if (!userSettings.Settings.WarmupConnectionsOnStartup)
            return;

        var tasks = BuildWarmupTasks(priorityAreas, ct);
        if (tasks.Count == 0)
            return;

        await Task.WhenAll(tasks);
    }

    public void InvalidateCaches()
    {
        aksCache.Invalidate();
        redisCache.Invalidate();
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
            // Timeout or app-level cancellation — silently discard
        }
        catch (Exception)
        {
            // Network, auth, or config error — silently discard
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
            // Silently discard
        }
        catch (Exception)
        {
            // Silently discard
        }
    }
}
