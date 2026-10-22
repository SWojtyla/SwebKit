using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="IMonitoringConnectionPool"/>. Resolves the same AKS /
/// Service Bus / Redis clients the monitoring signal sources need, using the exact resolution
/// path the REST endpoints use (ProfileRepository + DemoModeService + the client factories), so
/// a rule configured in the UI evaluates against the same backend the pages talk to.
/// </summary>
/// <remarks>
/// The pool owns every client it caches and disposes it on eviction — a cached Redis client wraps a
/// <c>ConnectionMultiplexer</c> with live sockets, so simply <c>Clear()</c>-ing the dictionary (the
/// previous behaviour) leaked one connection per invalidation. Demo-mode clients are deliberately
/// <em>not</em> cached here: <see cref="DemoModeService"/> hands out long-lived singletons it
/// disposes itself, and caching them would make this pool dispose something it does not own.
/// <para>Each cache is a <see cref="SingleFlightCache{T}"/>: concurrent cold misses for one key
/// share a single build. Building an AKS client runs kubelogin/<c>az</c> synchronously, so the
/// alert engine and the UI's first page load each building the same client — and the loser
/// disposing its copy — used to cost seconds of duplicate work on startup.</para>
/// </remarks>
public sealed class SidecarMonitoringConnectionPool : IMonitoringConnectionPool
{
    private readonly ProfileRepository _profile;
    private readonly DemoModeService _demo;
    private readonly IAksClientFactory _aksFactory;
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly IRedisClientFactory _redisFactory;
    private readonly ILogger<SidecarMonitoringConnectionPool> _logger;

    private readonly SingleFlightCache<IAksClient> _aksCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SingleFlightCache<IServiceBusClient> _sbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SingleFlightCache<IRedisClient> _redisCache = new(StringComparer.OrdinalIgnoreCase);

    public SidecarMonitoringConnectionPool(
        ProfileRepository profile,
        DemoModeService demo,
        IAksClientFactory aksFactory,
        IServiceBusClientFactory sbFactory,
        IRedisClientFactory redisFactory,
        ILogger<SidecarMonitoringConnectionPool> logger)
    {
        _profile = profile;
        _demo = demo;
        _aksFactory = aksFactory;
        _sbFactory = sbFactory;
        _redisFactory = redisFactory;
        _logger = logger;
    }

    public IAksClient? GetAksClient() => GetAksClient(null);

    public string? ResolveAksContext(string? context)
    {
        if (!string.IsNullOrWhiteSpace(context))
            return context;
        if (_demo.IsDemoMode)
            // The demo "current" context — GetContextsAsync marks aks-ecommerce-dev IsCurrent.
            return "aks-ecommerce-dev";
        return _profile.GetProfileData().Config.AksConfig?.KubeconfigContext;
    }

    public IAksClient? GetAksClient(string? context)
    {
        if (_demo.IsDemoMode)
            return _demo.GetAksClient(context);

        var aksConfig = _profile.GetProfileData().Config.AksConfig;
        if (aksConfig is null)
            return null;

        // The requested context must drive the factory, not just the cache key: previously the
        // key said "other" while the client was still built for the profile's configured context,
        // so explicit-context callers (context switch test, per-rule monitoring) silently talked
        // to the wrong cluster.
        // Empty string means "configured context" too — rules persist kubeconfigContext as ""
        // (the dialog's "Configured context" option), and passing "" through would build a client
        // for the kubeconfig's *current* context instead (BuildClientConfiguration treats
        // whitespace as "no explicit context"), silently evaluating the rule against a different
        // cluster than the pages show.
        var effectiveContext = string.IsNullOrWhiteSpace(context) ? aksConfig.KubeconfigContext : context;
        return _aksCache.GetOrCreate(
            effectiveContext ?? "default",
            () => _aksFactory.Create(effectiveContext, aksConfig.KubeconfigPath));
    }

    public IServiceBusClient? GetServiceBusClient(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        if (_demo.IsDemoMode)
        {
            var demoNs = _demo.GetDemoNamespaces()
                .FirstOrDefault(n => string.Equals(n.Alias, alias, StringComparison.OrdinalIgnoreCase)
                                  || n.Id.ToString("N") == alias);
            return demoNs is null ? null : _demo.GetSbClient(demoNs);
        }

        var ns = _profile.ServiceBusNamespaces
            .FirstOrDefault(n => string.Equals(n.Alias, alias, StringComparison.OrdinalIgnoreCase)
                              || n.Id.ToString("N") == alias);
        if (ns is null)
            return null;

        return _sbCache.GetOrCreate(
            alias,
            () => ns.AuthMode == SbAuthMode.ConnectionString
                ? _sbFactory.Create(ns.CredentialKey, ns.TransportType)
                : _sbFactory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType));
    }

    public async ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        if (_demo.IsDemoMode)
        {
            var demoCache = _demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId);
            if (demoCache is null || !string.Equals(demoCache.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                return null;
            return _demo.GetRedisClient(demoCache);
        }

        var config = _profile.GetProfileData().Config.RedisConfig;
        config?.EnsureMigrated();
        var cache = config?.Caches.FirstOrDefault(c =>
            string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)
            || c.Id == displayName);
        if (cache is null)
            return null;
        // The shared build runs uncancelled (other callers may be waiting on it); ct bounds only this wait.
        return await _redisCache
            .GetOrCreateAsync(displayName, () => _redisFactory.CreateAsync(cache, CancellationToken.None), ct)
            .ConfigureAwait(false);
    }

    public void InvalidateStaleConnections()
    {
        DrainAndDispose(_aksCache, "AKS");
        DrainAndDispose(_sbCache, "Service Bus");
        DrainAndDispose(_redisCache, "Redis");
    }

    public void EvictServiceBusClient(string alias)
    {
        if (!string.IsNullOrWhiteSpace(alias) && _sbCache.TryRemove(alias, out var build))
            DisposeWhenBuilt(build, "Service Bus", alias);
    }

    public void EvictAksClients() => DrainAndDispose(_aksCache, "AKS");

    public void EvictRedisClient(string key)
    {
        if (!string.IsNullOrWhiteSpace(key) && _redisCache.TryRemove(key, out var build))
            DisposeWhenBuilt(build, "Redis", key);
    }

    public ValueTask DisposeAsync()
    {
        InvalidateStaleConnections();
        return ValueTask.CompletedTask;
    }

    /// <summary>Removes every entry and disposes it — now if built, or when its in-flight build
    /// completes. Removal is per key, so exactly one caller disposes each client even under
    /// concurrent invalidation.</summary>
    private void DrainAndDispose<T>(SingleFlightCache<T> cache, string kind)
        where T : class
    {
        foreach (var (key, build) in cache.RemoveAll())
            DisposeWhenBuilt(build, kind, key);
    }

    private void DisposeWhenBuilt<T>(Task<T> build, string kind, string key)
        where T : class =>
        SingleFlightCache.WhenBuilt(build, client => DisposeEntry(client, kind, key));

    private void DisposeEntry(object? client, string kind, string key)
    {
        switch (client)
        {
            case null:
                return;

            case IDisposable sync:
                try
                {
                    sync.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose pooled {Kind} client '{Key}'", kind, key);
                }
                return;

            case IAsyncDisposable async:
                // InvalidateStaleConnections/EvictServiceBusClient are synchronous on
                // IMonitoringConnectionPool, and blocking a caller on an async SDK teardown would
                // just move the stall somewhere else — so an async-only client is torn down on a
                // detached task whose failures are still logged.
                _ = DisposeAsyncSafeAsync(async, kind, key);
                return;
        }
    }

    private async Task DisposeAsyncSafeAsync(IAsyncDisposable client, string kind, string key)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose pooled {Kind} client '{Key}'", kind, key);
        }
    }
}
