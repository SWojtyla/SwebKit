using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services.AccessProbes;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="IAccessReportService"/> — the Phase 2 per-environment
/// access report (docs/features/active/access-awareness-pipeline.md).
///
/// <para>Probe results are cached per <c>area|connection|capability</c> key with a 5-minute TTL.
/// Each cell is a <see cref="Lazy{T}"/> of the probe task, so a burst of report requests (or the
/// report plus a single-entry refresh) coalesces onto one SDK call per capability — never one
/// per request.</para>
///
/// <para>Probes fan out with <see cref="Task.WhenAll"/> under an external 8s per-probe timeout
/// (abandon, not just cancel — Redis connects run with <c>AbortOnConnectFail=false</c> and can
/// ignore the token). Failures classify through <see cref="AccessAdvisor"/>: a recognized authz
/// failure is <see cref="AccessStatus.Denied"/>, anything else is
/// <see cref="AccessStatus.Unknown"/> with a <see cref="ConnectionTestError"/>-sanitized summary —
/// never a false deny, never raw SDK text.</para>
///
/// <para>Every probe goes through the existing pooled clients — no SDK client is constructed
/// here (docs/pitfalls/azure-sdk.md AZ-4).</para>
/// </summary>
public sealed class AccessReportService : IAccessReportService
{
    internal static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly ProfileRepository _profile;
    private readonly DemoModeService _demo;
    private readonly IServiceBusConnectionPool _serviceBusPool;
    private readonly ISqlConnectionPool _sqlPool;
    private readonly IRedisConnectionPool _redisPool;
    private readonly IStorageConnectionPool _storagePool;
    private readonly IMonitoringConnectionPool _monitoringPool;
    private readonly IObservabilityProviderFactory _observabilityProviders;
    private readonly ILogger<AccessReportService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _probeTimeout;

    private readonly ConcurrentDictionary<string, Lazy<Task<AccessProbeResult>>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (AccessDenial Denial, DateTimeOffset RecordedAt)> _observedDenials = new(StringComparer.OrdinalIgnoreCase);

    public AccessReportService(
        ProfileRepository profile,
        DemoModeService demo,
        IServiceBusConnectionPool serviceBusPool,
        ISqlConnectionPool sqlPool,
        IRedisConnectionPool redisPool,
        IStorageConnectionPool storagePool,
        IMonitoringConnectionPool monitoringPool,
        IObservabilityProviderFactory observabilityProviders,
        ILogger<AccessReportService> logger,
        // Internal knobs so tests can exercise TTL expiry and probe timeout without real waits.
        TimeSpan? cacheTtl = null,
        TimeSpan? probeTimeout = null)
    {
        _profile = profile;
        _demo = demo;
        _serviceBusPool = serviceBusPool;
        _sqlPool = sqlPool;
        _redisPool = redisPool;
        _storagePool = storagePool;
        _monitoringPool = monitoringPool;
        _observabilityProviders = observabilityProviders;
        _logger = logger;
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
    }

    public async Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var specs = BuildSpecs();
        var results = await Task.WhenAll(specs.Select(spec => GetOrRunAsync(spec, forceRefresh)))
            .ConfigureAwait(false);

        // Group results into entries keyed (area, connection), preserving spec order.
        var entryIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entryKeys = new List<(string Area, string Connection)>();
        var entryMeta = new List<(string Label, string? Scope)>();
        var capLists = new List<List<AccessProbeResult>>();

        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var groupKey = spec.FeatureArea + "|" + spec.ConnectionKey;
            if (!entryIndex.TryGetValue(groupKey, out var slot))
            {
                slot = capLists.Count;
                entryIndex[groupKey] = slot;
                entryKeys.Add((spec.FeatureArea, spec.ConnectionKey));
                entryMeta.Add((spec.Label, spec.ScopeResourceId));
                capLists.Add([]);
            }
            capLists[slot].Add(results[i]);
        }

        var entries = new List<AccessReportEntry>(capLists.Count);
        for (var i = 0; i < capLists.Count; i++)
        {
            entries.Add(new AccessReportEntry(
                entryKeys[i].Area, entryKeys[i].Connection,
                entryMeta[i].Label, entryMeta[i].Scope, capLists[i]));
        }

        ApplyDemoOverrides(entries);
        return new AccessReport(entries, DateTimeOffset.UtcNow);
    }

    public void Invalidate(string featureArea, string? connectionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureArea);
        var prefix = connectionKey is null
            ? featureArea + "|"
            : $"{featureArea}|{connectionKey}|";
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateAll()
    {
        _cache.Clear();
        _observedDenials.Clear();
    }

    public void RecordObservedDenial(AccessDenial denial, string connectionKey)
    {
        ArgumentNullException.ThrowIfNull(denial);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);

        var key = $"{denial.FeatureArea}|{connectionKey}|{denial.Capability}";
        _observedDenials[key] = (denial, DateTimeOffset.UtcNow);
        // Flip the cached row immediately — an observed send/query denial must turn the report
        // red now, not after the TTL expires.
        _cache[key] = new Lazy<Task<AccessProbeResult>>(() => Task.FromResult(
            new AccessProbeResult(denial.FeatureArea, connectionKey, denial.Capability,
                AccessStatus.Denied, DateTimeOffset.UtcNow, Denial: denial)),
            LazyThreadSafetyMode.PublicationOnly);
    }

    public bool TryGetKnownDenial(string featureArea, string connectionKey, string capability, out AccessDenial denial)
    {
        var key = $"{featureArea}|{connectionKey}|{capability}";
        var freshSince = DateTimeOffset.UtcNow - _cacheTtl;

        if (_observedDenials.TryGetValue(key, out var recorded) && recorded.RecordedAt >= freshSince)
        {
            denial = recorded.Denial;
            return true;
        }

        // A probed denial is equally good evidence — e.g. sql.metadata classified Denied means the
        // agent's sql.query short-circuit can answer without re-running the probe. Only completed
        // cells are consulted: this lookup must never start a probe itself.
        if (_cache.TryGetValue(key, out var lazy)
            && lazy.IsValueCreated
            && lazy.Value.IsCompletedSuccessfully
            && lazy.Value.Result is { Status: AccessStatus.Denied, Denial: not null } result
            && result.CheckedAt >= freshSince)
        {
            denial = result.Denial;
            return true;
        }

        denial = null!;
        return false;
    }

    private AccessDenial? KnownDenial(string featureArea, string connectionKey, string capability) =>
        TryGetKnownDenial(featureArea, connectionKey, capability, out var denial) ? denial : null;

    /// <summary>
    /// The probe list for the current profile. Demo mode substitutes the demo overlay for the
    /// Service Bus/SQL/Redis/Storage connection lists — the same replacement
    /// <c>ConfigEndpoints.GetProfilesAsync</c> applies — so the report shows what the app would
    /// actually use.
    /// </summary>
    private List<AccessProbeSpec> BuildSpecs()
    {
        var config = _profile.GetProfileData().Config;
        var demoMode = _demo.IsDemoMode;
        var specs = new List<AccessProbeSpec>();

        // Demo-reserved ids are filtered out of the non-demo lists too — a save made while demo
        // mode was on can persist the overlay, and that copy must never resolve to a real client
        // (same rule as SqlEndpoints.ResolveConnection et al.).
        var namespaces = demoMode
            ? _demo.GetDemoNamespaces()
            : _profile.ServiceBusNamespaces
                .Where(n => n.Id != DemoModeService.DemoNamespaceId1 && n.Id != DemoModeService.DemoNamespaceId2)
                .ToList();
        specs.AddRange(ServiceBusAccessProbes.Build(
            namespaces, config.ServiceBusEntityLinks ?? [], _serviceBusPool, KnownDenial));

        IReadOnlyList<SqlConnectionEntry> sqlConnections = demoMode
            ? _demo.GetDemoSqlConnections()
            : (IReadOnlyList<SqlConnectionEntry>)(config.SqlConfig?.Connections
                .Where(c => c.Active
                    && c.Id is not (DemoModeService.DemoSqlConnectionId
                        or DemoModeService.DemoSqlConnectionId2
                        or DemoModeService.DemoSqlConnectionIdRestricted))
                .ToList() ?? []);
        specs.AddRange(SqlAccessProbes.Build(sqlConnections, _sqlPool));

        IReadOnlyList<RedisCacheEntry> redisCaches;
        if (demoMode)
        {
            redisCaches = _demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId) is { } demoCache
                ? [demoCache]
                : [];
        }
        else
        {
            config.RedisConfig?.EnsureMigrated();
            redisCaches = (config.RedisConfig?.Caches ?? [])
                .Where(c => c.Id != DemoModeService.DemoRedisCacheId)
                .ToList();
        }
        specs.AddRange(RedisAccessProbes.Build(redisCaches, _redisPool));

        IReadOnlyList<StorageConfig> storageAccounts = demoMode
            ? (_demo.GetDemoStorageConfig() is { } demoStorage ? [demoStorage] : (IReadOnlyList<StorageConfig>)[])
            : config.StorageAccounts.Where(a => a.Id != DemoModeService.DemoStorageId).ToList();
        specs.AddRange(StorageAccessProbes.Build(storageAccounts, _storagePool, _demo));

        if (AksAccessProbes.Build(config.AksConfig, _monitoringPool, demoMode) is { } aksSpec)
            specs.Add(aksSpec);
        if (ObservabilityAccessProbes.Build(config.ObservabilityConfig, _observabilityProviders, demoMode) is { } obsSpec)
            specs.Add(obsSpec);

        return specs;
    }

    /// <summary>
    /// Demo parity: <c>demo-sql-prd</c> models a locked-down PRD login (SELECT/EXECUTE but no
    /// VIEW DEFINITION). The SQL probe normally detects this via the metadata-hidden heuristic on
    /// its own; this synthesis guarantees the Denied <c>sql.metadata</c> row exists even if the
    /// probe answers differently, so demo/e2e always exercises the denied-row UI.
    /// </summary>
    private void ApplyDemoOverrides(List<AccessReportEntry> entries)
    {
        if (!_demo.IsDemoMode)
            return;

        var denial = SqlAccessProbes.MetadataHiddenDenial(
            "Demo restricted connection: SELECT/EXECUTE granted, metadata hidden.");
        var synthesized = new AccessProbeResult(
            SqlAccessProbes.Area, DemoModeService.DemoSqlConnectionIdRestricted,
            SqlAccessProbes.CapabilityMetadata, AccessStatus.Denied,
            DateTimeOffset.UtcNow, Denial: denial);

        var index = entries.FindIndex(e =>
            string.Equals(e.FeatureArea, SqlAccessProbes.Area, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ConnectionKey, DemoModeService.DemoSqlConnectionIdRestricted, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            entries.Add(new AccessReportEntry(
                SqlAccessProbes.Area, DemoModeService.DemoSqlConnectionIdRestricted,
                "orders-prd-sql (restricted)", null, [synthesized]));
            return;
        }

        var caps = entries[index].Capabilities.ToList();
        var capIndex = caps.FindIndex(c =>
            string.Equals(c.Capability, SqlAccessProbes.CapabilityMetadata, StringComparison.OrdinalIgnoreCase));
        if (capIndex < 0)
            caps.Add(synthesized);
        else
            caps[capIndex] = synthesized with { AuthMode = caps[capIndex].AuthMode };
        entries[index] = entries[index] with { Capabilities = caps };
    }

    /// <summary>
    /// Returns the cached probe for <paramref name="spec"/>, running it when absent, stale or
    /// force-refreshed. The <see cref="Lazy{T}"/> wrapper coalesces concurrent callers onto a
    /// single execution; a stale result is re-probed only by the caller that wins the removal.
    /// </summary>
    private async Task<AccessProbeResult> GetOrRunAsync(AccessProbeSpec spec, bool forceRefresh)
    {
        if (forceRefresh)
            _cache.TryRemove(spec.Key, out _);

        // Bounded against pathological churn: a cache cell that somehow stays stale after
        // re-evaluation returns its result rather than spinning.
        for (var attempts = 0; attempts < 3; attempts++)
        {
            var lazy = _cache.GetOrAdd(spec.Key, _ => new Lazy<Task<AccessProbeResult>>(
                () => RunProbeAsync(spec), LazyThreadSafetyMode.ExecutionAndPublication));

            AccessProbeResult result;
            try
            {
                result = await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                // A faulted lazy would poison every later caller — drop it so the next
                // request re-probes instead of rethrowing the same cached failure forever.
                _cache.TryRemove(new KeyValuePair<string, Lazy<Task<AccessProbeResult>>>(spec.Key, lazy));
                throw;
            }

            if (result.CheckedAt >= DateTimeOffset.UtcNow - _cacheTtl)
                return result;

            // Stale: only the thread that removes this exact lazy instance re-probes; others
            // return the stale row once rather than stampeding the SDK.
            if (_cache.TryRemove(new KeyValuePair<string, Lazy<Task<AccessProbeResult>>>(spec.Key, lazy)))
                continue;
        }

        // Give up on freshness rather than loop forever — return whatever is cached now.
        return _cache.TryGetValue(spec.Key, out var current) && current.IsValueCreated && current.Value.IsCompletedSuccessfully
            ? current.Value.Result
            : await GetOrRunFreshAsync(spec).ConfigureAwait(false);
    }

    private async Task<AccessProbeResult> GetOrRunFreshAsync(AccessProbeSpec spec)
    {
        var lazy = new Lazy<Task<AccessProbeResult>>(
            () => RunProbeAsync(spec), LazyThreadSafetyMode.ExecutionAndPublication);
        _cache[spec.Key] = lazy;
        return await lazy.Value.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one probe under the external timeout and classifies the outcome. Never throws: an
    /// unreachable/misconfigured target is <see cref="AccessStatus.Unknown"/> data, not a report
    /// failure.
    /// </summary>
    private async Task<AccessProbeResult> RunProbeAsync(AccessProbeSpec spec)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var timeout = new CancellationTokenSource(_probeTimeout);
        Task<ProbeOutcome> probeTask;
        try
        {
            probeTask = spec.Probe(timeout.Token);
        }
        catch (Exception ex)
        {
            timeout.Dispose();
            return spec.ToResult(Classify(spec, ex), checkedAt);
        }

        // Dispose the timeout source once the probe settles, however it settled.
        _ = probeTask.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            timeout, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        if (await Task.WhenAny(probeTask, Task.Delay(_probeTimeout, CancellationToken.None)).ConfigureAwait(false) != probeTask)
        {
            // Abandoned, not just cancelled — a probe ignoring the token (Redis with
            // AbortOnConnectFail=false) still finishes in the background; observe a late fault
            // so it doesn't die silently.
            _ = probeTask.ContinueWith(t =>
                    _logger.LogWarning(t.Exception,
                        "Access probe {ProbeKey} faulted after the timeout was abandoned", spec.Key),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return spec.ToResult(ProbeOutcome.Unknown("Probe timed out."), checkedAt);
        }

        try
        {
            return spec.ToResult(await probeTask.ConfigureAwait(false), checkedAt);
        }
        catch (Exception ex)
        {
            return spec.ToResult(Classify(spec, ex), checkedAt);
        }
    }

    /// <summary>
    /// Maps a probe failure to an outcome: recognized authz failures become Denied with the
    /// structured remedy; everything else is Unknown with a sanitized summary.
    /// </summary>
    private ProbeOutcome Classify(AccessProbeSpec spec, Exception ex)
    {
        if (ex is OperationCanceledException)
            return ProbeOutcome.Unknown("Probe timed out.");
        if (AccessAdvisor.TryCreateDenial(ex, spec.FeatureArea, out var denial))
            return ProbeOutcome.Denied(denial);
        _logger.LogDebug(ex, "Access probe {ProbeKey} failed", spec.Key);
        return ProbeOutcome.Unknown(ConnectionTestError.Describe(ex));
    }
}
