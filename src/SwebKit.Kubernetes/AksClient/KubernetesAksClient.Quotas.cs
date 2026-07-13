using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace SwebKit.Kubernetes.AksClient;

public partial class KubernetesAksClient
{
    // ── Wave 1: namespace and workload constraint visibility ──────────────────

    public async Task<IReadOnlyList<ResourceQuotaInfo>> GetResourceQuotasAsync(string ns, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var result = await _client.CoreV1.ListNamespacedResourceQuotaAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                return result.Items.Select(quota => new ResourceQuotaInfo
                {
                    Name = quota.Metadata?.Name ?? string.Empty,
                    Namespace = quota.Metadata?.NamespaceProperty ?? ns,
                    HardLimits = quota.Status?.Hard?
                        .Select(kv => new ResourceQuotaUsage { Resource = kv.Key, Hard = kv.Value?.ToString() })
                        .ToList() ?? [],
                    Used = quota.Status?.Used?
                        .Select(kv => new ResourceQuotaUsage { Resource = kv.Key, Used = kv.Value?.ToString() })
                        .ToList() ?? []
                }).ToList<ResourceQuotaInfo>();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Reading ResourceQuotas requires permission to list resourcequotas in namespace '{ns}'.",
                ex);
        }
    }

    public async Task<IReadOnlyList<LimitRangeInfo>> GetLimitRangesAsync(string ns, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var result = await _client.CoreV1.ListNamespacedLimitRangeAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                return result.Items.Select(lr => new LimitRangeInfo
                {
                    Name = lr.Metadata?.Name ?? string.Empty,
                    Namespace = lr.Metadata?.NamespaceProperty ?? ns,
                    Limits = lr.Spec?.Limits?.Select(item => new LimitRangeItem
                    {
                        Type = item.Type ?? "Container",
                        DefaultRequests = item.DefaultRequest?
                            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) ?? [],
                        DefaultLimits = item.DefaultProperty?
                            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) ?? [],
                        Min = item.Min?
                            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) ?? [],
                        Max = item.Max?
                            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) ?? []
                    }).ToList() ?? []
                }).ToList<LimitRangeInfo>();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Reading LimitRanges requires permission to list limitranges in namespace '{ns}'.",
                ex);
        }
    }

    public async Task<IReadOnlyList<PodDisruptionBudgetInfo>> GetPodDisruptionBudgetsAsync(string ns, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var result = await _client.PolicyV1.ListNamespacedPodDisruptionBudgetAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                return result.Items.Select(pdb => new PodDisruptionBudgetInfo
                {
                    Name = pdb.Metadata?.Name ?? string.Empty,
                    Namespace = pdb.Metadata?.NamespaceProperty ?? ns,
                    MinAvailable = pdb.Spec?.MinAvailable?.ToString(),
                    MaxUnavailable = pdb.Spec?.MaxUnavailable?.ToString(),
                    DesiredHealthy = pdb.Status?.DesiredHealthy ?? 0,
                    CurrentHealthy = pdb.Status?.CurrentHealthy ?? 0,
                    ExpectedPods = pdb.Status?.ExpectedPods ?? 0,
                    DisruptionsAllowed = (pdb.Status?.DisruptionsAllowed ?? 0) > 0,
                    AllowedDisruptions = pdb.Status?.DisruptionsAllowed ?? 0,
                    SelectorLabels = pdb.Spec?.Selector?.MatchLabels is not null
                        ? new Dictionary<string, string>(pdb.Spec.Selector.MatchLabels)
                        : []
                }).ToList<PodDisruptionBudgetInfo>();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Reading PodDisruptionBudgets requires permission to list poddisruptionbudgets in namespace '{ns}'.",
                ex);
        }
    }

    public async Task<ProbeFailureSummary> GetProbeFailureSummaryAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var (resolvedPods, _) = await ResolveWorkloadPodsAsync(ns, workloadKind, workloadName, ct).ConfigureAwait(false);
                var eventsTask = _client.CoreV1.ListNamespacedEventAsync(ns, cancellationToken: ct);

                var podList = await _client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                var events = await eventsTask.ConfigureAwait(false);

                var podDetails = resolvedPods
                    .Select(pod =>
                    {
                        var fullPod = podList.Items.FirstOrDefault(p =>
                            string.Equals(p.Metadata?.Name, pod.Metadata?.Name, StringComparison.Ordinal))
                            ?? pod;

                        var containerStatus = fullPod.Status?.ContainerStatuses?.FirstOrDefault();
                        var specContainers = fullPod.Spec?.Containers ?? [];

                        return new PodProbeStatus
                        {
                            PodName = fullPod.Metadata?.Name ?? string.Empty,
                            RestartCount = containerStatus?.RestartCount ?? 0,
                            Ready = containerStatus?.Ready ?? false,
                            LivenessProbeConfigured = specContainers.Any(c => c.LivenessProbe is not null),
                            ReadinessProbeConfigured = specContainers.Any(c => c.ReadinessProbe is not null),
                            LastTerminationReason = containerStatus?.LastState?.Terminated?.Reason,
                            LastTerminationMessage = containerStatus?.LastState?.Terminated?.Message
                        };
                    }).ToList();

                var probeEvents = events.Items
                    .Where(ev =>
                        ev.Reason is not null &&
                        (ev.Reason.Contains("Liveness", StringComparison.OrdinalIgnoreCase)
                         || ev.Reason.Contains("Readiness", StringComparison.OrdinalIgnoreCase)
                         || ev.Reason.Contains("BackOff", StringComparison.OrdinalIgnoreCase)
                         || ev.Reason.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase))
                        && resolvedPods.Any(p =>
                            string.Equals(ev.InvolvedObject?.Name, p.Metadata?.Name, StringComparison.Ordinal)))
                    .OrderByDescending(ev => ev.LastTimestamp)
                    .Take(10)
                    .Select(ev => $"[{ev.Reason}] {ev.Message} (pod: {ev.InvolvedObject?.Name})")
                    .ToList();

                var findings = new List<string>();
                var podsWithRestarts = podDetails.Count(p => p.RestartCount > 0);
                var podsNotReady = podDetails.Count(p => !p.Ready);

                if (podsWithRestarts > 0)
                    findings.Add($"{podsWithRestarts} of {podDetails.Count} pod(s) have restarted at least once in the current session.");

                if (podsNotReady > 0)
                    findings.Add($"{podsNotReady} of {podDetails.Count} pod(s) are not ready.");

                foreach (var ev in probeEvents.Take(3))
                    findings.Add(ev);

                if (findings.Count == 0)
                    findings.Add("No probe failures or restarts observed in the inspected pods.");

                return new ProbeFailureSummary
                {
                    Namespace = ns,
                    WorkloadKind = workloadKind,
                    WorkloadName = workloadName,
                    TotalPods = podDetails.Count,
                    PodsWithRestarts = podsWithRestarts,
                    Pods = podDetails,
                    RecentProbeEvents = probeEvents,
                    Findings = findings
                };
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"{workloadKind} '{workloadName}' was not found in namespace '{ns}'.",
                ex);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Probe failure summary requires permission to read workload, Pod, and Event resources in namespace '{ns}'.",
                ex);
        }
    }

    public async Task<PlacementAnalysis> GetPlacementAnalysisAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var (resolvedPods, _) = await ResolveWorkloadPodsAsync(ns, workloadKind, workloadName, ct).ConfigureAwait(false);
                var events = await _client.CoreV1.ListNamespacedEventAsync(ns, cancellationToken: ct).ConfigureAwait(false);

                var firstPod = resolvedPods.FirstOrDefault();
                var spec = firstPod?.Spec;

                var nodeSelector = spec?.NodeSelector is not null
                    ? new Dictionary<string, string>(spec.NodeSelector)
                    : [];
                var tolerations = spec?.Tolerations?
                    .Select(t => string.IsNullOrWhiteSpace(t.Effect)
                        ? t.Key ?? "(all)"
                        : $"{t.Key ?? "(all)"}:{t.Effect}")
                    .ToList() ?? [];
                var topologySpreadKeys = spec?.TopologySpreadConstraints?
                    .Select(tsc => tsc.TopologyKey ?? "(unspecified)")
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? [];

                var podNames = resolvedPods
                    .Select(p => p.Metadata?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToHashSet(StringComparer.Ordinal);

                var schedulingFailureEvents = events.Items
                    .Where(ev =>
                        string.Equals(ev.Reason, "FailedScheduling", StringComparison.Ordinal)
                        && podNames.Contains(ev.InvolvedObject?.Name ?? string.Empty))
                    .OrderByDescending(ev => ev.LastTimestamp)
                    .Take(5)
                    .Select(ev => ev.Message ?? string.Empty)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                var findings = new List<string>();
                if (nodeSelector.Count > 0)
                    findings.Add($"Node selector requires: {string.Join(", ", nodeSelector.Select(kv => $"{kv.Key}={kv.Value}"))}.");

                if (spec?.Affinity?.NodeAffinity is not null)
                    findings.Add("Node affinity rule declared.");

                if (spec?.Affinity?.PodAffinity is not null)
                    findings.Add("Pod affinity rule declared: pods prefer to co-locate.");

                if (spec?.Affinity?.PodAntiAffinity is not null)
                    findings.Add("Pod anti-affinity rule declared: pods prefer to spread across failure domains.");

                if (tolerations.Count > 0)
                    findings.Add($"Tolerations declared: {string.Join(", ", tolerations)}.");

                if (topologySpreadKeys.Count > 0)
                    findings.Add($"Topology spread constraints on keys: {string.Join(", ", topologySpreadKeys)}.");

                foreach (var msg in schedulingFailureEvents)
                    findings.Add($"FailedScheduling: {msg}");

                if (findings.Count == 0)
                    findings.Add("No placement constraints or scheduling failures observed in the inspected pods.");

                return new PlacementAnalysis
                {
                    Namespace = ns,
                    WorkloadKind = workloadKind,
                    WorkloadName = workloadName,
                    HasNodeSelector = nodeSelector.Count > 0,
                    NodeSelector = nodeSelector,
                    HasNodeAffinity = spec?.Affinity?.NodeAffinity is not null,
                    HasPodAffinity = spec?.Affinity?.PodAffinity is not null,
                    HasPodAntiAffinity = spec?.Affinity?.PodAntiAffinity is not null,
                    HasTolerations = tolerations.Count > 0,
                    Tolerations = tolerations,
                    HasTopologySpreadConstraints = topologySpreadKeys.Count > 0,
                    TopologySpreadKeys = topologySpreadKeys,
                    RecentSchedulingFailureEvents = schedulingFailureEvents,
                    Findings = findings
                };
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"{workloadKind} '{workloadName}' was not found in namespace '{ns}'.",
                ex);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Placement analysis requires permission to read workload, Pod, and Event resources in namespace '{ns}'.",
                ex);
        }
    }
}
