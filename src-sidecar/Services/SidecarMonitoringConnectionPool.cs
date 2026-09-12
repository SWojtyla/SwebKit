using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
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
/// </remarks>
public sealed class SidecarMonitoringConnectionPool : IMonitoringConnectionPool
{
    private readonly ProfileRepository _profile;
    private readonly DemoModeService _demo;
    private readonly IAksClientFactory _aksFactory;
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly IRedisClientFactory _redisFactory;
    private readonly ILogger<SidecarMonitoringConnectionPool> _logger;

    private readonly ConcurrentDictionary<string, IAksClient> _aksCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IServiceBusClient> _sbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IRedisClient> _redisCache = new(StringComparer.OrdinalIgnoreCase);

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

    public IAksClient? GetAksClient(string? context)
    {
        if (_demo.IsDemoMode)
            return _demo.GetAksClient();

        var aksConfig = _profile.GetProfileData().Config.AksConfig;
        if (aksConfig is null)
            return null;

        var key = context ?? aksConfig.KubeconfigContext ?? "default";
        return GetOrCreate(
            _aksCache,
            key,
            () => _aksFactory.Create(aksConfig.KubeconfigContext, aksConfig.KubeconfigPath),
            "AKS");
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

        return GetOrCreate(
            _sbCache,
            alias,
            () => ns.AuthMode == SbAuthMode.ConnectionString
                ? _sbFactory.Create(ns.CredentialKey, ns.TransportType)
                : _sbFactory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType),
            "Service Bus");
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
        if (_redisCache.TryGetValue(displayName, out var cached))
            return cached;

        var created = await _redisFactory.CreateAsync(cache, ct).ConfigureAwait(false);
        return Publish(_redisCache, displayName, created, "Redis");
    }

    public void InvalidateStaleConnections()
    {
        DrainAndDispose(_aksCache, "AKS");
        DrainAndDispose(_sbCache, "Service Bus");
        DrainAndDispose(_redisCache, "Redis");
    }

    public void EvictServiceBusClient(string alias)
    {
        if (!string.IsNullOrWhiteSpace(alias) && _sbCache.TryRemove(alias, out var client))
            DisposeEntry(client, "Service Bus", alias);
    }

    public ValueTask DisposeAsync()
    {
        InvalidateStaleConnections();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Caches one client per key without ever leaking a second one. <c>ConcurrentDictionary</c> has
    /// no atomic "create exactly once" primitive — <c>GetOrAdd</c>'s factory overload can run on two
    /// threads at once and silently discard one result — so the client is built outside the
    /// dictionary and the loser of the race is disposed rather than orphaned.
    /// </summary>
    private T GetOrCreate<T>(ConcurrentDictionary<string, T> cache, string key, Func<T> factory, string kind)
        where T : class
    {
        if (cache.TryGetValue(key, out var existing))
            return existing;

        return Publish(cache, key, factory(), kind);
    }

    /// <summary>Adds <paramref name="created"/> unless another thread got there first, in which case
    /// the loser is disposed and the winner returned.</summary>
    private T Publish<T>(ConcurrentDictionary<string, T> cache, string key, T created, string kind)
        where T : class
    {
        var winner = cache.GetOrAdd(key, created);
        if (!ReferenceEquals(winner, created))
            DisposeEntry(created, kind, key);
        return winner;
    }

    /// <summary>Removes every entry and disposes it. <c>TryRemove</c> (rather than <c>Clear</c>)
    /// guarantees exactly one caller disposes each client even under concurrent invalidation.</summary>
    private void DrainAndDispose<T>(ConcurrentDictionary<string, T> cache, string kind)
        where T : class
    {
        foreach (var key in cache.Keys.ToArray())
        {
            if (cache.TryRemove(key, out var client))
                DisposeEntry(client, kind, key);
        }
    }

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
