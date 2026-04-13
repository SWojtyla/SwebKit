using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using YamlDotNet.Serialization;

namespace SwebKit.Kubernetes.AksClient;

public class KubernetesAksClient : IAksClient, IAsyncDisposable
{
    private const string DefaultAksServerAppId = "6dae42f8-4368-4678-94ff-3960e28e3630";
    private const int MaxGeneratedJobNamePrefixLength = 52;
    private const string GatewayApiGroup = "gateway.networking.k8s.io";

    private static readonly string[] GatewayApiVersions = ["v1", "v1beta1", "v1alpha2"];

    private static readonly HashSet<string> ControllerOwnedJobLabelKeys =
    [
        "controller-uid",
        "batch.kubernetes.io/controller-uid",
        "job-name",
        "batch.kubernetes.io/job-name"
    ];

    private static readonly HashSet<string> ControllerOwnedJobAnnotationKeys =
    [
        AksBatchAnnotations.SourceKind,
        AksBatchAnnotations.SourceName,
        "cronjob.kubernetes.io/instantiate",
        "batch.kubernetes.io/cronjob-scheduled-timestamp"
    ];

    private static readonly DefaultAzureCredentialOptions AzureCredentialOptions = new()
    {
        ExcludeEnvironmentCredential = true,
        ExcludeWorkloadIdentityCredential = true,
        ExcludeManagedIdentityCredential = true,
        ExcludeAzureDeveloperCliCredential = true,
        ExcludeInteractiveBrowserCredential = true,
    };

    private k8s.Kubernetes _client;
    private readonly string? _kubeconfigPath;
    private readonly string? _kubeconfigContext;

    private readonly Dictionary<Guid, Process> _portForwardProcesses = [];
    private readonly Lock _portForwardLock = new();
    private readonly Lock _rebuildLock = new();
    private DateTime _lastRebuild = DateTime.MinValue;

    public KubernetesAksClient(
        string? kubeconfigContext = null,
        string? kubeconfigPath = null)
    {
        _kubeconfigPath = kubeconfigPath;
        _kubeconfigContext = kubeconfigContext;

        var config = BuildClientConfiguration(kubeconfigContext, kubeconfigPath);
        TryApplyAzureCredentialFallback(config, kubeconfigPath);

        _client = new k8s.Kubernetes(config);
    }

    private void RebuildClient()
    {
        lock (_rebuildLock)
        {
            if ((DateTime.UtcNow - _lastRebuild).TotalSeconds < 30)
                return;

            var config = BuildClientConfiguration(_kubeconfigContext, _kubeconfigPath);
            TryApplyAzureCredentialFallback(config, _kubeconfigPath);
            _client = new k8s.Kubernetes(config);
            _lastRebuild = DateTime.UtcNow;
        }
    }

    private async Task<T> WithAuthRetryAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            RebuildClient();
            return await action();
        }
    }

    private async Task WithAuthRetryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            RebuildClient();
            await action();
        }
    }

    internal static KubernetesClientConfiguration BuildClientConfiguration(string? kubeconfigContext, string? kubeconfigPath)
    {
        var hasExplicitKubeconfig = !string.IsNullOrWhiteSpace(kubeconfigPath);
        var hasExplicitContext = !string.IsNullOrWhiteSpace(kubeconfigContext);

        if (!hasExplicitKubeconfig && !hasExplicitContext)
            return KubernetesClientConfiguration.BuildDefaultConfig();

        return KubernetesClientConfiguration.BuildConfigFromConfigFile(
            hasExplicitKubeconfig ? kubeconfigPath : null,
            hasExplicitContext ? kubeconfigContext : null);
    }

    internal static void TryApplyAzureCredentialFallback(KubernetesClientConfiguration config, string? kubeconfigPath)
    {
        if (!AksAzureAuthHelpers.ShouldUseAzureCredentialFallback(config.Host, config.AccessToken))
            return;

        var effectiveKubeconfigPath = string.IsNullOrWhiteSpace(kubeconfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : kubeconfigPath;

        string? serverId = null;
        if (!string.IsNullOrWhiteSpace(effectiveKubeconfigPath) && File.Exists(effectiveKubeconfigPath))
        {
            var kubeconfigContent = File.ReadAllText(effectiveKubeconfigPath);
            serverId = AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(kubeconfigContent);
        }

        foreach (var scope in AksAzureAuthHelpers.BuildAksTokenScopes(serverId ?? DefaultAksServerAppId))
        {
            try
            {
                var credential = new DefaultAzureCredential(AzureCredentialOptions);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var accessToken = credential.GetToken(new TokenRequestContext([scope]), cts.Token);
                if (!string.IsNullOrWhiteSpace(accessToken.Token))
                {
                    config.AccessToken = accessToken.Token;
                    return;
                }
            }
            catch
            {
                // Keep kubeconfig-based auth as the primary mechanism and silently continue fallback attempts.
            }
        }
    }

    public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.AppsV1.ListNamespacedDeploymentAsync(ns, cancellationToken: ct);
            return result.Items.Select(d => new DeploymentInfo
            {
                Name = d.Metadata.Name,
                Namespace = d.Metadata.NamespaceProperty ?? ns,
                Replicas = d.Spec?.Replicas ?? 0,
                ReadyReplicas = d.Status?.ReadyReplicas ?? 0,
                Status = d.Status?.Conditions?.FirstOrDefault(c => c.Type == "Available")?.Status ?? "Unknown",
                Labels = d.Metadata.Labels is not null ? new Dictionary<string, string>(d.Metadata.Labels) : [],
                SelectorLabels = d.Spec?.Selector?.MatchLabels is not null
                    ? new Dictionary<string, string>(d.Spec.Selector.MatchLabels)
                    : []
            }).ToList();
        });
    }

    public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: labelSelector, cancellationToken: ct);
            return result.Items.Select(p =>
            {
                var containerStatuses = p.Status?.ContainerStatuses;
                var lastTerminated = containerStatuses?
                    .Select(c => c.LastState?.Terminated)
                    .Where(t => t?.FinishedAt is not null)
                    .OrderByDescending(t => t!.FinishedAt)
                    .FirstOrDefault();

                return new PodInfo
                {
                    Name = p.Metadata.Name,
                    Namespace = p.Metadata.NamespaceProperty ?? ns,
                    Phase = p.Status?.Phase ?? "Unknown",
                    Status = DeriveDisplayStatus(p),
                    Ready = containerStatuses?.All(c => c.Ready) ?? false,
                    ReadyContainers = containerStatuses?.Count(c => c.Ready) ?? 0,
                    TotalContainers = p.Spec?.Containers?.Count ?? 0,
                    RestartCount = containerStatuses?.Sum(c => c.RestartCount) ?? 0,
                    LastRestartTime = lastTerminated?.FinishedAt is { } fin ? new DateTimeOffset(fin) : null,
                    LastRestartReason = lastTerminated?.Reason,
                    PodIP = p.Status?.PodIP,
                    NodeName = p.Spec?.NodeName,
                    StartTime = p.Status?.StartTime.HasValue == true ? new DateTimeOffset(p.Status.StartTime.Value) : null,
                    Containers = p.Spec?.Containers?.Select(c => c.Name).ToList() ?? [],
                    Labels = p.Metadata.Labels is not null ? new Dictionary<string, string>(p.Metadata.Labels) : []
                };
            }).ToList();
        });
    }

    /// <summary>
    /// Derives the display status matching kubectl output from container states.
    /// Priority: DeletionTimestamp → init container waiting reason → container waiting reason →
    /// container terminated reason → pod phase.
    /// </summary>
    private static string DeriveDisplayStatus(V1Pod pod)
    {
        if (pod.Metadata?.DeletionTimestamp is not null)
            return "Terminating";

        var phase = pod.Status?.Phase ?? "Unknown";

        if (pod.Status?.InitContainerStatuses is { } initStatuses)
        {
            foreach (var cs in initStatuses)
            {
                if (cs.State?.Waiting?.Reason is { } initWaitReason)
                    return $"Init:{initWaitReason}";
                if (cs.State?.Terminated is { } initTerm && initTerm.ExitCode != 0)
                    return initTerm.Reason ?? $"Init:ExitCode:{initTerm.ExitCode}";
            }
        }

        if (pod.Status?.ContainerStatuses is { } statuses)
        {
            foreach (var cs in statuses)
            {
                if (cs.State?.Waiting?.Reason is { } waitReason)
                    return waitReason;
            }

            foreach (var cs in statuses)
            {
                if (cs.State?.Terminated is { } term)
                {
                    if (term.Reason is { Length: > 0 } reason)
                        return reason;
                    if (term.Signal is not null and not 0)
                        return $"Signal:{term.Signal}";
                    if (term.ExitCode != 0)
                        return $"ExitCode:{term.ExitCode}";
                }
            }
        }

        return phase;
    }

    public async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var fieldSelector = involvedObjectName is not null
                ? $"involvedObject.name={involvedObjectName}"
                : null;
            var result = await _client.CoreV1.ListNamespacedEventAsync(ns, fieldSelector: fieldSelector, cancellationToken: ct);
            return result.Items
                .OrderByDescending(e => e.LastTimestamp)
                .Select(e => new KubernetesEvent
                {
                    Name = e.Metadata.Name,
                    Namespace = e.Metadata.NamespaceProperty ?? ns,
                    Type = e.Type ?? "Normal",
                    Reason = e.Reason,
                    Message = e.Message,
                    InvolvedObjectName = e.InvolvedObject?.Name,
                    InvolvedObjectKind = e.InvolvedObject?.Kind,
                    LastTimestamp = e.LastTimestamp.HasValue ? new DateTimeOffset(e.LastTimestamp.Value) : null,
                    Count = e.Count ?? 1
                }).ToList();
        });
    }

    public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.NetworkingV1.ListNamespacedIngressAsync(ns, cancellationToken: ct);
            return result.Items.Select(ing => new IngressInfo
            {
                Name = ing.Metadata.Name,
                Namespace = ing.Metadata.NamespaceProperty ?? ns,
                IngressClass = ing.Spec?.IngressClassName,
                Rules = ing.Spec?.Rules?.Select(r => new IngressRule
                {
                    Host = r.Host,
                    Paths = r.Http?.Paths?.Select(p => new IngressPath
                    {
                        Path = p.Path ?? "/",
                        PathType = p.PathType,
                        ServiceName = p.Backend?.Service?.Name,
                        ServicePort = p.Backend?.Service?.Port?.Number
                    }).ToList() ?? []
                }).ToList() ?? [],
                Addresses = ing.Status?.LoadBalancer?.Ingress?.Select(i => i.Ip ?? i.Hostname ?? "").Where(a => a != "").ToList() ?? [],
                Labels = ing.Metadata.Labels is not null ? new Dictionary<string, string>(ing.Metadata.Labels) : []
            }).ToList();
        });
    }

    public async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await ListGatewayApiCustomObjectsAsync(ns, "gateways", ct);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapGateways(doc.RootElement, ns);
        });
    }

    public async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await ListGatewayApiCustomObjectsAsync(ns, "httproutes", ct);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapHttpRoutes(doc.RootElement, ns);
        });
    }

    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct);
            return result.Items.Select(n => n.Metadata.Name).OrderBy(n => n).ToList();
        });
    }

    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
    {
        var kubeconfigPath = string.IsNullOrWhiteSpace(_kubeconfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : _kubeconfigPath;

        var contexts = new List<KubeContextInfo>();
        if (string.IsNullOrWhiteSpace(kubeconfigPath) || !File.Exists(kubeconfigPath))
            return Task.FromResult<IReadOnlyList<KubeContextInfo>>(contexts);

        var config = KubernetesClientConfiguration.LoadKubeConfig(kubeconfigPath);
        var currentContext = config.CurrentContext;

        foreach (var ctx in config.Contexts ?? [])
        {
            contexts.Add(new KubeContextInfo
            {
                Name = ctx.Name,
                Cluster = ctx.ContextDetails?.Cluster,
                User = ctx.ContextDetails?.User,
                Namespace = ctx.ContextDetails?.Namespace,
                IsCurrent = string.Equals(ctx.Name, currentContext, StringComparison.Ordinal)
            });
        }

        return Task.FromResult<IReadOnlyList<KubeContextInfo>>(contexts.OrderBy(c => c.Name).ToList());
    }

    public async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            // Helm stores releases as Secrets with type=helm.sh/release.v1 and label owner=helm
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: "owner=helm", cancellationToken: ct);

            var releases = new Dictionary<string, HelmReleaseInfo>();
            foreach (var secret in secrets.Items)
            {
                var labels = secret.Metadata.Labels;
                var name = (labels is not null && labels.TryGetValue("name", out var n) ? n : null) ?? secret.Metadata.Name;
                var version = labels is not null && labels.TryGetValue("version", out var ver) && int.TryParse(ver, out var v) ? v : 1;
                var status = (labels is not null && labels.TryGetValue("status", out var s) ? s : null) ?? "unknown";
                var chart = labels is not null && labels.TryGetValue("chart", out var c) ? c : null;

                // Keep only the latest revision per release name
                if (releases.TryGetValue(name, out var existing) && existing.Revision >= version)
                    continue;

                var chartVersion = TryParseChartVersion(chart);

                releases[name] = new HelmReleaseInfo
                {
                    Name = name,
                    Namespace = ns,
                    Chart = chart,
                    ChartVersion = chartVersion,
                    Revision = version,
                    Status = status,
                    Updated = secret.Metadata.CreationTimestamp.HasValue
                        ? new DateTimeOffset(secret.Metadata.CreationTimestamp.Value)
                        : null
                };
            }

            return releases.Values.OrderBy(r => r.Name).ToList();
        });
    }

    /// <summary>
    /// Extracts the version portion from a Helm chart label value (e.g. "ingress-nginx-4.9.1" → "4.9.1").
    /// </summary>
    internal static string? TryParseChartVersion(string? chart)
    {
        if (string.IsNullOrWhiteSpace(chart))
            return null;

        // Helm chart labels use format "chart-name-X.Y.Z". Find the last hyphen before a digit sequence.
        for (var i = chart.Length - 1; i >= 0; i--)
        {
            if (chart[i] == '-' && i + 1 < chart.Length && char.IsDigit(chart[i + 1]))
                return chart[(i + 1)..];
        }

        return null;
    }

    public async Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
    {
        if (kind.Equals("helm", StringComparison.OrdinalIgnoreCase))
            return await GetHelmManifestAsync(ns, name, ct);

        if (kind.Equals("gateway", StringComparison.OrdinalIgnoreCase))
        {
            return await WithAuthRetryAsync(async () =>
                SerializeCustomObjectYaml(await ReadGatewayApiCustomObjectAsync(ns, "gateways", name, ct)));
        }

        if (kind.Equals("httproute", StringComparison.OrdinalIgnoreCase))
        {
            return await WithAuthRetryAsync(async () =>
                SerializeCustomObjectYaml(await ReadGatewayApiCustomObjectAsync(ns, "httproutes", name, ct)));
        }

        return await WithAuthRetryAsync(async () =>
        {
            object resource = kind.ToLowerInvariant() switch
            {
                "deployment" => await _client.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct),
                "pod" => await _client.CoreV1.ReadNamespacedPodAsync(name, ns, cancellationToken: ct),
                "ingress" => await _client.NetworkingV1.ReadNamespacedIngressAsync(name, ns, cancellationToken: ct),
                "service" => await _client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct),
                "statefulset" => await _client.AppsV1.ReadNamespacedStatefulSetAsync(name, ns, cancellationToken: ct),
                "configmap" => await _client.CoreV1.ReadNamespacedConfigMapAsync(name, ns, cancellationToken: ct),
                "secret" => await _client.CoreV1.ReadNamespacedSecretAsync(name, ns, cancellationToken: ct),
                "horizontalpodautoscaler" or "hpa" => await _client.AutoscalingV2.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct),
                "job" => await _client.BatchV1.ReadNamespacedJobAsync(name, ns, cancellationToken: ct),
                "cronjob" => await _client.BatchV1.ReadNamespacedCronJobAsync(name, ns, cancellationToken: ct),
                _ => throw new ArgumentException($"Unsupported resource kind: {kind}")
            };

            return KubernetesYaml.Serialize(resource);
        });
    }

    private async Task<object?> ListGatewayApiCustomObjectsAsync(string ns, string plural, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    ns,
                    plural,
                    cancellationToken: ct);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return null;
    }

    private async Task<object> ReadGatewayApiCustomObjectAsync(string ns, string plural, string name, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.GetNamespacedCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    ns,
                    plural,
                    name,
                    cancellationToken: ct);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        throw new InvalidOperationException(
            $"Gateway API resource '{plural}/{name}' is not available in namespace '{ns}'.");
    }

    private static List<GatewayInfo> MapGateways(JsonElement root, string fallbackNamespace)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var gateways = new List<GatewayInfo>();

        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var listenerRouteCounts = GetGatewayListenerRouteCounts(item);
            var listeners = GetGatewayListeners(item, listenerRouteCounts);
            var addresses = GetGatewayAddresses(item);

            gateways.Add(new GatewayInfo
            {
                Name = name,
                Namespace = GetMetadataNamespace(item, fallbackNamespace),
                GatewayClassName = TryGetProperty(item, "spec", out var spec)
                    ? GetStringProperty(spec, "gatewayClassName")
                    : null,
                Status = GetGatewayStatus(item, addresses),
                AttachedRoutes = listeners.Sum(listener => listener.AttachedRoutes),
                Addresses = addresses,
                Listeners = listeners,
                Labels = GetMetadataLabels(item)
            });
        }

        return gateways
            .OrderBy(gateway => gateway.Namespace, StringComparer.Ordinal)
            .ThenBy(gateway => gateway.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<HttpRouteInfo> MapHttpRoutes(JsonElement root, string fallbackNamespace)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var routes = new List<HttpRouteInfo>();

        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var routeNamespace = GetMetadataNamespace(item, fallbackNamespace);
            routes.Add(new HttpRouteInfo
            {
                Name = name,
                Namespace = routeNamespace,
                Status = GetHttpRouteStatus(item),
                Hostnames = GetHttpRouteHostnames(item),
                ParentRefs = GetHttpRouteParentRefs(item, routeNamespace),
                BackendRefs = GetHttpRouteBackendRefs(item, routeNamespace),
                Labels = GetMetadataLabels(item)
            });
        }

        return routes
            .OrderBy(route => route.Namespace, StringComparer.Ordinal)
            .ThenBy(route => route.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<GatewayListenerInfo> GetGatewayListeners(
        JsonElement item,
        IReadOnlyDictionary<string, int> listenerRouteCounts)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "listeners", out var listeners)
            || listeners.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<GatewayListenerInfo>();
        foreach (var listener in listeners.EnumerateArray())
        {
            var listenerName = GetStringProperty(listener, "name");
            if (string.IsNullOrWhiteSpace(listenerName))
                continue;

            results.Add(new GatewayListenerInfo
            {
                Name = listenerName,
                Port = GetIntProperty(listener, "port"),
                Protocol = GetStringProperty(listener, "protocol"),
                Hostname = GetStringProperty(listener, "hostname"),
                AttachedRoutes = listenerRouteCounts.TryGetValue(listenerName, out var attachedRoutes)
                    ? attachedRoutes
                    : 0
            });
        }

        return results;
    }

    private static Dictionary<string, int> GetGatewayListenerRouteCounts(JsonElement item)
    {
        var results = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "listeners", out var listeners)
            || listeners.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var listener in listeners.EnumerateArray())
        {
            var listenerName = GetStringProperty(listener, "name");
            if (string.IsNullOrWhiteSpace(listenerName))
                continue;

            results[listenerName] = GetIntProperty(listener, "attachedRoutes");
        }

        return results;
    }

    private static List<string> GetGatewayAddresses(JsonElement item)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "addresses", out var addresses)
            || addresses.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return addresses.EnumerateArray()
            .Select(address => GetStringProperty(address, "value"))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetGatewayStatus(JsonElement item, IReadOnlyList<string> addresses)
    {
        if (HasTopLevelCondition(item, "Programmed"))
            return "Programmed";

        if (HasTopLevelCondition(item, "Accepted"))
            return "Accepted";

        if (TryGetFirstTopLevelFailingCondition(item, out var failingCondition))
            return failingCondition;

        return addresses.Count > 0 ? "Addressed" : "Pending";
    }

    private static List<string> GetHttpRouteHostnames(JsonElement item)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "hostnames", out var hostnames)
            || hostnames.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return hostnames.EnumerateArray()
            .Where(hostname => hostname.ValueKind == JsonValueKind.String)
            .Select(hostname => hostname.GetString())
            .Where(hostname => !string.IsNullOrWhiteSpace(hostname))
            .Select(hostname => hostname!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetHttpRouteParentRefs(JsonElement item, string routeNamespace)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "parentRefs", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return parents.EnumerateArray()
            .Select(parent => FormatParentRef(parent, routeNamespace))
            .Where(parent => !string.IsNullOrWhiteSpace(parent))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatParentRef(JsonElement parent, string routeNamespace)
    {
        var name = GetStringProperty(parent, "name");
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var kind = GetStringProperty(parent, "kind");
        var parentNamespace = GetStringProperty(parent, "namespace");
        var sectionName = GetStringProperty(parent, "sectionName");

        var prefix = !string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, "Gateway", StringComparison.OrdinalIgnoreCase)
            ? $"{kind}/"
            : string.Empty;
        var namespacePrefix = !string.IsNullOrWhiteSpace(parentNamespace)
            && !string.Equals(parentNamespace, routeNamespace, StringComparison.Ordinal)
            ? $"{parentNamespace}/"
            : string.Empty;

        return $"{prefix}{namespacePrefix}{name}{(string.IsNullOrWhiteSpace(sectionName) ? string.Empty : $"#{sectionName}")}";
    }

    private static List<string> GetHttpRouteBackendRefs(JsonElement item, string routeNamespace)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "rules", out var rules)
            || rules.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var backends = new List<string>();

        foreach (var rule in rules.EnumerateArray())
        {
            if (!TryGetProperty(rule, "backendRefs", out var backendRefs)
                || backendRefs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var backend in backendRefs.EnumerateArray())
            {
                var formatted = FormatBackendRef(backend, routeNamespace);
                if (!string.IsNullOrWhiteSpace(formatted))
                    backends.Add(formatted);
            }
        }

        return backends.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FormatBackendRef(JsonElement backend, string routeNamespace)
    {
        var name = GetStringProperty(backend, "name");
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var kind = GetStringProperty(backend, "kind");
        var backendNamespace = GetStringProperty(backend, "namespace");
        var port = TryGetIntProperty(backend, "port");

        var prefix = !string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, "Service", StringComparison.OrdinalIgnoreCase)
            ? $"{kind}/"
            : string.Empty;
        var namespacePrefix = !string.IsNullOrWhiteSpace(backendNamespace)
            && !string.Equals(backendNamespace, routeNamespace, StringComparison.Ordinal)
            ? $"{backendNamespace}/"
            : string.Empty;

        return $"{prefix}{namespacePrefix}{name}{(port.HasValue ? $":{port.Value}" : string.Empty)}";
    }

    private static string GetHttpRouteStatus(JsonElement item)
    {
        if (HasParentCondition(item, "Accepted"))
            return "Accepted";

        if (HasParentCondition(item, "ResolvedRefs"))
            return "ResolvedRefs";

        if (TryGetFirstFailingParentCondition(item, out var failingCondition))
            return failingCondition;

        return "Pending";
    }

    private static bool HasTopLevelCondition(JsonElement item, string type)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "conditions", out var conditions)
            || conditions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return conditions.EnumerateArray().Any(condition =>
            string.Equals(GetStringProperty(condition, "type"), type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetFirstTopLevelFailingCondition(JsonElement item, out string conditionType)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "conditions", out var conditions)
            || conditions.ValueKind != JsonValueKind.Array)
        {
            conditionType = string.Empty;
            return false;
        }

        foreach (var condition in conditions.EnumerateArray())
        {
            if (!string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase))
            {
                conditionType = GetStringProperty(condition, "type") ?? "Pending";
                return true;
            }
        }

        conditionType = string.Empty;
        return false;
    }

    private static bool HasParentCondition(JsonElement item, string type)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var parent in parents.EnumerateArray())
        {
            if (!TryGetProperty(parent, "conditions", out var conditions)
                || conditions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (conditions.EnumerateArray().Any(condition =>
                string.Equals(GetStringProperty(condition, "type"), type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFirstFailingParentCondition(JsonElement item, out string conditionType)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            conditionType = string.Empty;
            return false;
        }

        foreach (var parent in parents.EnumerateArray())
        {
            if (!TryGetProperty(parent, "conditions", out var conditions)
                || conditions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var condition in conditions.EnumerateArray())
            {
                if (!string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase))
                {
                    conditionType = GetStringProperty(condition, "type") ?? "Pending";
                    return true;
                }
            }
        }

        conditionType = string.Empty;
        return false;
    }

    private static string SerializeCustomObjectYaml(object resource)
    {
        var json = JsonSerializer.Serialize(resource);
        using var document = JsonDocument.Parse(json);
        var serializer = new SerializerBuilder().Build();
        return serializer.Serialize(ConvertJsonElement(document.RootElement));
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static bool TryGetProperty(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static int GetIntProperty(JsonElement parent, string propertyName)
        => TryGetIntProperty(parent, propertyName) ?? 0;

    private static int? TryGetIntProperty(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
            return intValue;

        return null;
    }

    private static string GetMetadataNamespace(JsonElement item, string fallbackNamespace)
    {
        if (TryGetProperty(item, "metadata", out var metadata))
        {
            var itemNamespace = GetStringProperty(metadata, "namespace");
            if (!string.IsNullOrWhiteSpace(itemNamespace))
                return itemNamespace;
        }

        return fallbackNamespace;
    }

    private static string? GetMetadataName(JsonElement item)
    {
        if (!TryGetProperty(item, "metadata", out var metadata))
            return null;

        return GetStringProperty(metadata, "name");
    }

    private static Dictionary<string, string> GetMetadataLabels(JsonElement item)
    {
        if (!TryGetProperty(item, "metadata", out var metadata)
            || !TryGetProperty(metadata, "labels", out var labels)
            || labels.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in labels.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    private async Task<string> GetHelmManifestAsync(string ns, string releaseName, CancellationToken ct)
    {
        var args = $"get manifest {releaseName} --namespace {ns}{BuildKubeconfigArgs()}";

        var psi = new ProcessStartInfo("helm")
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start helm process.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            if (IsForbiddenError(stderr))
            {
                var token = TryAcquireFreshAzureToken();
                if (token is not null)
                {
                    var retryPsi = new ProcessStartInfo("helm")
                    {
                        Arguments = $"get manifest {releaseName} --namespace {ns}{BuildKubeconfigArgs()} --kube-token {token}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var retryProcess = Process.Start(retryPsi)
                        ?? throw new InvalidOperationException("Failed to start helm process.");
                    var retryStdout = await retryProcess.StandardOutput.ReadToEndAsync(ct);
                    var retryStderr = await retryProcess.StandardError.ReadToEndAsync(ct);
                    await retryProcess.WaitForExitAsync(ct);

                    if (retryProcess.ExitCode != 0)
                        throw new InvalidOperationException($"helm get manifest failed after credential refresh (exit {retryProcess.ExitCode}): {retryStderr}");

                    return retryStdout;
                }
            }

            throw new InvalidOperationException($"helm get manifest failed (exit {process.ExitCode}): {stderr}");
        }

        return stdout;
    }

    public async IAsyncEnumerable<string> StreamPodLogsAsync(
        string ns, string podName, string container,
        LogStreamOptions opts, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
            podName, ns,
            container: string.IsNullOrEmpty(container) ? null : container,
            previous: opts.PreviousContainer,
            follow: opts.Follow,
            tailLines: opts.TailLines,
            sinceSeconds: opts.SinceSeconds,
            cancellationToken: ct);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (opts.TextFilter is null || line.Contains(opts.TextFilter, StringComparison.OrdinalIgnoreCase))
                yield return line;
        }
    }

    public Task<PortForwardSession> StartPortForwardAsync(
        string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default)
    {
        var session = new PortForwardSession
        {
            Namespace = ns,
            ResourceName = resourceName,
            LocalPort = localPort,
            RemotePort = remotePort,
            Status = PortForwardStatus.Starting
        };

        var psi = new ProcessStartInfo("kubectl")
        {
            Arguments = $"port-forward {resourceName} {localPort}:{remotePort} -n {ns}{BuildKubeconfigArgs()}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start kubectl port-forward.");

        var stderrBuffer = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.Contains("Forwarding from") == true)
            {
                session.Status = PortForwardStatus.Active;
                session.OnStatusChanged?.Invoke(session);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuffer.AppendLine(e.Data);
        };

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (session.Status is not PortForwardStatus.Stopping and not PortForwardStatus.Stopped)
            {
                session.Status = PortForwardStatus.Error;
                session.LastError = stderrBuffer.Length > 0 ? stderrBuffer.ToString().Trim() : "kubectl process exited unexpectedly.";
                session.OnStatusChanged?.Invoke(session);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_portForwardLock) _portForwardProcesses[session.SessionId] = process;
        return Task.FromResult(session);
    }

    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default)
    {
        session.Status = PortForwardStatus.Stopping;
        session.OnStatusChanged?.Invoke(session);

        lock (_portForwardLock)
        {
            if (_portForwardProcesses.Remove(session.SessionId, out var p) && !p.HasExited)
                p.Kill(entireProcessTree: true);
        }

        session.Status = PortForwardStatus.Stopped;
        session.OnStatusChanged?.Invoke(session);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_portForwardLock)
        {
            foreach (var (_, process) in _portForwardProcesses)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* Process may already be gone */ }
                process.Dispose();
            }
            _portForwardProcesses.Clear();
        }
        return ValueTask.CompletedTask;
    }

    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
    {
        var kubeconfigArgs = BuildKubeconfigArgs();
        var args = $"exec -it {podName} -n {ns} -c {container}{kubeconfigArgs} -- /bin/sh";
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"kubectl {args}") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k kubectl {args}") { UseShellExecute = true });
        }
        return Task.CompletedTask;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try { await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct); return true; }
        catch { return false; }
    }

    public async Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            // Equivalent to `kubectl rollout restart deployment/<name> -n <ns>`
            // Patches the pod template annotation with a restart timestamp.
            var patch = new k8s.Models.V1Deployment
            {
                Spec = new k8s.Models.V1DeploymentSpec
                {
                    Template = new k8s.Models.V1PodTemplateSpec
                    {
                        Metadata = new k8s.Models.V1ObjectMeta
                        {
                            Annotations = new Dictionary<string, string>
                            {
                                ["kubectl.kubernetes.io/restartedAt"] = DateTime.UtcNow.ToString("O")
                            }
                        }
                    }
                }
            };
            await _client.AppsV1.PatchNamespacedDeploymentAsync(
                new k8s.Models.V1Patch(patch, k8s.Models.V1Patch.PatchType.StrategicMergePatch),
                deploymentName, ns, cancellationToken: ct);
        });
    }

    public async Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            await _client.CoreV1.DeleteNamespacedPodAsync(podName, ns, cancellationToken: ct);
        });
    }

    public async Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            var patch = new k8s.Models.V1Deployment
            {
                Spec = new k8s.Models.V1DeploymentSpec
                {
                    Replicas = replicas
                }
            };
            await _client.AppsV1.PatchNamespacedDeploymentAsync(
                new k8s.Models.V1Patch(patch, k8s.Models.V1Patch.PatchType.StrategicMergePatch),
                deploymentName, ns, cancellationToken: ct);
        });
    }

    public async Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: $"owner=helm,name={releaseName}", cancellationToken: ct);

            var revisions = new List<HelmRevisionInfo>();
            foreach (var secret in secrets.Items)
            {
                var labels = secret.Metadata.Labels;
                var version = labels is not null && labels.TryGetValue("version", out var ver) && int.TryParse(ver, out var v) ? v : 1;
                var status = (labels is not null && labels.TryGetValue("status", out var s) ? s : null) ?? "unknown";
                var chart = labels is not null && labels.TryGetValue("chart", out var c) ? c : null;
                var chartVersion = TryParseChartVersion(chart);

                revisions.Add(new HelmRevisionInfo
                {
                    Revision = version,
                    Status = status,
                    Chart = chart,
                    AppVersion = chartVersion,
                    Updated = secret.Metadata.CreationTimestamp.HasValue
                        ? new DateTimeOffset(secret.Metadata.CreationTimestamp.Value)
                        : null,
                    Description = status switch
                    {
                        "deployed" => "Upgrade complete",
                        "superseded" => "Superseded by new release",
                        "failed" => "Upgrade failed",
                        _ => null
                    }
                });
            }

            return revisions.OrderBy(r => r.Revision).ToList();
        });
    }

    public async Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            // Find the latest release secret
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: $"owner=helm,name={releaseName}", cancellationToken: ct);

            var latest = secrets.Items
                .OrderByDescending(s =>
                {
                    var labels = s.Metadata.Labels;
                    return labels is not null && labels.TryGetValue("version", out var ver) && int.TryParse(ver, out var v) ? v : 0;
                })
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Helm release '{releaseName}' not found in namespace '{ns}'.");

            if (latest.Data is null || !latest.Data.TryGetValue("release", out var releaseData))
                return "# No values found";

            // Helm stores release data as base64 -> gzip -> base64 -> protobuf/json
            // The outer base64 is already decoded by the K8s client into byte[].
            // Inner layer is base64-encoded gzip data.
            var innerBase64 = Encoding.UTF8.GetString(releaseData);
            try
            {
                var gzipBytes = Convert.FromBase64String(innerBase64);
                using var gzipStream = new GZipStream(
                    new MemoryStream(gzipBytes), CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(ct);

                // Extract the "config" field which contains the user-supplied values
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("config", out var config))
                    return System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                return json;
            }
            catch
            {
                return "# Unable to decode release values";
            }
        });
    }

    public async Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("helm")
        {
            Arguments = $"rollback {releaseName} {targetRevision} --namespace {ns} --wait{BuildKubeconfigArgs()}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start helm process. Ensure 'helm' is on PATH.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            if (IsForbiddenError(stderr))
            {
                var token = TryAcquireFreshAzureToken();
                if (token is not null)
                {
                    var retryPsi = new ProcessStartInfo("helm")
                    {
                        Arguments = $"rollback {releaseName} {targetRevision} --namespace {ns} --wait{BuildKubeconfigArgs()} --kube-token {token}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var retryProcess = Process.Start(retryPsi)
                        ?? throw new InvalidOperationException("Failed to start helm process. Ensure 'helm' is on PATH.");

                    var retryStderr = await retryProcess.StandardError.ReadToEndAsync(ct);
                    await retryProcess.WaitForExitAsync(ct);

                    if (retryProcess.ExitCode != 0)
                        throw new InvalidOperationException($"Helm rollback failed after credential refresh (exit code {retryProcess.ExitCode}): {retryStderr}");

                    return;
                }
            }

            throw new InvalidOperationException($"Helm rollback failed (exit code {process.ExitCode}): {stderr}");
        }
    }

    public async Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"swebkit-apply-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(tempFile, yaml, ct);
        try
        {
            var (exitCode, stderr) = await RunKubectlApplyAsync(tempFile, ns, ct);

            if (exitCode != 0)
            {
                if (IsForbiddenError(stderr))
                {
                    var token = TryAcquireFreshAzureToken();
                    if (token is not null)
                    {
                        var (retryExit, retryStderr) = await RunKubectlApplyWithTokenAsync(tempFile, ns, token, ct);
                        if (retryExit != 0)
                            throw new InvalidOperationException($"kubectl apply failed after credential refresh (exit {retryExit}): {retryStderr}");
                    }
                    else
                    {
                        throw new InvalidOperationException($"kubectl apply failed (exit {exitCode}): {stderr}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"kubectl apply failed (exit {exitCode}): {stderr}");
                }
            }
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    private async Task<(int ExitCode, string Stderr)> RunKubectlApplyAsync(string tempFile, string ns, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("kubectl")
        {
            Arguments = $"apply -f \"{tempFile}\" --namespace {ns}{BuildKubeconfigArgs()}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start kubectl. Ensure 'kubectl' is on PATH.");

        await process.WaitForExitAsync(ct);

        var stderr = process.ExitCode != 0
            ? await process.StandardError.ReadToEndAsync(ct)
            : string.Empty;

        return (process.ExitCode, stderr);
    }

    private static bool IsForbiddenError(string stderr)
        => stderr.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);

    private string BuildKubeconfigArgs()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_kubeconfigPath))
            sb.Append($" --kubeconfig \"{_kubeconfigPath}\"");
        if (!string.IsNullOrWhiteSpace(_kubeconfigContext))
            sb.Append($" --context {_kubeconfigContext}");
        return sb.ToString();
    }

    private async Task<(int ExitCode, string Stderr)> RunKubectlApplyWithTokenAsync(string tempFile, string ns, string token, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("kubectl")
        {
            Arguments = $"apply -f \"{tempFile}\" --namespace {ns}{BuildKubeconfigArgs()} --token {token}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start kubectl. Ensure 'kubectl' is on PATH.");

        await process.WaitForExitAsync(ct);

        var stderr = process.ExitCode != 0
            ? await process.StandardError.ReadToEndAsync(ct)
            : string.Empty;

        return (process.ExitCode, stderr);
    }

    private string? TryAcquireFreshAzureToken()
    {
        string? serverId = null;
        var effectiveKubeconfigPath = string.IsNullOrWhiteSpace(_kubeconfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : _kubeconfigPath;

        if (!string.IsNullOrWhiteSpace(effectiveKubeconfigPath) && File.Exists(effectiveKubeconfigPath))
        {
            var kubeconfigContent = File.ReadAllText(effectiveKubeconfigPath);
            serverId = AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(kubeconfigContent);
        }

        foreach (var scope in AksAzureAuthHelpers.BuildAksTokenScopes(serverId ?? DefaultAksServerAppId))
        {
            try
            {
                var credential = new DefaultAzureCredential(AzureCredentialOptions);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var accessToken = credential.GetToken(new TokenRequestContext([scope]), cts.Token);
                if (!string.IsNullOrWhiteSpace(accessToken.Token))
                    return accessToken.Token;
            }
            catch { }
        }

        return null;
    }

    public async Task<IReadOnlyList<Core.Models.PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            try
            {
                var result = await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    "metrics.k8s.io", "v1beta1", ns, "pods", cancellationToken: ct);

                var json = System.Text.Json.JsonSerializer.Serialize(result);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                var metrics = new List<Core.Models.PodMetrics>();
                if (!doc.RootElement.TryGetProperty("items", out var items))
                    return metrics;

                foreach (var item in items.EnumerateArray())
                {
                    var podName = item.GetProperty("metadata").GetProperty("name").GetString()!;
                    var podNs = item.GetProperty("metadata").GetProperty("namespace").GetString()!;
                    var containers = new List<Core.Models.ContainerMetrics>();

                    if (item.TryGetProperty("containers", out var containersEl))
                    {
                        foreach (var c in containersEl.EnumerateArray())
                        {
                            var name = c.GetProperty("name").GetString()!;
                            var cpuStr = c.GetProperty("usage").GetProperty("cpu").GetString() ?? "0";
                            var memStr = c.GetProperty("usage").GetProperty("memory").GetString() ?? "0";

                            containers.Add(new Core.Models.ContainerMetrics
                            {
                                Name = name,
                                CpuCores = ParseCpuToMillicores(cpuStr),
                                MemoryBytes = ParseMemoryToBytes(memStr)
                            });
                        }
                    }

                    metrics.Add(new Core.Models.PodMetrics
                    {
                        PodName = podName,
                        Namespace = podNs,
                        Containers = containers
                    });
                }

                return metrics;
            }
            catch (k8s.Autorest.HttpOperationException) { throw; }
            catch
            {
                // Metrics API not installed or unavailable — return empty list
                return [];
            }
        });
    }

    // ── Feature 1: Multi-pod log aggregation ─────────────────────────────────

    public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
        string ns, string deploymentName, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Resolve pods via selector from deployment spec — authoritative, not name-based
        var deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, ns, cancellationToken: ct);
        var matchLabels = deployment.Spec?.Selector?.MatchLabels;
        var labelSelector = matchLabels is not null
            ? string.Join(",", matchLabels.Select(kv => $"{kv.Key}={kv.Value}"))
            : $"app={deploymentName}";

        var pods = await GetPodsAsync(ns, labelSelector, ct);
        if (pods.Count == 0) yield break;

        var channel = Channel.CreateUnbounded<AggregatedLogLine>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        int remainingCount = pods.Count;

        foreach (var pod in pods)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var container = pod.Containers.FirstOrDefault() ?? string.Empty;
                    await foreach (var line in StreamPodLogsAsync(ns, pod.Name, container, opts, linkedCts.Token))
                    {
                        await channel.Writer.WriteAsync(
                            new AggregatedLogLine { PodName = pod.Name, Line = line },
                            linkedCts.Token);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Pod-specific stream ended (e.g. pod restarted) — not overall cancellation.
                    // Let the countdown handle completion.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[StreamDeploymentLogs] Pod '{pod.Name}' stream failed: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (Interlocked.Decrement(ref remainingCount) == 0)
                        channel.Writer.TryComplete();
                }
            }, linkedCts.Token);
        }

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    // ── Feature 2: StatefulSets ───────────────────────────────────────────────

    public async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.AppsV1.ListNamespacedStatefulSetAsync(ns, cancellationToken: ct);
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
        });
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
                name, ns, cancellationToken: ct);
        });
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
                name, ns, cancellationToken: ct);
        });
    }

    // ── Feature 3: ConfigMaps and Secrets ────────────────────────────────────

    public async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedConfigMapAsync(ns, cancellationToken: ct);
            return result.Items.Select(cm => new ConfigMapInfo
            {
                Name = cm.Metadata.Name,
                Namespace = cm.Metadata.NamespaceProperty ?? ns,
                Data = cm.Data is not null ? new Dictionary<string, string>(cm.Data) : [],
                Labels = cm.Metadata.Labels is not null ? new Dictionary<string, string>(cm.Metadata.Labels) : []
            }).ToList();
        });
    }

    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedSecretAsync(ns, cancellationToken: ct);
            return result.Items
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
        });
    }

    public async Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(name, ns, cancellationToken: ct);
            if (secret.Data is null) return [];
            return secret.Data.ToDictionary(
                kv => kv.Key,
                kv => Encoding.UTF8.GetString(kv.Value));
        });
    }

    // ── Feature 4: Container details ─────────────────────────────────────────

    public async Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
        string ns, string podName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var pod = await _client.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: ct);
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
                    var cm = await _client.CoreV1.ReadNamespacedConfigMapAsync(cmName, ns, cancellationToken: ct);
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
        });
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
            try
            {
                var result = await _client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(ns, cancellationToken: ct);
                return result.Items.Select(MapHpaV2).ToList();
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Fall back to autoscaling/v1 on older clusters
                var result = await _client.AutoscalingV1.ListNamespacedHorizontalPodAutoscalerAsync(ns, cancellationToken: ct);
                return result.Items.Select(hpa => new HpaInfo
                {
                    Name = hpa.Metadata.Name,
                    Namespace = hpa.Metadata.NamespaceProperty ?? ns,
                    TargetKind = hpa.Spec?.ScaleTargetRef?.Kind ?? "Deployment",
                    TargetName = hpa.Spec?.ScaleTargetRef?.Name ?? string.Empty,
                    MinReplicas = hpa.Spec?.MinReplicas ?? 1,
                    MaxReplicas = hpa.Spec?.MaxReplicas ?? 1,
                    CurrentReplicas = hpa.Status?.CurrentReplicas ?? 0,
                    DesiredReplicas = hpa.Status?.DesiredReplicas ?? 0,
                    CurrentCpuUtilizationPercent = hpa.Status?.CurrentCPUUtilizationPercentage,
                    TargetCpuUtilizationPercent = hpa.Spec?.TargetCPUUtilizationPercentage
                }).ToList();
            }
        });
    }

    private HpaInfo MapHpaV2(V2HorizontalPodAutoscaler hpa)
    {
        var ns = hpa.Metadata.NamespaceProperty ?? string.Empty;
        var cpuMetric = hpa.Status?.CurrentMetrics
            ?.FirstOrDefault(m => m.Type == "Resource" && m.Resource?.Name == "cpu");
        var cpuTarget = hpa.Spec?.Metrics
            ?.FirstOrDefault(m => m.Type == "Resource" && m.Resource?.Name == "cpu");

        return new HpaInfo
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
        if (mem.EndsWith("Ki"))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var ki) ? ki * 1024 : 0;
        if (mem.EndsWith("Mi"))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mi) ? mi * 1024 * 1024 : 0;
        if (mem.EndsWith("Gi"))
            return long.TryParse(mem[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var gi) ? gi * 1024 * 1024 * 1024 : 0;
        return long.TryParse(mem, NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes) ? bytes : 0;
    }

    // ── Jobs and CronJobs ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.BatchV1.ListNamespacedJobAsync(ns, cancellationToken: ct);
            return result.Items
                .Select(job => MapJobInfo(job, ns))
                .OrderByDescending(job => job.StartTime ?? DateTimeOffset.MinValue)
                .ThenBy(job => job.Name, StringComparer.Ordinal)
                .ToList();
        });
    }

    public async Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var cronJob = await _client.BatchV1.ReadNamespacedCronJobAsync(cronJobName, ns, cancellationToken: ct);
                var createdJob = await _client.BatchV1.CreateNamespacedJobAsync(
                    BuildTriggeredJobFromCronJob(cronJob, ns),
                    ns,
                    cancellationToken: ct);

                return createdJob.Metadata?.Name
                    ?? throw new InvalidOperationException($"Kubernetes created a Job from CronJob '{cronJobName}' without returning a name.");
            });
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
                var sourceJob = await _client.BatchV1.ReadNamespacedJobAsync(jobName, ns, cancellationToken: ct);
                var createdJob = await _client.BatchV1.CreateNamespacedJobAsync(
                    BuildTriggeredJobFromJob(sourceJob, ns),
                    ns,
                    cancellationToken: ct);

                return createdJob.Metadata?.Name
                    ?? throw new InvalidOperationException($"Kubernetes reran Job '{jobName}' without returning a created Job name.");
            });
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

    public async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.BatchV1.ListNamespacedCronJobAsync(ns, cancellationToken: ct);
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
        });
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

internal static class AksAzureAuthHelpers
{
    private static readonly Regex ServerIdRegex = new(
        "--server-id(?:=|\\s+)(?<value>[^\\s\"']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool ShouldUseAzureCredentialFallback(string? host, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            return false;

        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Contains("azmk8s.io", StringComparison.OrdinalIgnoreCase)
            || host.Contains("azure.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryExtractServerIdFromKubeconfig(string kubeconfigContent)
    {
        if (string.IsNullOrWhiteSpace(kubeconfigContent))
            return null;

        var match = ServerIdRegex.Match(kubeconfigContent);
        if (match.Success)
        {
            var serverId = match.Groups["value"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(serverId) && serverId != "-")
                return serverId;
        }

        var lines = kubeconfigContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.Contains("--server-id", StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = line.IndexOf("--server-id=", StringComparison.OrdinalIgnoreCase);
            if (separatorIndex >= 0)
            {
                var inlineValue = line[(separatorIndex + "--server-id=".Length)..].Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(inlineValue))
                    return inlineValue;
            }

            for (var next = i + 1; next < lines.Length; next++)
            {
                var valueLine = lines[next].Trim();
                if (string.IsNullOrWhiteSpace(valueLine))
                    continue;

                if (valueLine.StartsWith("-"))
                    valueLine = valueLine[1..].Trim();

                if (!valueLine.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    return valueLine.Trim('"', '\'');

                break;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildAksTokenScopes(string serverId)
    {
        var normalized = serverId.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var scopes = new List<string>();
        if (normalized.StartsWith("api://", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add(EnsureDefaultSuffix(normalized));
            return scopes;
        }

        scopes.Add(EnsureDefaultSuffix($"api://{normalized}"));

        if (Uri.IsWellFormedUriString(normalized, UriKind.Absolute))
            scopes.Add(EnsureDefaultSuffix(normalized));

        return scopes;
    }

    private static string EnsureDefaultSuffix(string value)
    {
        var trimmed = value.TrimEnd('/');
        if (trimmed.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return string.Create(CultureInfo.InvariantCulture, $"{trimmed}/.default");
    }
}

