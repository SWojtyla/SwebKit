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
            return _aksCache.GetOrAdd("demo", _ => _demo.GetAksClient());

        var aksConfig = _profile.GetProfileData().Config.AksConfig;
        if (aksConfig is null)
            return null;

        var key = context ?? aksConfig.KubeconfigContext ?? "default";
        return _aksCache.GetOrAdd(key, _ =>
            _aksFactory.Create(aksConfig.KubeconfigContext, aksConfig.KubeconfigPath));
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
            return demoNs is null ? null : _sbCache.GetOrAdd(alias, _ => _demo.GetSbClient(demoNs));
        }

        var ns = _profile.ServiceBusNamespaces
            .FirstOrDefault(n => string.Equals(n.Alias, alias, StringComparison.OrdinalIgnoreCase)
                              || n.Id.ToString("N") == alias);
        if (ns is null)
            return null;

        return _sbCache.GetOrAdd(alias, _ =>
            ns.AuthMode == SbAuthMode.ConnectionString
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
            return _redisCache.GetOrAdd(displayName, _ => _demo.GetRedisClient(demoCache));
        }

        var config = _profile.GetProfileData().Config.RedisConfig;
        config?.EnsureMigrated();
        var cache = config?.Caches.FirstOrDefault(c =>
            string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)
            || c.Id == displayName);
        if (cache is null)
            return null;
        if (_redisCache.TryGetValue(displayName, out var cached) && cached is not null)
            return cached;

        var entry = await _redisFactory.CreateAsync(cache, ct).ConfigureAwait(false);
        _redisCache[displayName] = entry;
        return entry;
    }

    public void InvalidateStaleConnections()
    {
        _aksCache.Clear();
        _sbCache.Clear();
        _redisCache.Clear();
    }

    public void EvictServiceBusClient(string alias)
    {
        if (!string.IsNullOrWhiteSpace(alias))
            _sbCache.TryRemove(alias, out _);
    }

    public async ValueTask DisposeAsync()
    {
        InvalidateStaleConnections();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
