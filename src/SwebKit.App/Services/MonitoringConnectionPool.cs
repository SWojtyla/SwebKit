using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

/// <summary>
/// Singleton connection pool for alert monitoring signal sources.
///
/// Resource efficiency principles:
/// <list type="bullet">
///   <item>ONE AKS <see cref="IAksClient"/> per configured kubeconfig context (shared across all AKS sources).</item>
///   <item>ONE <see cref="IServiceBusClient"/> per Service Bus namespace alias (shared across DLQ / active-depth / dead-subscription sources).</item>
///   <item>ONE <see cref="IRedisClient"/> per Redis cache display name (shared across memory and client-count sources).</item>
///   <item>Connections are established lazily and reused until configuration changes.</item>
///   <item>Stale connections (config changed or resource removed) are released on <see cref="InvalidateStaleConnections"/>.</item>
/// </list>
///
/// Signal sources must NOT dispose clients returned by this pool.
/// </summary>
public sealed class MonitoringConnectionPool : IMonitoringConnectionPool
{
    private readonly AppStateService _appState;
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAksClient;
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly ICredentialStore _credentials;
    private readonly IRedisClientFactory _redisFactory;
    private readonly ILogger<MonitoringConnectionPool> _logger;
    private readonly object _lock = new();

    // ── AKS ─────────────────────────────────────────────────────────────────
    private IAksClient? _aksClient;
    private string? _aksContext;
    private string? _aksKubeconfigPath;
    private bool _aksIsDemo;

    // Per-rule context overrides (rare — only when a rule targets a non-default context)
    private readonly Dictionary<string, IAksClient> _overrideClients
        = new(StringComparer.Ordinal);

    // ── Service Bus ──────────────────────────────────────────────────────────
    // Key = alias (case-insensitive). Value = (client, credentialKey last used).
    private readonly Dictionary<string, (IServiceBusClient Client, string CredentialKey)> _sbClients
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Redis ────────────────────────────────────────────────────────────────
    // Key = displayName (case-insensitive). Value = (client, connectionString last used for staleness check).
    private readonly Dictionary<string, (IRedisClient Client, string ConnectionString)> _redisClients
        = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public MonitoringConnectionPool(
        AppStateService appState,
        IAksClientFactory aksFactory,
        DemoAksClient demoAksClient,
        IServiceBusClientFactory sbFactory,
        ICredentialStore credentials,
        IRedisClientFactory redisFactory,
        ILogger<MonitoringConnectionPool> logger)
    {
        _appState = appState;
        _aksFactory = aksFactory;
        _demoAksClient = demoAksClient;
        _sbFactory = sbFactory;
        _credentials = credentials;
        _redisFactory = redisFactory;
        _logger = logger;

        // Invalidate when the user saves profile changes (connection strings may have changed).
        _appState.ConfigChanged += InvalidateStaleConnections;
    }

    // ── IAksClient ───────────────────────────────────────────────────────────

    public IAksClient? GetAksClient(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return GetAksClient();

        // Demo mode — DemoAksClient handles all contexts.
        if (_appState.UseDemoData)
            return GetAksClient();

        var cfg = _appState.Config.AksConfig;
        if (cfg is null)
            return null;

        // If the requested context matches the global configured context, reuse the shared client.
        if (string.Equals(context, cfg.KubeconfigContext, StringComparison.Ordinal))
            return GetAksClient();

        // Per-rule context override — create a lightweight client without caching
        // (overrides are rare, typically just a handful of distinct contexts).
        lock (_lock)
        {
            if (_overrideClients.TryGetValue(context, out var existing))
                return existing;

            var client = _aksFactory.Create(context, cfg.KubeconfigPath);
            _overrideClients[context] = client;
            _logger.LogDebug("MonitoringConnectionPool: AKS override client created for context '{Context}'", context);
            return client;
        }
    }

    public IAksClient? GetAksClient()
    {
        // Demo mode — return the shared DemoAksClient singleton (no creation needed).
        if (_appState.UseDemoData)
        {
            lock (_lock)
            {
                if (_aksIsDemo && _aksClient is not null)
                    return _aksClient;
                DisposeAksClientLocked();
                _aksClient = _demoAksClient;
                _aksIsDemo = true;
                return _aksClient;
            }
        }

        var cfg = _appState.Config.AksConfig;
        if (cfg is null)
            return null;

        lock (_lock)
        {
            if (!_aksIsDemo &&
                _aksClient is not null &&
                cfg.KubeconfigContext == _aksContext &&
                cfg.KubeconfigPath == _aksKubeconfigPath)
                return _aksClient;

            DisposeAksClientLocked();
            _aksClient = _aksFactory.Create(cfg.KubeconfigContext, cfg.KubeconfigPath);
            _aksContext = cfg.KubeconfigContext;
            _aksKubeconfigPath = cfg.KubeconfigPath;
            _aksIsDemo = false;
            _logger.LogDebug("MonitoringConnectionPool: AKS client (re)created for context '{Context}'", _aksContext);
            return _aksClient;
        }
    }

    // ── IServiceBusClient ────────────────────────────────────────────────────

    public IServiceBusClient? GetServiceBusClient(string alias)
    {
        var ns = _appState.ServiceBusNamespaces.FirstOrDefault(
            n => string.Equals(n.Alias, alias, StringComparison.OrdinalIgnoreCase));
        if (ns is null)
            return null;

        lock (_lock)
        {
            // Return cached if credential key is unchanged.
            if (_sbClients.TryGetValue(alias, out var entry) && entry.CredentialKey == ns.CredentialKey)
                return entry.Client;

            // Credential changed or first call — create a fresh client.
            _sbClients.Remove(alias);
        }

        var connStr = _credentials.Get(ns.CredentialKey);
        if (ns.AuthMode != SwebKit.Core.Domain.SbAuthMode.DefaultAzureCredential && string.IsNullOrWhiteSpace(connStr))
            return null;

        var client = ns.AuthMode == SwebKit.Core.Domain.SbAuthMode.DefaultAzureCredential
            ? _sbFactory.CreateWithEntra(ns.FullyQualifiedNamespace)
            : _sbFactory.Create(connStr!);
        lock (_lock)
        {
            _sbClients[alias] = (client, ns.CredentialKey);
        }
        _logger.LogDebug("MonitoringConnectionPool: Service Bus client (re)created for '{Alias}'", alias);
        return client;
    }

    public void EvictServiceBusClient(string alias)
    {
        lock (_lock)
        {
            _sbClients.Remove(alias);
        }
        _logger.LogDebug("MonitoringConnectionPool: Service Bus client for '{Alias}' manually evicted.", alias);
    }

    // ── IRedisClient ─────────────────────────────────────────────────────────

    public async ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default)
    {
        var cfg = _appState.Config.RedisConfig;
        if (cfg is null)
            return null;

        var cacheEntry = cfg.Caches.FirstOrDefault(
            c => string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        if (cacheEntry is null)
            return null;

        lock (_lock)
        {
            // Return cached if the connection string hasn't changed.
            if (_redisClients.TryGetValue(displayName, out var entry) &&
                entry.ConnectionString == cacheEntry.ConnectionString)
                return entry.Client;

            // Connection string changed — dispose stale client before recreating.
            if (_redisClients.TryGetValue(displayName, out var stale))
            {
                (stale.Client as IDisposable)?.Dispose();
                _redisClients.Remove(displayName);
            }
        }

        var newClient = await _redisFactory.CreateAsync(cacheEntry, ct);

        lock (_lock)
        {
            // Double-checked: another concurrent call may have won the race.
            if (_redisClients.TryGetValue(displayName, out var race))
            {
                (newClient as IDisposable)?.Dispose();
                return race.Client;
            }
            _redisClients[displayName] = (newClient, cacheEntry.ConnectionString);
        }

        _logger.LogDebug("MonitoringConnectionPool: Redis client (re)created for '{DisplayName}'", displayName);
        return newClient;
    }

    // ── Invalidation ─────────────────────────────────────────────────────────

    public void InvalidateStaleConnections()
    {
        lock (_lock)
        {
            // AKS: dispose if config has changed.
            var aksCfg = _appState.Config.AksConfig;
            if (_aksClient is not null && !_aksIsDemo && (
                aksCfg is null ||
                aksCfg.KubeconfigContext != _aksContext ||
                aksCfg.KubeconfigPath != _aksKubeconfigPath))
            {
                DisposeAksClientLocked();
                _logger.LogDebug("MonitoringConnectionPool: AKS client invalidated.");
            }

            // Invalidate per-rule context overrides (kubeconfig path may have changed)
            if (aksCfg is null || _overrideClients.Count > 0)
            {
                foreach (var (_, oc) in _overrideClients)
                    if (oc is IAsyncDisposable od) _ = od.DisposeAsync();
                _overrideClients.Clear();
            }

            // Service Bus: remove aliases no longer in config.
            var configuredAliases = _appState.ServiceBusNamespaces
                .Select(n => n.Alias)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _sbClients.Keys.Where(k => !configuredAliases.Contains(k)).ToList())
            {
                _sbClients.Remove(key);
                _logger.LogDebug("MonitoringConnectionPool: Service Bus client for '{Alias}' evicted.", key);
            }

            // Redis: remove caches no longer in config.
            var configuredCaches = (_appState.Config.RedisConfig?.Caches ?? [])
                .Select(c => c.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _redisClients.Keys.Where(k => !configuredCaches.Contains(k)).ToList())
            {
                ((_redisClients[key].Client) as IDisposable)?.Dispose();
                _redisClients.Remove(key);
                _logger.LogDebug("MonitoringConnectionPool: Redis client for '{DisplayName}' evicted.", key);
            }
        }
    }

    // ── Disposal ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _appState.ConfigChanged -= InvalidateStaleConnections;

        IAksClient? aksToDispose;
        List<IRedisClient> redisToDispose;

        lock (_lock)
        {
            aksToDispose = _aksIsDemo ? null : _aksClient; // never dispose the shared DemoAksClient
            _aksClient = null;
            redisToDispose = _redisClients.Values.Select(v => v.Client).ToList();
            _redisClients.Clear();
            _sbClients.Clear(); // Service Bus client disposal handled by Azure SDK internally
        }

        if (aksToDispose is IAsyncDisposable aksDisposable)
            await aksDisposable.DisposeAsync();

        foreach (var r in redisToDispose)
            (r as IDisposable)?.Dispose();

        // Dispose context-override AKS clients
        foreach (var (_, oc) in _overrideClients)
            if (oc is IAsyncDisposable od) await od.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void DisposeAksClientLocked()
    {
        if (!_aksIsDemo && _aksClient is IAsyncDisposable d)
            _ = d.DisposeAsync(); // fire-and-forget; we're replacing the client

        _aksClient = null;
        _aksContext = null;
        _aksKubeconfigPath = null;
        _aksIsDemo = false;
    }
}
