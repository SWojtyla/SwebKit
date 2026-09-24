using k8s;
using k8s.Autorest;
using k8s.Models;
using SwebKit.Core.Models;
using System.Net;
using System.Text.Json;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// KEDA <c>ScaledJob</c> support. The Kubernetes client SDK has no typed ScaledJob model —
/// everything goes through <c>CustomObjects</c> against the <c>keda.sh/v1alpha1</c> API,
/// the same path <c>ScaledObject</c> paused-state resolution already uses in Workloads.cs.
/// </summary>
public partial class KubernetesAksClient
{
    private const string KedaScaledJobsPlural = "scaledjobs";

    public async Task<IReadOnlyList<ScaledJobInfo>> GetScaledJobsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            object? raw;
            try
            {
                raw = await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    KedaApiGroup, KedaApiVersions[0], ns, KedaScaledJobsPlural, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // KEDA isn't installed on this cluster — "no scaled jobs" is the true answer.
                return (IReadOnlyList<ScaledJobInfo>)[];
            }

            return ParseScaledJobs(ns, raw);
        }).ConfigureAwait(false);
    }

    public async Task SetScaledJobScalingEnabledAsync(string ns, string scaledJobName, bool enabled, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            // Same native pause switch KEDA honours on ScaledObjects.
            var patchJson = JsonSerializer.Serialize(new
            {
                metadata = new
                {
                    annotations = new Dictionary<string, string>
                    {
                        [SwebKit.Core.Constants.AksScalingAnnotations.KedaPaused] = enabled ? "false" : "true"
                    }
                }
            });
            var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
            await PatchScaledJobAsync(ns, scaledJobName, patch, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task ScaleScaledJobAsync(string ns, string scaledJobName, int minReplicas, int maxReplicas, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patchJson = JsonSerializer.Serialize(new
            {
                spec = new { minReplicaCount = minReplicas, maxReplicaCount = maxReplicas }
            });
            var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
            await PatchScaledJobAsync(ns, scaledJobName, patch, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteScaledJobAsync(string ns, string scaledJobName, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            try
            {
                await _client.CustomObjects.DeleteNamespacedCustomObjectAsync(
                    KedaApiGroup, KedaApiVersions[0], ns, KedaScaledJobsPlural, scaledJobName,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"KEDA ScaledJob '{scaledJobName}' was not found in namespace '{ns}'.", ex);
            }
        }).ConfigureAwait(false);
    }

    private async Task PatchScaledJobAsync(string ns, string scaledJobName, V1Patch patch, CancellationToken ct)
    {
        try
        {
            await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
                patch, KedaApiGroup, KedaApiVersions[0], ns, KedaScaledJobsPlural, scaledJobName,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"KEDA ScaledJob '{scaledJobName}' was not found in namespace '{ns}'.", ex);
        }
    }

    /// <summary>
    /// Maps a <c>scaledjobs</c> CustomObject list response to <see cref="ScaledJobInfo"/> rows.
    /// Internal + static so the parsing is unit-testable without a cluster.
    /// </summary>
    internal static IReadOnlyList<ScaledJobInfo> ParseScaledJobs(string ns, object? raw)
    {
        var result = new List<ScaledJobInfo>();
        if (raw is null)
            return result;

        var json = JsonSerializer.Serialize(raw);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("metadata", out var meta)
                || !meta.TryGetProperty("name", out var nameEl)
                || nameEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var info = new ScaledJobInfo
            {
                Name = nameEl.GetString()!,
                Namespace = meta.TryGetProperty("namespace", out var nsEl) && nsEl.ValueKind == JsonValueKind.String
                    ? nsEl.GetString() ?? ns
                    : ns,
            };

            if (meta.TryGetProperty("annotations", out var ann)
                && ann.ValueKind == JsonValueKind.Object
                && ann.TryGetProperty(SwebKit.Core.Constants.AksScalingAnnotations.KedaPaused, out var pausedEl)
                && pausedEl.ValueKind == JsonValueKind.String)
            {
                info.IsPaused = string.Equals(pausedEl.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }

            if (item.TryGetProperty("spec", out var spec) && spec.ValueKind == JsonValueKind.Object)
            {
                info.MinReplicas = GetInt(spec, "minReplicaCount");
                info.MaxReplicas = GetInt(spec, "maxReplicaCount");

                if (spec.TryGetProperty("triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var trigger in triggers.EnumerateArray())
                    {
                        if (trigger.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                        {
                            var type = typeEl.GetString();
                            if (!string.IsNullOrWhiteSpace(type))
                                info.Triggers.Add(type);
                        }
                    }
                }
            }

            result.Add(info);
        }

        return result;
    }

    private static int GetInt(JsonElement obj, string property)
        => obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : 0;
}
