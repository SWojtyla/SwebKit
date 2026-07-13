using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Shared base for AKS pod-based alert signal sources. Centralizes the common
/// connection-pool lookup, namespace resolution, pod fetch, cancellation and
/// error handling so derived sources supply only their distinct evaluation logic.
/// </summary>
public abstract class PodSignalSourceBase : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;

    /// <summary>Logger for the concrete signal source, used by the shared error handler.</summary>
    protected ILogger Logger { get; }

    protected PodSignalSourceBase(IMonitoringConnectionPool pool, ILogger logger)
    {
        _pool = pool;
        Logger = logger;
    }

    public abstract AlertRuleSource Source { get; }

    public async Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        var client = _pool.GetAksClient(rule.AksPodParams?.KubeconfigContext);
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "AKS not configured");

        var ns = rule.AksPodParams?.Namespace ?? string.Empty;
        try
        {
            var pods = await client.GetPodsAsync(ns, null, ct).ConfigureAwait(false);
            return Evaluate(rule, ns, pods);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{SignalSource} error for rule {RuleId}", GetType().Name, rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client - the pool owns its lifetime.
    }

    /// <summary>
    /// Evaluates the fetched pods for the given rule. Invoked inside the base
    /// class's cancellation/error handler, so implementations may throw freely.
    /// </summary>
    protected abstract AlertSignalResult Evaluate(MonitoringAlertRule rule, string ns, IReadOnlyList<PodInfo> pods);
}
