using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Services.AccessProbes;

namespace SwebKit.Sidecar.Services;

/// <summary>Answers "what changed since 14:32?" — a single time-ordered timeline merging the
/// sources that can actually attest a change: durable alert history + the engine's in-memory
/// ring buffer (same merge as <see cref="GetAlertHistoryTool"/> and /api/monitoring/history),
/// Kubernetes events, pod restarts, deployment status-condition updates
/// (<see cref="DeploymentInfo.LastUpdateTime"/>), and Helm revision timestamps (the
/// <c>owner=helm</c> Secret creation timestamps behind <see cref="IAksClient.GetHelmRevisionsAsync"/>).
///
/// Lives in the sidecar (not SwebKit.Agents) because the alert engine's ring buffer and the
/// pooled AKS client are both sidecar-hosted — same precedent as <see cref="GetAlertHistoryTool"/>.
///
/// Two deliberate design choices:
/// <list type="bullet">
///   <item>Every entry carries a <c>confidence</c> label and the description instructs
///   correlation-not-causality: this tool reports <em>what changed and when</em>, never
///   <em>what broke it</em>. Temporal proximity is evidence, not proof.</item>
///   <item>Denials are reported <em>per source</em> in the <c>coverage</c> section rather than
///   failing the whole call — a merge tool whose AKS legs are denied must still return the
///   alert history. That is also why this tool does NOT implement
///   <see cref="IAccessAwareTool"/>: its all-or-nothing known-denial short-circuit would
///   suppress the local alert data whenever the cluster leg is denied. The capability
///   (<c>kubernetes.read</c>) and connection key (<c>"aks"</c>) are still declared the same
///   way — known denials are checked up front and observed denials are recorded via
///   <see cref="IAccessReportService"/> — so the access report still flips red.</item>
/// </list></summary>
public sealed class GetChangeTimelineTool : IAgentTool
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 250;
    private const int EventsFetchLimit = 250;
    private const int MaxDetailChars = 300;

    private static readonly string[] AksSources =
        ["kubernetes_events", "pod_restarts", "deployment_updates", "helm_revisions"];

    private static readonly string[] CoverageNotes =
    [
        "Correlation, not causality: this lists what changed and when — it does not establish that any entry caused a symptom.",
        "pod_restart shows each pod's MOST RECENT restart only; earlier restarts inside the window are not individually visible.",
        "kubernetes_event 'at' is the event's last occurrence; count>1 means it recurred (first occurrence may predate the window).",
        "deployment_update 'at' is the deployment's newest status-condition timestamp — proves something changed, not what.",
        "alert_history covers configured monitoring rules only; a change that fired no rule leaves no alert row.",
    ];

    private readonly MonitoringAlertEvaluationService _engine;
    private readonly IAlertHistoryRepository _history;
    private readonly IMonitoringConnectionPool _pool;
    private readonly DemoModeService _demo;
    private readonly ProfileRepository _profile;
    private readonly IAccessReportService? _accessReport;

    public GetChangeTimelineTool(
        MonitoringAlertEvaluationService engine,
        IAlertHistoryRepository history,
        IMonitoringConnectionPool pool,
        DemoModeService demo,
        ProfileRepository profile,
        IAccessReportService? accessReport = null)
    {
        _engine = engine;
        _history = history;
        _pool = pool;
        _demo = demo;
        _profile = profile;
        _accessReport = accessReport;
    }

    public string Name => "get_change_timeline";

    public string Description =>
        "Returns a single time-ordered timeline of everything that changed since a given " +
        "timestamp: alert firings/resolutions, Kubernetes events, pod restarts, deployment " +
        "updates, and Helm revision changes. Use for 'what changed since 14:32?' questions and " +
        "to correlate changes with symptoms. IMPORTANT: this is correlation, not causality — " +
        "it shows what changed and in what order, never which change broke something. Each " +
        "entry carries a confidence label: 'high' = the timestamp IS the change (alert observed, " +
        "Helm revision written); 'medium' = real timestamp that may under-represent (an event's " +
        "last recurrence, a pod's latest restart — earlier occurrences inside the window are " +
        "invisible); 'low' = a proxy timestamp (deployment condition update — proves something " +
        "about the deployment changed, not what). Always check the coverage section before " +
        "concluding 'nothing changed' — it lists sources that were unchecked, denied, or errored.";

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "since_iso": {
              "type": "string",
              "description": "ISO-8601 timestamp — the start of the window. Required, e.g. \"2026-03-14T14:32:00Z\"."
            },
            "area": {
              "type": "string",
              "enum": ["all", "aks", "monitoring"],
              "description": "Optional source filter: \"monitoring\" = alert history only, \"aks\" = Kubernetes sources only, \"all\" (default) = everything."
            },
            "namespace": {
              "type": "string",
              "description": "Kubernetes namespace for the cluster sources (default \"default\"). \"*\" or empty scans every readable namespace — per-namespace RBAC denials are reported in coverage instead of failing."
            },
            "context": {
              "type": "string",
              "description": "Optional kubeconfig context — target this cluster instead of the globally configured one."
            },
            "limit": {
              "type": "integer",
              "description": "Max timeline entries to return, newest first. Defaults to 100, capped at 250."
            }
          },
          "required": ["since_iso"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("since_iso", out var sinceEl)
            || sinceEl.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(sinceEl.GetString(),
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                   out var since))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Missing or invalid 'since_iso' — pass an ISO-8601 timestamp like \"2026-03-14T14:32:00Z\"."
            });
        }

        var areaArg = arguments.TryGetProperty("area", out var areaEl) && areaEl.ValueKind == JsonValueKind.String
            ? areaEl.GetString()?.Trim().ToLowerInvariant()
            : null;
        var (includeAlerts, includeAks) = areaArg switch
        {
            null or "" or "all" => (true, true),
            "monitoring" or "alerts" or "alert" => (true, false),
            "aks" or "kubernetes" or "k8s" => (false, true),
            _ => (false, false),
        };
        if (!includeAlerts && !includeAks)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Unknown area '{areaArg}' — expected \"all\", \"aks\", or \"monitoring\"."
            });
        }

        var ns = arguments.TryGetProperty("namespace", out var nsEl) && nsEl.ValueKind == JsonValueKind.String
            ? nsEl.GetString()?.Trim()
            : null;
        var allNamespaces = ns is "*" || string.Equals(ns, "all", StringComparison.OrdinalIgnoreCase);
        var effectiveNs = allNamespaces ? "*" : (string.IsNullOrEmpty(ns) ? "default" : ns);

        var context = arguments.TryGetProperty("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.String
            ? ctxEl.GetString()
            : null;

        var limit = DefaultLimit;
        if (arguments.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number
            && l.TryGetInt32(out var lv) && lv > 0)
            limit = Math.Min(lv, MaxLimit);

        var entries = new List<TimelineEntry>();
        var coverage = new List<object>();

        // ── Alert history: durable store merged with the engine's volatile ring buffer ──
        // (identical merge/dedupe to GetAlertHistoryTool and /api/monitoring/history).
        if (includeAlerts)
        {
            try
            {
                var persisted = await _history.GetAllAsync();
                var seen = new HashSet<string>(persisted.Select(AlertKey));
                var alerts = persisted
                    .Concat(_engine.RecentAlerts.Select(ToHistoryEntry).Where(e => seen.Add(AlertKey(e))))
                    .Where(e => e.At >= since)
                    .Select(e => new TimelineEntry(
                        e.At,
                        e.Kind == AlertHistoryKind.Resolved ? "alert_resolved"
                            : e.Kind == AlertHistoryKind.Suppressed ? "alert_suppressed" : "alert_fired",
                        $"{e.Kind} — rule '{e.RuleName}' ({e.Source}): {e.Message}",
                        $"rule_id={e.RuleId}; severity={e.Severity}",
                        "high"))
                    .ToList();
                entries.AddRange(alerts);
                coverage.Add(new { source = "alert_history", status = "checked", entries = alerts.Count });
            }
            catch (Exception ex)
            {
                coverage.Add(new { source = "alert_history", status = "error", detail = Truncate(ex.Message) });
            }
        }
        else
        {
            coverage.Add(new { source = "alert_history", status = "skipped", detail = "Excluded by area filter." });
        }

        // ── Kubernetes sources: events, pod restarts, deployment updates, Helm revisions ──
        if (!includeAks)
        {
            foreach (var s in AksSources)
                coverage.Add(new { source = s, status = "skipped", detail = "Excluded by area filter." });
        }
        else
        {
            var connectionKey = ResolveConnectionKey(context);
            var client = _pool.GetAksClient(context);

            if (client is null)
            {
                foreach (var s in AksSources)
                    coverage.Add(new { source = s, status = "unchecked", detail = "AKS is not configured — no kubeconfig context to query." });
            }
            else if (connectionKey is not null && _accessReport is not null
                     && _accessReport.TryGetKnownDenial(nameof(FeatureArea.Aks), connectionKey, AccessCapabilities.KubernetesRead, out var known))
            {
                // A fresh probed/observed denial means every leg would 403 the same way —
                // report it per source without re-hitting the cluster.
                foreach (var s in AksSources)
                    coverage.Add(DeniedCoverage(s, known, cached: true));
            }
            else
            {
                // One scope collects any per-namespace RBAC denials the multi-namespace fan-out
                // swallows (namespace="*" path) — surfaced as partial-denial detail on coverage.
                using var deniedScope = new AksAccessDeniedScope();

                IReadOnlyList<string>? namespaces = null;
                if (allNamespaces)
                {
                    try { namespaces = await client.GetNamespacesAsync(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        foreach (var s in AksSources)
                            coverage.Add(FailedCoverage(s, ex, connectionKey));
                        namespaces = null;
                    }
                }

                if (!allNamespaces || namespaces is not null)
                {
                    var legs = await Task.WhenAll(
                        RunSourceAsync("kubernetes_events", connectionKey, ct, async token =>
                        {
                            var events = allNamespaces
                                ? await client.GetEventsAsync(namespaces!, EventsFetchLimit, token)
                                : await client.GetEventsAsync(effectiveNs, EventsFetchLimit, token);
                            return events
                                .Where(e => e.LastTimestamp >= since)
                                .Select(e => new TimelineEntry(
                                    e.LastTimestamp!.Value,
                                    "kubernetes_event",
                                    $"{e.Type} {e.Reason} on {e.InvolvedObjectKind}/{e.InvolvedObjectName}: {e.Message}",
                                    $"namespace={e.Namespace}; count={e.Count}",
                                    "medium"))
                                .ToList();
                        }),
                        RunSourceAsync("pod_restarts", connectionKey, ct, async token =>
                        {
                            var pods = allNamespaces
                                ? await client.GetPodsAsync(namespaces!, token)
                                : await client.GetPodsAsync(effectiveNs, null, token);
                            return pods
                                .Where(p => p.LastRestartTime >= since)
                                .Select(p => new TimelineEntry(
                                    p.LastRestartTime!.Value,
                                    "pod_restart",
                                    $"Pod {p.Namespace}/{p.Name} restarted ({p.LastRestartReason ?? "reason unknown"}) — {p.RestartCount} restart(s) total",
                                    $"phase={p.Phase}; status={p.Status}",
                                    "medium"))
                                .ToList();
                        }),
                        RunSourceAsync("deployment_updates", connectionKey, ct, async token =>
                        {
                            var deployments = allNamespaces
                                ? await client.GetDeploymentsAsync(namespaces!, token)
                                : await client.GetDeploymentsAsync(effectiveNs, token);
                            return deployments
                                .Where(d => d.LastUpdateTime >= since)
                                .Select(d => new TimelineEntry(
                                    d.LastUpdateTime!.Value,
                                    "deployment_update",
                                    $"Deployment {d.Namespace}/{d.Name} updated — status {d.Status}, {d.ReadyReplicas}/{d.Replicas} ready",
                                    d.ImageTag is { } tag ? $"image_tag={tag}" : null,
                                    "low"))
                                .ToList();
                        }),
                        RunSourceAsync("helm_revisions", connectionKey, ct, async token =>
                        {
                            IReadOnlyList<HelmRevisionInfo> revisions = allNamespaces
                                ? (await Task.WhenAll(namespaces!.Select(n => client.GetHelmRevisionsAsync(n, token)))
                                        .ConfigureAwait(false))
                                    .SelectMany(r => r).ToList()
                                : await client.GetHelmRevisionsAsync(effectiveNs, token).ConfigureAwait(false);
                            return revisions
                                .Where(r => r.Updated >= since)
                                .Select(r => new TimelineEntry(
                                    r.Updated!.Value,
                                    "helm_revision",
                                    $"Helm release {r.ReleaseName} revision {r.Revision} → {r.Status}",
                                    $"chart={r.Chart}{(r.Description is { } d ? $"; {d}" : "")}",
                                    "high"))
                                .ToList();
                        }));
                    coverage.AddRange(legs.Select(leg => leg.Coverage));
                    entries.AddRange(legs.SelectMany(leg => leg.Entries));
                }

                var partial = deniedScope.Denials;
                if (partial.Count > 0)
                {
                    coverage.Add(new
                    {
                        source = "kubernetes_namespaces",
                        status = "access_denied",
                        detail = "RBAC denied for: " + string.Join(", ",
                            partial.Select(d => $"{d.ResourceKind} in '{d.Namespace}'")),
                        capability = AccessCapabilities.KubernetesRead,
                    });
                }
            }
        }

        var merged = entries
            .GroupBy(e => (e.Source, e.At.UtcTicks, e.Summary))
            .Select(g => g.First())
            .OrderByDescending(e => e.At)
            .ToList();

        var truncated = merged.Count > limit;
        var window = merged.Take(limit).ToList();

        return JsonSerializer.Serialize(new
        {
            since = since,
            queried_at = DateTimeOffset.UtcNow,
            area = areaArg ?? "all",
            @namespace = effectiveNs,
            context,
            entry_count = window.Count,
            truncated,
            entries = window.Select(e => new
            {
                at = e.At,
                source = e.Source,
                summary = e.Summary,
                detail = e.Detail,
                confidence = e.Confidence,
            }),
            coverage = new
            {
                sources = coverage,
                notes = CoverageNotes,
            },
        });
    }

    /// <summary>Runs one Kubernetes source leg, converting any failure into a coverage row
    /// instead of throwing — a denied/failed source must not discard the sources that worked.</summary>
    private async Task<(List<TimelineEntry> Entries, object Coverage)> RunSourceAsync(
        string source, string? connectionKey, CancellationToken ct,
        Func<CancellationToken, Task<List<TimelineEntry>>> fetch)
    {
        try
        {
            var found = await fetch(ct);
            return (found, new { source, status = "checked", entries = found.Count });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ([], FailedCoverage(source, ex, connectionKey));
        }
    }

    /// <summary>Classifies a failed source leg: a recognized authz failure becomes an
    /// <c>access_denied</c> coverage row (and feeds the access report's observed-denial path,
    /// the same feed <see cref="AgentToolRegistry"/> maintains for IAccessAwareTool tools);
    /// anything else is a plain <c>error</c>.</summary>
    private object FailedCoverage(string source, Exception ex, string? connectionKey)
    {
        if (ex is AksAccessDeniedException || AccessAdvisor.TryCreateDenial(ex, nameof(FeatureArea.Aks), out _))
        {
            AccessAdvisor.TryCreateDenial(ex, nameof(FeatureArea.Aks), out var denial);
            denial ??= new AccessDenial(
                nameof(FeatureArea.Aks), AccessCapabilities.KubernetesRead,
                "Azure Kubernetes Service RBAC Reader",
                "Ask a cluster admin for the 'Azure Kubernetes Service RBAC Reader' role (or a namespace-scoped read role) on this AKS cluster.",
                ex.Message);
            denial = denial with { Capability = AccessCapabilities.KubernetesRead };
            if (connectionKey is not null && _accessReport is not null)
            {
                try { _accessReport.RecordObservedDenial(denial, connectionKey); }
                catch { /* a report-sink failure must never mask the coverage row */ }
            }
            return DeniedCoverage(source, denial, cached: false);
        }
        return new { source, status = "error", detail = Truncate(ex.Message) };
    }

    private static object DeniedCoverage(string source, AccessDenial denial, bool cached) => new
    {
        source,
        status = "access_denied",
        capability = denial.Capability,
        required_access = denial.RequiredAccess,
        guidance = denial.Guidance,
        detail = Truncate(denial.Detail),
        cached,
    };

    /// <summary>
    /// Maps the call's resolved target to the access report's AKS connection key — mirrors
    /// <c>AksToolContext.ResolveConnectionKey</c> in SwebKit.Agents (internal there, so the
    /// single-cluster rule is duplicated here): the fixed <c>"aks"</c> key in demo mode or when
    /// the call targets the configured cluster; null for an un-probed explicit context, where a
    /// configured-cluster denial must not pre-empt it.
    /// </summary>
    private string? ResolveConnectionKey(string? context)
    {
        if (_demo.IsDemoMode)
            return AksAccessProbes.ConnectionKey;
        var aks = _profile.GetProfileData().Config.AksConfig;
        if (aks is null)
            return null;
        return string.IsNullOrWhiteSpace(context)
            || string.Equals(context, aks.KubeconfigContext, StringComparison.OrdinalIgnoreCase)
                ? AksAccessProbes.ConnectionKey
                : null;
    }

    private static string? Truncate(string? s) =>
        s is null ? null : s.Length <= MaxDetailChars ? s : s[..MaxDetailChars];

    private static string AlertKey(AlertHistoryEntry e) => $"{e.RuleId}|{e.At.UtcTicks}|{e.Kind}";

    private static AlertHistoryEntry ToHistoryEntry(AlertFiredEvent a) => new()
    {
        RuleId = a.RuleId,
        RuleName = a.RuleName,
        Source = a.Source,
        Severity = a.Severity,
        Kind = a.Suppressed ? AlertHistoryKind.Suppressed : AlertHistoryKind.Fired,
        At = a.FiredAt,
        Message = a.SuppressedBy is { } by ? $"{a.Message} (silenced: {by})" : a.Message,
    };

    private sealed record TimelineEntry(DateTimeOffset At, string Source, string Summary, string? Detail, string Confidence);
}
