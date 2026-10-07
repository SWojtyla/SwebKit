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
        // Resolve the effective context up front: rules may follow the globally configured
        // context (empty pin), and an error like "pods is forbidden" is undiagnosable unless
        // the message says which cluster it came from.
        var context = _pool.ResolveAksContext(rule.AksPodParams?.KubeconfigContext);
        var client = _pool.GetAksClient(context);
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "AKS not configured");

        var ns = rule.AksPodParams?.Namespace ?? string.Empty;
        try
        {
            var pods = await client.GetPodsAsync(ns, null, ct).ConfigureAwait(false);
            var result = Evaluate(rule, ns, pods);
            // Firing messages surface in notifications and alert history without the rule
            // row's context/namespace label — tag the cluster there too.
            return context is not null && result.Status == AlertSignalStatus.Firing
                ? result with { Message = $"[{context}] {result.Message}" }
                : result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{SignalSource} error for rule {RuleId} on context {Context}", GetType().Name, rule.Id, context);
            return new AlertSignalResult(
                AlertSignalStatus.Error,
                context is null ? ex.Message : $"[{context}] {ex.Message}");
        }
        // Note: do NOT dispose client - the pool owns its lifetime.
    }

    /// <summary>
    /// Evaluates the fetched pods for the given rule. Invoked inside the base
    /// class's cancellation/error handler, so implementations may throw freely.
    /// </summary>
    protected abstract AlertSignalResult Evaluate(MonitoringAlertRule rule, string ns, IReadOnlyList<PodInfo> pods);
}
