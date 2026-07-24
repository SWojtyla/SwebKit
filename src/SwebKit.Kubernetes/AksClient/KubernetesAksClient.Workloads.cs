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
    // ── Feature 2: StatefulSets ───────────────────────────────────────────────

    public async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.AppsV1.ListNamespacedStatefulSetAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items.Select(s => new StatefulSetInfo
            {
                Name = s.Metadata.Name,
                Namespace = s.Metadata.NamespaceProperty ?? ns,
                Replicas = s.Spec?.Replicas ?? 0,
                ReadyReplicas = s.Status?.ReadyReplicas ?? 0,
                CurrentRevision = s.Status?.CurrentRevision,
                UpdateRevision = s.Status?.UpdateRevision,
                Labels = s.Metadata.Labels is not null ? new Dictionary<string, string>(s.Metadata.Labels) : [],
                SelectorLabels = s.Spec?.Selector?.MatchLabels is not null
                    ? new Dictionary<string, string>(s.Spec.Selector.MatchLabels)
                    : []
            }).ToList();
        }).ConfigureAwait(false);
    }

    public async Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patch = new V1StatefulSet
            {
                Spec = new V1StatefulSetSpec
                {
                    Template = new V1PodTemplateSpec
                    {
                        Metadata = new V1ObjectMeta
                        {
                            Annotations = new Dictionary<string, string>
                            {
                                ["kubectl.kubernetes.io/restartedAt"] = DateTime.UtcNow.ToString("O")
                            }
                        }
                    }
                }
            };
            await _client.AppsV1.PatchNamespacedStatefulSetAsync(
                new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
                name, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patch = new V1StatefulSet
            {
                Spec = new V1StatefulSetSpec { Replicas = replicas }
            };
            await _client.AppsV1.PatchNamespacedStatefulSetAsync(
                new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
                name, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    // ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────

    public async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedConfigMapAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items.Select(cm => new ConfigMapInfo
            {
                Name = cm.Metadata.Name,
                Namespace = cm.Metadata.NamespaceProperty ?? ns,
                Data = cm.Data is not null ? new Dictionary<string, string>(cm.Data) : [],
                Labels = cm.Metadata.Labels is not null ? new Dictionary<string, string>(cm.Metadata.Labels) : []
            }).ToList();
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedSecretAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return MapSecrets(result.Items, ns);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches Secrets and Helm release info together with a single <c>ListNamespacedSecretAsync</c>
    /// call — Helm releases are themselves stored as Secrets (<c>owner=helm</c>), so
    /// <see cref="GetSecretsAsync"/> and <see cref="GetHelmReleasesAsync"/> would otherwise each list
    /// the namespace's secrets independently on every namespace switch and auto-refresh.
    /// </summary>
    public async Task<(IReadOnlyList<SecretInfo> Secrets, IReadOnlyList<HelmReleaseInfo> HelmReleases)> GetSecretsAndHelmReleasesAsync(
        string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedSecretAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return (MapSecrets(result.Items, ns), MapHelmReleases(result.Items, ns));
        }).ConfigureAwait(false);
    }

    internal static List<SecretInfo> MapSecrets(IEnumerable<V1Secret> secrets, string ns) =>
        secrets
            // Exclude Helm release secrets and service-account token secrets
            .Where(s =>
                s.Type != "kubernetes.io/service-account-token" &&
                !(s.Metadata.Labels?.TryGetValue("owner", out var owner) == true && owner == "helm"))
            .Select(s => new SecretInfo
            {
                Name = s.Metadata.Name,
                Namespace = s.Metadata.NamespaceProperty ?? ns,
                Type = s.Type ?? "Opaque",
                Keys = s.Data?.Keys.ToList() ?? [],
                Labels = s.Metadata.Labels is not null ? new Dictionary<string, string>(s.Metadata.Labels) : []
            }).ToList();

    public async Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
            if (secret.Data is null) return [];
            return secret.Data.ToDictionary(
                kv => kv.Key,
                kv => Encoding.UTF8.GetString(kv.Value));
        }).ConfigureAwait(false);
    }

    // ── Feature 4: Container details ─────────────────────────────────────────

    public async Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var pod = await _client.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: ct).ConfigureAwait(false);
            var containers = pod.Spec?.Containers ?? [];

            // Batch ConfigMap fetches — one API call per unique ConfigMap name
            var configMapNames = containers
                .SelectMany(c => c.Env ?? [])
                .Where(e => e.ValueFrom?.ConfigMapKeyRef is not null)
                .Select(e => e.ValueFrom!.ConfigMapKeyRef!.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var configMapCache = new Dictionary<string, V1ConfigMap>(StringComparer.Ordinal);
            foreach (var cmName in configMapNames)
            {
                try
                {
                    var cm = await _client.CoreV1.ReadNamespacedConfigMapAsync(cmName, ns, cancellationToken: ct).ConfigureAwait(false);
                    configMapCache[cmName] = cm;
                }
                catch { /* ConfigMap might not exist — skip resolution */ }
            }

            return containers.Select(c =>
            {
                var imageParts = (c.Image ?? string.Empty).Split(':', 2);
                var envVars = (c.Env ?? []).Select(e => MapEnvVar(e, configMapCache)).ToList();

                // Synthetic flag rows for envFrom sources
                foreach (var envFrom in c.EnvFrom ?? [])
                {
                    if (envFrom.ConfigMapRef is not null)
                        envVars.Add(new EnvVarDetail
                        {
                            Name = $"<all keys from configmap: {envFrom.ConfigMapRef.Name}>",
                            Source = EnvVarSourceKind.ConfigMapRef,
                            SourceName = envFrom.ConfigMapRef.Name,
                            IsResolved = false
                        });
                    else if (envFrom.SecretRef is not null)
                        envVars.Add(new EnvVarDetail
                        {
                            Name = $"<all keys from secret: {envFrom.SecretRef.Name}>",
                            Source = EnvVarSourceKind.SecretRef,
                            SourceName = envFrom.SecretRef.Name,
                            IsResolved = false
                        });
                }

                return new ContainerDetail
                {
                    Name = c.Name,
                    Image = c.Image ?? string.Empty,
                    ImageTag = imageParts.Length == 2 ? imageParts[1] : null,
                    Resources = new ResourceRequirements
                    {
                        CpuRequest = GetResourceValue(c.Resources?.Requests, "cpu"),
                        MemoryRequest = GetResourceValue(c.Resources?.Requests, "memory"),
                        CpuLimit = GetResourceValue(c.Resources?.Limits, "cpu"),
                        MemoryLimit = GetResourceValue(c.Resources?.Limits, "memory")
                    },
                    EnvVars = envVars
                };
            }).ToList();
        }).ConfigureAwait(false);
    }

    private static string? GetResourceValue(IDictionary<string, ResourceQuantity>? dict, string key)
    {
        if (dict is null) return null;
        return dict.TryGetValue(key, out var val) ? val?.ToString() : null;
    }

    private static EnvVarDetail MapEnvVar(V1EnvVar envVar, Dictionary<string, V1ConfigMap> configMapCache)
    {
        if (envVar.Value is not null)
            return new EnvVarDetail { Name = envVar.Name, Value = envVar.Value, Source = EnvVarSourceKind.Plain, IsResolved = true };

        if (envVar.ValueFrom?.ConfigMapKeyRef is not null)
        {
            var cmRef = envVar.ValueFrom.ConfigMapKeyRef;
            string? resolved = null;
            var isResolved = false;
            if (configMapCache.TryGetValue(cmRef.Name, out var cm) && cm.Data?.TryGetValue(cmRef.Key, out var val) == true)
            {
                resolved = val;
                isResolved = true;
            }
            return new EnvVarDetail
            {
                Name = envVar.Name,
                Value = resolved,
                Source = EnvVarSourceKind.ConfigMapRef,
                SourceName = cmRef.Name,
                SourceKey = cmRef.Key,
                IsResolved = isResolved
            };
        }

        if (envVar.ValueFrom?.SecretKeyRef is not null)
        {
            var sRef = envVar.ValueFrom.SecretKeyRef;
            return new EnvVarDetail
            {
                Name = envVar.Name,
                Value = null,
                Source = EnvVarSourceKind.SecretRef,
                SourceName = sRef.Name,
                SourceKey = sRef.Key,
                IsResolved = false
            };
        }

        if (envVar.ValueFrom?.FieldRef is not null)
            return new EnvVarDetail
            {
                Name = envVar.Name,
                Value = envVar.ValueFrom.FieldRef.FieldPath,
                Source = EnvVarSourceKind.FieldRef,
                IsResolved = true
            };

        return new EnvVarDetail { Name = envVar.Name, Source = EnvVarSourceKind.Plain, IsResolved = false };
    }

    // ── Feature 5: HPA ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            List<HpaInfo> hpas;
            try
            {
                var result = await _client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                hpas = result.Items.Select(MapHpaV2).ToList();
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Fall back to autoscaling/v1 on older clusters
                var result = await _client.AutoscalingV1.ListNamespacedHorizontalPodAutoscalerAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                hpas = result.Items.Select(MapHpaV1).ToList();
            }

            await ApplyKedaScalingStateAsync(ns, hpas, ct).ConfigureAwait(false);
            return (IReadOnlyList<HpaInfo>)hpas;
        }).ConfigureAwait(false);
    }

    private HpaInfo MapHpaV2(V2HorizontalPodAutoscaler hpa)
    {
        var ns = hpa.Metadata.NamespaceProperty ?? string.Empty;
        var cpuMetric = hpa.Status?.CurrentMetrics
            ?.FirstOrDefault(m => m.Type == "Resource" && m.Resource?.Name == "cpu");
        var cpuTarget = hpa.Spec?.Metrics
            ?.FirstOrDefault(m => m.Type == "Resource" && m.Resource?.Name == "cpu");

        var info = new HpaInfo
        {
            Name = hpa.Metadata.Name,
            Namespace = ns,
            TargetKind = hpa.Spec?.ScaleTargetRef?.Kind ?? "Deployment",
            TargetName = hpa.Spec?.ScaleTargetRef?.Name ?? string.Empty,
            MinReplicas = hpa.Spec?.MinReplicas ?? 1,
            MaxReplicas = hpa.Spec?.MaxReplicas ?? 1,
            CurrentReplicas = hpa.Status?.CurrentReplicas ?? 0,
            DesiredReplicas = hpa.Status?.DesiredReplicas ?? 0,
            CurrentCpuUtilizationPercent = cpuMetric?.Resource?.Current?.AverageUtilization,
            TargetCpuUtilizationPercent = cpuTarget?.Resource?.Target?.AverageUtilization,
            Metrics = hpa.Status?.CurrentMetrics?.Select(m => new HpaMetricStatus
            {
                Name = m.Resource?.Name ?? m.Pods?.Metric?.Name ?? m.External?.Metric?.Name ?? "unknown",
                Type = m.Type,
                CurrentValue = m.Resource?.Current?.AverageUtilization.HasValue == true
                    ? (double?)m.Resource!.Current!.AverageUtilization!.Value
                    : null,
                TargetValue = cpuTarget?.Resource?.Target?.AverageUtilization.HasValue == true
                    ? (double?)cpuTarget!.Resource!.Target!.AverageUtilization!.Value
                    : null
            }).ToList() ?? [],
            Conditions = hpa.Status?.Conditions?.Select(c => new HpaCondition
            {
                Type = c.Type,
                Status = c.Status,
                Reason = c.Reason,
                Message = c.Message
            }).ToList() ?? []
        };

        ApplyScalingMetadata(info, hpa.Metadata);
        return info;
    }

    private static HpaInfo MapHpaV1(V1HorizontalPodAutoscaler hpa)
    {
        var info = new HpaInfo
        {
            Name = hpa.Metadata.Name,
            Namespace = hpa.Metadata.NamespaceProperty ?? string.Empty,
            TargetKind = hpa.Spec?.ScaleTargetRef?.Kind ?? "Deployment",
            TargetName = hpa.Spec?.ScaleTargetRef?.Name ?? string.Empty,
            MinReplicas = hpa.Spec?.MinReplicas ?? 1,
            MaxReplicas = hpa.Spec?.MaxReplicas ?? 1,
            CurrentReplicas = hpa.Status?.CurrentReplicas ?? 0,
            DesiredReplicas = hpa.Status?.DesiredReplicas ?? 0,
            CurrentCpuUtilizationPercent = hpa.Status?.CurrentCPUUtilizationPercentage,
            TargetCpuUtilizationPercent = hpa.Spec?.TargetCPUUtilizationPercentage
        };

        ApplyScalingMetadata(info, hpa.Metadata);
        return info;
    }

    /// <summary>
    /// Attaches KEDA-ownership and disabled-state hints that can be read straight from the HPA's own
    /// metadata (zero extra API calls). The KEDA <em>paused</em> state lives on the ScaledObject, not
    /// the HPA, and is resolved separately in <see cref="ApplyKedaScalingStateAsync"/>.
    /// </summary>
    private static void ApplyScalingMetadata(HpaInfo info, V1ObjectMeta? meta)
    {
        if (meta?.Labels is { } labels
            && labels.TryGetValue(AksScalingAnnotations.KedaScaledObjectNameLabel, out var scaledObject)
            && !string.IsNullOrWhiteSpace(scaledObject))
        {
            info.IsKedaManaged = true;
            info.ScaledObjectName = scaledObject;
        }

        // Plain-HPA freeze marker. (KEDA-managed HPAs carry no such marker — their paused state is
        // read from the owning ScaledObject instead.)
        if (!info.IsKedaManaged
            && meta?.Annotations is { } annotations
            && annotations.TryGetValue(AksScalingAnnotations.ScalingDisabled, out var flag)
            && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.IsScalingDisabled = true;
        }
    }

    /// <summary>
    /// For any KEDA-managed HPAs in <paramref name="hpas"/>, resolves their disabled (paused) state
    /// from the owning ScaledObjects with a single list call per namespace. Failures here are
    /// deliberately swallowed — enriching paused state must never break plain HPA browsing.
    /// </summary>
    private async Task ApplyKedaScalingStateAsync(string ns, List<HpaInfo> hpas, CancellationToken ct)
    {
        if (_kedaCrdAvailable == false)
            return;
        if (!hpas.Any(h => h.IsKedaManaged))
            return;

        object? raw;
        try
        {
            raw = await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                KedaApiGroup, KedaApiVersions[0], ns, KedaScaledObjectsPlural, cancellationToken: ct).ConfigureAwait(false);
            _kedaCrdAvailable = true;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // KEDA CRD isn't installed on this cluster — remember so we skip the extra call next time.
            _kedaCrdAvailable = false;
            return;
        }
        catch (k8s.Autorest.HttpOperationException)
        {
            // Forbidden or transient — leave paused state unknown for this pass without failing the load.
            return;
        }

        var pausedByName = ParseScaledObjectPausedMap(raw);
        foreach (var hpa in hpas)
        {
            if (hpa is { IsKedaManaged: true, ScaledObjectName: { } name }
                && pausedByName.TryGetValue(name, out var isPaused))
            {
                hpa.IsScalingDisabled = isPaused;
            }
        }
    }

    private static Dictionary<string, bool> ParseScaledObjectPausedMap(object? raw)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (raw is null)
            return map;

        var json = JsonSerializer.Serialize(raw);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("metadata", out var meta)
                || !meta.TryGetProperty("name", out var nameEl)
                || nameEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var paused = false;
            if (meta.TryGetProperty("annotations", out var ann)
                && ann.ValueKind == JsonValueKind.Object
                && ann.TryGetProperty(AksScalingAnnotations.KedaPaused, out var pausedEl)
                && pausedEl.ValueKind == JsonValueKind.String)
            {
                paused = string.Equals(pausedEl.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }

            map[nameEl.GetString()!] = paused;
        }

        return map;
    }

    public async Task SetHpaScalingEnabledAsync(string ns, string hpaName, bool enabled, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var (v2, v1) = await ReadHpaAsync(ns, hpaName, ct).ConfigureAwait(false);

            var labels = v2?.Metadata?.Labels ?? v1?.Metadata?.Labels;
            var annotations = v2?.Metadata?.Annotations ?? v1?.Metadata?.Annotations;

            // KEDA-managed HPA: toggle the ScaledObject's native pause annotation.
            if (labels is not null
                && labels.TryGetValue(AksScalingAnnotations.KedaScaledObjectNameLabel, out var scaledObjectName)
                && !string.IsNullOrWhiteSpace(scaledObjectName))
            {
                await SetKedaPausedAsync(ns, scaledObjectName, paused: !enabled, ct).ConfigureAwait(false);
                return;
            }

            // Plain HPA: freeze at current replicas (disable) or restore the stashed bounds (enable).
            var minReplicas = v2?.Spec?.MinReplicas ?? v1?.Spec?.MinReplicas ?? 1;
            var maxReplicas = v2?.Spec?.MaxReplicas ?? v1?.Spec?.MaxReplicas ?? 1;
            var currentReplicas = v2?.Status?.CurrentReplicas ?? v1?.Status?.CurrentReplicas ?? 0;

            string patchJson;
            if (!enabled)
            {
                var freeze = currentReplicas > 0 ? currentReplicas : Math.Max(maxReplicas, 1);
                patchJson = JsonSerializer.Serialize(new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string?>
                        {
                            [AksScalingAnnotations.ScalingDisabled] = "true",
                            [AksScalingAnnotations.OriginalBounds] = $"{minReplicas}/{maxReplicas}"
                        }
                    },
                    spec = new { minReplicas = freeze, maxReplicas = freeze }
                });
            }
            else
            {
                var (restoreMin, restoreMax) = ParseOriginalBounds(annotations, minReplicas, maxReplicas);
                patchJson = JsonSerializer.Serialize(new
                {
                    // Null values in a JSON merge patch remove the annotation keys.
                    metadata = new
                    {
                        annotations = new Dictionary<string, string?>
                        {
                            [AksScalingAnnotations.ScalingDisabled] = null,
                            [AksScalingAnnotations.OriginalBounds] = null
                        }
                    },
                    spec = new { minReplicas = restoreMin, maxReplicas = restoreMax }
                });
            }

            var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
            if (v2 is not null)
            {
                await _client.AutoscalingV2.PatchNamespacedHorizontalPodAutoscalerAsync(
                    patch, hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await _client.AutoscalingV1.PatchNamespacedHorizontalPodAutoscalerAsync(
                    patch, hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads an HPA preferring <c>autoscaling/v2</c>, falling back to <c>v1</c> when v2 is unavailable.
    /// Returns exactly one of the two typed objects (the other is <c>null</c>).
    /// </summary>
    private async Task<(V2HorizontalPodAutoscaler? V2, V1HorizontalPodAutoscaler? V1)> ReadHpaAsync(
        string ns, string name, CancellationToken ct)
    {
        try
        {
            var v2 = await _client.AutoscalingV2.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
            return (v2, null);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            var v1 = await _client.AutoscalingV1.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
            return (null, v1);
        }
    }

    private async Task SetKedaPausedAsync(string ns, string scaledObjectName, bool paused, CancellationToken ct)
    {
        var patchJson = JsonSerializer.Serialize(new
        {
            metadata = new
            {
                annotations = new Dictionary<string, string>
                {
                    [AksScalingAnnotations.KedaPaused] = paused ? "true" : "false"
                }
            }
        });
        var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);

        foreach (var version in KedaApiVersions)
        {
            try
            {
                await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
                    patch, KedaApiGroup, version, ns, KedaScaledObjectsPlural, scaledObjectName, cancellationToken: ct).ConfigureAwait(false);
                _kedaCrdAvailable = true;
                return;
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Try the next served API version, if any.
            }
        }

        throw new InvalidOperationException(
            $"KEDA ScaledObject '{scaledObjectName}' was not found in namespace '{ns}'.");
    }

    /// <summary>
    /// Parses the SwebKit "{min}/{max}" bounds stash written when a plain HPA was frozen. Falls back to
    /// the HPA's current bounds when the stash is missing or malformed (e.g. the HPA was disabled
    /// outside SwebKit) so re-enabling still produces a valid, non-frozen HPA where possible.
    /// </summary>
    private static (int Min, int Max) ParseOriginalBounds(
        IDictionary<string, string>? annotations, int fallbackMin, int fallbackMax)
    {
        if (annotations is not null
            && annotations.TryGetValue(AksScalingAnnotations.OriginalBounds, out var raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('/', 2);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var min)
                && int.TryParse(parts[1], out var max)
                && min >= 1 && max >= min)
            {
                return (min, max);
            }
        }

        var safeMin = fallbackMin >= 1 ? fallbackMin : 1;
        return (safeMin, Math.Max(fallbackMax, safeMin));
    }

    internal static double ParseCpuToMillicores(string cpu)
    {
        if (cpu.EndsWith('n'))
            return double.TryParse(cpu[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var nanos) ? nanos / 1_000_000_000.0 : 0;
        if (cpu.EndsWith('u'))
            return double.TryParse(cpu[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var micros) ? micros / 1_000_000.0 : 0;
        if (cpu.EndsWith('m'))
            return double.TryParse(cpu[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var millis) ? millis / 1_000.0 : 0;
        return double.TryParse(cpu, NumberStyles.Any, CultureInfo.InvariantCulture, out var cores) ? cores : 0;
    }

    internal static long ParseMemoryToBytes(string mem)
    {
        if (mem.EndsWith("Ki", StringComparison.Ordinal))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var ki) ? ki * 1024 : 0;
        if (mem.EndsWith("Mi", StringComparison.Ordinal))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mi) ? mi * 1024 * 1024 : 0;
        if (mem.EndsWith("Gi", StringComparison.Ordinal))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var gi) ? gi * 1024 * 1024 * 1024 : 0;
        return long.TryParse(mem, NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes) ? bytes : 0;
    }

    // ── Jobs and CronJobs ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.BatchV1.ListNamespacedJobAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items
                .Select(job => MapJobInfo(job, ns))
                .OrderByDescending(job => job.StartTime ?? DateTimeOffset.MinValue)
                .ThenBy(job => job.Name, StringComparer.Ordinal)
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var cronJob = await _client.BatchV1.ReadNamespacedCronJobAsync(cronJobName, ns, cancellationToken: ct).ConfigureAwait(false);
                var createdJob = await _client.BatchV1.CreateNamespacedJobAsync(
                    BuildTriggeredJobFromCronJob(cronJob, ns),
                    ns,
                    cancellationToken: ct).ConfigureAwait(false);

                return createdJob.Metadata?.Name
                    ?? throw new InvalidOperationException($"Kubernetes created a Job from CronJob '{cronJobName}' without returning a name.");
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Kubernetes denied creating Jobs in namespace '{ns}'. Ensure the current identity has batch/v1 Job create permission.",
                ex);
        }
    }

    public async Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var sourceJob = await _client.BatchV1.ReadNamespacedJobAsync(jobName, ns, cancellationToken: ct).ConfigureAwait(false);
                var createdJob = await _client.BatchV1.CreateNamespacedJobAsync(
                    BuildTriggeredJobFromJob(sourceJob, ns),
                    ns,
                    cancellationToken: ct).ConfigureAwait(false);

                return createdJob.Metadata?.Name
                    ?? throw new InvalidOperationException($"Kubernetes reran Job '{jobName}' without returning a created Job name.");
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Kubernetes denied creating Jobs in namespace '{ns}'. Ensure the current identity has batch/v1 Job create permission.",
                ex);
        }
    }

    public async Task SuspendCronJobAsync(string ns, string cronJobName, bool suspend, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patch = new V1CronJob { Spec = new V1CronJobSpec { Suspend = suspend } };
            await _client.BatchV1.PatchNamespacedCronJobAsync(
                new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
                cronJobName, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SetJobParallelismAsync(string ns, string jobName, int parallelism, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patch = new V1Job { Spec = new V1JobSpec { Parallelism = parallelism } };
            await _client.BatchV1.PatchNamespacedJobAsync(
                new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
                jobName, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.BatchV1.ListNamespacedCronJobAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items.Select(cj => new CronJobInfo
            {
                Name = cj.Metadata.Name,
                Namespace = cj.Metadata.NamespaceProperty ?? ns,
                Schedule = cj.Spec?.Schedule,
                Suspend = cj.Spec?.Suspend ?? false,
                ActiveCount = cj.Status?.Active?.Count ?? 0,
                LastScheduleTime = cj.Status?.LastScheduleTime.HasValue == true
                    ? new DateTimeOffset(cj.Status.LastScheduleTime.Value)
                    : null,
                LastSuccessfulTime = cj.Status?.LastSuccessfulTime.HasValue == true
                    ? new DateTimeOffset(cj.Status.LastSuccessfulTime.Value)
                    : null,
                Labels = cj.Metadata.Labels is not null ? new Dictionary<string, string>(cj.Metadata.Labels) : []
            }).ToList();
        }).ConfigureAwait(false);
    }

    internal static JobInfo MapJobInfo(V1Job job, string fallbackNamespace)
    {
        var (sourceKind, sourceName) = GetJobSource(job.Metadata);
        return new JobInfo
        {
            Name = job.Metadata?.Name ?? string.Empty,
            Namespace = job.Metadata?.NamespaceProperty ?? fallbackNamespace,
            Status = DeriveJobStatus(job),
            Active = job.Status?.Active ?? 0,
            Succeeded = job.Status?.Succeeded ?? 0,
            Failed = job.Status?.Failed ?? 0,
            DesiredCompletions = job.Spec?.Completions,
            StartTime = job.Status?.StartTime.HasValue == true
                ? new DateTimeOffset(job.Status.StartTime.Value)
                : null,
            CompletionTime = job.Status?.CompletionTime.HasValue == true
                ? new DateTimeOffset(job.Status.CompletionTime.Value)
                : null,
            Parallelism = job.Spec?.Parallelism ?? 1,
            SourceKind = sourceKind,
            SourceName = sourceName,
            Labels = RemoveControllerOwnedJobLabels(job.Metadata?.Labels)
        };
    }

    internal static V1Job BuildTriggeredJobFromCronJob(V1CronJob cronJob, string ns)
    {
        var cronJobName = cronJob.Metadata?.Name;
        if (string.IsNullOrWhiteSpace(cronJobName))
            throw new InvalidOperationException("CronJob name is missing.");

        var jobSpec = DeepClone(cronJob.Spec?.JobTemplate?.Spec)
            ?? throw new InvalidOperationException($"CronJob '{cronJobName}' does not define a job template.");

        SanitizeJobSpecForCreate(jobSpec);

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = CreateTriggeredJobMetadata(
                ns,
                cronJobName,
                "CronJob",
                cronJob.Spec?.JobTemplate?.Metadata?.Labels,
                cronJob.Spec?.JobTemplate?.Metadata?.Annotations),
            Spec = jobSpec
        };
    }

    internal static V1Job BuildTriggeredJobFromJob(V1Job sourceJob, string ns)
    {
        var jobName = sourceJob.Metadata?.Name;
        if (string.IsNullOrWhiteSpace(jobName))
            throw new InvalidOperationException("Job name is missing.");

        var jobSpec = DeepClone(sourceJob.Spec)
            ?? throw new InvalidOperationException($"Job '{jobName}' does not define a spec.");

        SanitizeJobSpecForCreate(jobSpec);

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = CreateTriggeredJobMetadata(
                ns,
                jobName,
                "Job",
                sourceJob.Metadata?.Labels,
                sourceJob.Metadata?.Annotations),
            Spec = jobSpec
        };
    }

    internal static string DeriveJobStatus(V1Job job)
    {
        if (job.Status?.Conditions?.Any(condition =>
                string.Equals(condition.Type, "Failed", StringComparison.OrdinalIgnoreCase) &&
                IsJobConditionTrue(condition)) == true)
            return "Failed";

        if (job.Status?.Conditions?.Any(condition =>
                string.Equals(condition.Type, "Complete", StringComparison.OrdinalIgnoreCase) &&
                IsJobConditionTrue(condition)) == true)
            return "Succeeded";

        if (job.Spec?.Suspend == true)
            return "Suspended";

        if ((job.Status?.Active ?? 0) > 0)
            return "Active";

        if ((job.Status?.Succeeded ?? 0) > 0)
            return "Succeeded";

        if ((job.Status?.Failed ?? 0) > 0)
            return "Failed";

        return "Pending";
    }

    private static (string? SourceKind, string? SourceName) GetJobSource(V1ObjectMeta? metadata)
    {
        var ownerReference = metadata?.OwnerReferences?
            .FirstOrDefault(owner => owner.Controller == true &&
                                     !string.IsNullOrWhiteSpace(owner.Kind) &&
                                     !string.IsNullOrWhiteSpace(owner.Name))
            ?? metadata?.OwnerReferences?
                .FirstOrDefault(owner => !string.IsNullOrWhiteSpace(owner.Kind) &&
                                         !string.IsNullOrWhiteSpace(owner.Name));

        if (ownerReference is not null)
            return (ownerReference.Kind, ownerReference.Name);

        if (metadata?.Annotations is null)
            return (null, null);

        metadata.Annotations.TryGetValue(AksBatchAnnotations.SourceKind, out var sourceKind);
        metadata.Annotations.TryGetValue(AksBatchAnnotations.SourceName, out var sourceName);
        return (sourceKind, sourceName);
    }

    private static bool IsJobConditionTrue(V1JobCondition condition)
        => string.Equals(condition.Status, "True", StringComparison.OrdinalIgnoreCase);

    private static V1ObjectMeta CreateTriggeredJobMetadata(
        string ns,
        string sourceName,
        string sourceKind,
        IDictionary<string, string>? sourceLabels,
        IDictionary<string, string>? sourceAnnotations)
    {
        var labels = RemoveControllerOwnedJobLabels(sourceLabels);
        var annotations = RemoveControllerOwnedJobAnnotations(sourceAnnotations);
        annotations[AksBatchAnnotations.SourceKind] = sourceKind;
        annotations[AksBatchAnnotations.SourceName] = sourceName;

        return new V1ObjectMeta
        {
            NamespaceProperty = ns,
            GenerateName = BuildGeneratedJobNamePrefix(sourceName, sourceKind),
            Labels = labels.Count > 0 ? labels : null,
            Annotations = annotations.Count > 0 ? annotations : null
        };
    }

    private static void SanitizeJobSpecForCreate(V1JobSpec jobSpec)
    {
        jobSpec.ManualSelector = null;
        jobSpec.Selector = null;

        if (jobSpec.Template is null)
            throw new InvalidOperationException("Job spec is missing a pod template.");

        jobSpec.Template.Metadata ??= new V1ObjectMeta();
        jobSpec.Template.Metadata.Name = null;
        jobSpec.Template.Metadata.GenerateName = null;
        jobSpec.Template.Metadata.NamespaceProperty = null;
        jobSpec.Template.Metadata.ResourceVersion = null;
        jobSpec.Template.Metadata.Uid = null;
        jobSpec.Template.Metadata.CreationTimestamp = null;
        jobSpec.Template.Metadata.ManagedFields = null;
        jobSpec.Template.Metadata.OwnerReferences = null;
        jobSpec.Template.Metadata.Finalizers = null;
        jobSpec.Template.Metadata.Labels = RemoveControllerOwnedJobLabels(jobSpec.Template.Metadata.Labels);
        jobSpec.Template.Metadata.Annotations = RemoveControllerOwnedJobAnnotations(jobSpec.Template.Metadata.Annotations);
    }

    private static Dictionary<string, string> RemoveControllerOwnedJobLabels(IDictionary<string, string>? labels)
    {
        if (labels is null || labels.Count == 0)
            return [];

        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in labels)
        {
            if (ControllerOwnedJobLabelKeys.Contains(entry.Key))
                continue;

            sanitized[entry.Key] = entry.Value;
        }

        return sanitized;
    }

    private static Dictionary<string, string> RemoveControllerOwnedJobAnnotations(IDictionary<string, string>? annotations)
    {
        if (annotations is null || annotations.Count == 0)
            return [];

        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in annotations)
        {
            if (ControllerOwnedJobAnnotationKeys.Contains(entry.Key))
                continue;

            sanitized[entry.Key] = entry.Value;
        }

        return sanitized;
    }

    internal static string BuildGeneratedJobNamePrefix(string sourceName, string sourceKind)
    {
        var operation = string.Equals(sourceKind, "CronJob", StringComparison.OrdinalIgnoreCase)
            ? "manual"
            : "rerun";

        var sanitizedSourceName = SanitizeDnsLabel(sourceName);
        var suffix = $"-{operation}-";
        var maxSourceLength = Math.Max(1, MaxGeneratedJobNamePrefixLength - suffix.Length);

        if (sanitizedSourceName.Length > maxSourceLength)
            sanitizedSourceName = sanitizedSourceName[..maxSourceLength].TrimEnd('-');

        if (string.IsNullOrWhiteSpace(sanitizedSourceName))
            sanitizedSourceName = "job";

        return $"{sanitizedSourceName}{suffix}";
    }

    private static string SanitizeDnsLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "job";

        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch <= sbyte.MaxValue)
            {
                builder.Append(ch);
                previousWasDash = false;
                continue;
            }

            if (previousWasDash)
                continue;

            builder.Append('-');
            previousWasDash = true;
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "job" : sanitized;
    }

    private static T? DeepClone<T>(T? value)
    {
        if (value is null)
            return default;

        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }
}
