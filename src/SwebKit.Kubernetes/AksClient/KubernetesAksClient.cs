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
using SwebKit.Core.Services;
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

public partial class KubernetesAksClient : IAksClient, IAsyncDisposable
{
    private const string DefaultAksServerAppId = "6dae42f8-4368-4678-94ff-3960e28e3630";
    private const int MaxGeneratedJobNamePrefixLength = 52;
    private const string GatewayApiGroup = "gateway.networking.k8s.io";
    private static readonly Regex LogTimestampPrefixRegex = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] GatewayApiVersions = ["v1", "v1beta1", "v1alpha2"];

    private const string KedaApiGroup = "keda.sh";
    private const string KedaScaledObjectsPlural = "scaledobjects";
    private static readonly string[] KedaApiVersions = ["v1alpha1"];

    /// <summary>
    /// Cached probe result for whether the KEDA <c>ScaledObject</c> CRD is served by this cluster.
    /// <c>null</c> = not yet probed, <c>false</c> = confirmed absent (HPA reads then skip the extra
    /// ScaledObject list to avoid a 404 on every auto-refresh), <c>true</c> = present.
    /// </summary>
    private bool? _kedaCrdAvailable;

    /// <summary>
    /// Resource kinds — as recorded in <see cref="SwebKit.Core.Abstractions.AksAccessDeniedScope"/> denial
    /// tuples (model type name minus the "Info" suffix) — that belong to the optional Gateway API
    /// (<see cref="GatewayApiGroup"/>). An RBAC 403 on these represents missing access to optional advanced
    /// networking, not missing core cluster access, so the permission-warning builder excludes them to avoid
    /// falsely implying a core-permission problem. Kept next to the Gateway API constants so the exclusion
    /// list stays with the gateway feature (single source of truth). Compared case-insensitively.
    /// </summary>
    public static readonly IReadOnlySet<string> GatewayApiDenialKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Gateway", "GatewayClass", "HttpRoute" };

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
    private readonly ILogger<KubernetesAksClient> _logger;

    private readonly Dictionary<Guid, Process> _portForwardProcesses = [];
    private readonly Lock _portForwardLock = new();
    private readonly Lock _rebuildLock = new();
    private DateTime _lastRebuild = DateTime.MinValue;

    public KubernetesAksClient(
        string? kubeconfigContext = null,
        string? kubeconfigPath = null,
        ILogger<KubernetesAksClient>? logger = null)
    {
        _kubeconfigPath = kubeconfigPath;
        _kubeconfigContext = kubeconfigContext;
        _logger = logger ?? NullLogger<KubernetesAksClient>.Instance;

        var config = BuildClientConfiguration(kubeconfigContext, kubeconfigPath);
        TryApplyAzureCredentialFallback(config, kubeconfigPath);

        _client = new k8s.Kubernetes(config);
    }

    private void RebuildClient()
    {
        // Use a non-blocking TryEnter instead of `lock` so a storm of concurrent 403s (e.g. one
        // resource type denied across several fanned-out namespaces at once) cannot pile up
        // multiple threads waiting on this lock while the first one runs the potentially slow,
        // synchronous credential rebuild (which can shell out to an exec-credential/kubelogin
        // plugin). Callers that lose the race just skip rebuilding and retry with whatever client
        // is already current — the 30s throttle below means at most one real rebuild per window
        // regardless, so skipping here costs nothing but avoids thread-pool blocking that could
        // otherwise stall the UI.
        if (!_rebuildLock.TryEnter())
            return;

        try
        {
            if ((DateTime.UtcNow - _lastRebuild).TotalSeconds < 30)
                return;

            var config = BuildClientConfiguration(_kubeconfigContext, _kubeconfigPath);
            TryApplyAzureCredentialFallback(config, _kubeconfigPath);
            _client = new k8s.Kubernetes(config);
            _lastRebuild = DateTime.UtcNow;
        }
        finally
        {
            _rebuildLock.Exit();
        }
    }

    private async Task<T> WithAuthRetryAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            RebuildClient();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex2) when (ex2.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw ToAccessDeniedException(ex2);
            }
        }
    }

    private async Task WithAuthRetryAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            RebuildClient();
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex2) when (ex2.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw ToAccessDeniedException(ex2);
            }
        }
    }

    /// <summary>
    /// Converts a Kubernetes API 403 into a client-library-agnostic <see cref="AksAccessDeniedException"/>,
    /// extracting the human-readable RBAC denial message (e.g. "ingresses.networking.k8s.io is
    /// forbidden: User ... cannot list resource ... in the namespace ...") from the response body
    /// when available, so callers don't need to know about <c>k8s.Autorest.HttpOperationException</c>.
    /// </summary>
    private static AksAccessDeniedException ToAccessDeniedException(k8s.Autorest.HttpOperationException ex)
    {
        string message = ex.Message;
        try
        {
            using var doc = JsonDocument.Parse(ex.Response.Content);
            if (doc.RootElement.TryGetProperty("message", out var messageProperty) &&
                messageProperty.GetString() is { Length: > 0 } parsedMessage)
            {
                message = parsedMessage;
            }
        }
        catch (JsonException)
        {
            // Response body wasn't the expected Kubernetes Status JSON — fall back to ex.Message.
        }

        return new AksAccessDeniedException(message, ex);
    }

    internal static KubernetesClientConfiguration BuildClientConfiguration(string? kubeconfigContext, string? kubeconfigPath)
    {
        var hasExplicitKubeconfig = !string.IsNullOrWhiteSpace(kubeconfigPath);
        var hasExplicitContext = !string.IsNullOrWhiteSpace(kubeconfigContext);

        if (!hasExplicitKubeconfig && !hasExplicitContext)
            return KubernetesClientConfiguration.BuildDefaultConfig();

        try
        {
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(
                hasExplicitKubeconfig ? kubeconfigPath : null,
                hasExplicitContext ? kubeconfigContext : null);
        }
        catch (Exception ex)
        {
            if (AksAzureAuthHelpers.ShouldUseAzureCredentialFallbackAfterKubeConfigError(
                    ex,
                    hasExplicitKubeconfig ? kubeconfigPath : null,
                    hasExplicitContext ? kubeconfigContext : null,
                    out var fallbackConfig))
            {
                return fallbackConfig;
            }

            throw;
        }
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
                var credential = AzureCredentialFactory.CreateDefault(AzureCredentialOptions);
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
            var result = await _client.AppsV1.ListNamespacedDeploymentAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items.Select(d => new DeploymentInfo
            {
                Name = d.Metadata.Name,
                Namespace = d.Metadata.NamespaceProperty ?? ns,
                Replicas = d.Spec?.Replicas ?? 0,
                ReadyReplicas = d.Status?.ReadyReplicas ?? 0,
                Status = d.Status?.Conditions?.FirstOrDefault(c => c.Type == "Available")?.Status ?? "Unknown",
                ImageTag = ExtractImageTag(d.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image),
                Labels = d.Metadata.Labels is not null ? new Dictionary<string, string>(d.Metadata.Labels) : [],
                SelectorLabels = d.Spec?.Selector?.MatchLabels is not null
                    ? new Dictionary<string, string>(d.Spec.Selector.MatchLabels)
                    : []
            }).ToList();
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: labelSelector, cancellationToken: ct).ConfigureAwait(false);
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
        }).ConfigureAwait(false);
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
            var result = await _client.CoreV1.ListNamespacedEventAsync(ns, fieldSelector: fieldSelector, cancellationToken: ct).ConfigureAwait(false);
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
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespacedServiceAsync(ns, cancellationToken: ct).ConfigureAwait(false);
            return result.Items
                .Select(service =>
                {
                    var loadBalancerAddresses = service.Status?.LoadBalancer?.Ingress?
                        .Select(ingress => ingress.Ip ?? ingress.Hostname ?? string.Empty)
                        .Where(address => !string.IsNullOrWhiteSpace(address))
                        .Select(address => address!)
                        ?? [];

                    var externalAddresses = (service.Spec?.ExternalIPs ?? [])
                        .Where(address => !string.IsNullOrWhiteSpace(address))
                        .Concat(loadBalancerAddresses)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ServiceInfo
                    {
                        Name = service.Metadata.Name,
                        Namespace = service.Metadata.NamespaceProperty ?? ns,
                        Type = service.Spec?.Type ?? "ClusterIP",
                        ClusterIp = string.IsNullOrWhiteSpace(service.Spec?.ClusterIP)
                            ? "None"
                            : service.Spec.ClusterIP,
                        ExternalAddresses = externalAddresses,
                        Ports = service.Spec?.Ports?.Select(port => new ServicePortInfo
                        {
                            Name = port.Name,
                            Protocol = port.Protocol ?? "TCP",
                            Port = port.Port,
                            TargetPort = port.TargetPort?.Value,
                            NodePort = port.NodePort
                        }).ToList() ?? [],
                        SelectorLabels = service.Spec?.Selector is not null
                            ? new Dictionary<string, string>(service.Spec.Selector)
                            : [],
                        Labels = service.Metadata.Labels is not null
                            ? new Dictionary<string, string>(service.Metadata.Labels)
                            : []
                    };
                })
                .OrderBy(service => service.Namespace, StringComparer.Ordinal)
                .ThenBy(service => service.Name, StringComparer.Ordinal)
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.NetworkingV1.ListNamespacedIngressAsync(ns, cancellationToken: ct).ConfigureAwait(false);
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
        }).ConfigureAwait(false);
    }

    private async Task<(List<V1Pod> Pods, Dictionary<string, string> SelectorLabels)> ResolveWorkloadPodsAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct)
    {
        switch (workloadKind.Trim().ToLowerInvariant())
        {
            case "deployment":
                {
                    var deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(workloadName, ns, cancellationToken: ct).ConfigureAwait(false);
                    var pods = await _client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                    return (
                        pods.Items.Where(pod => MatchesLabelSelector(deployment.Spec?.Selector, pod.Metadata?.Labels)).ToList(),
                        deployment.Spec?.Selector?.MatchLabels is not null
                            ? new Dictionary<string, string>(deployment.Spec.Selector.MatchLabels)
                            : []);
                }

            case "statefulset":
                {
                    var statefulSet = await _client.AppsV1.ReadNamespacedStatefulSetAsync(workloadName, ns, cancellationToken: ct).ConfigureAwait(false);
                    var pods = await _client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                    return (
                        pods.Items.Where(pod => MatchesLabelSelector(statefulSet.Spec?.Selector, pod.Metadata?.Labels)).ToList(),
                        statefulSet.Spec?.Selector?.MatchLabels is not null
                            ? new Dictionary<string, string>(statefulSet.Spec.Selector.MatchLabels)
                            : []);
                }

            case "pod":
                {
                    var pod = await _client.CoreV1.ReadNamespacedPodAsync(workloadName, ns, cancellationToken: ct).ConfigureAwait(false);
                    return (
                        [pod],
                        pod.Metadata?.Labels is not null
                            ? new Dictionary<string, string>(pod.Metadata.Labels)
                            : []);
                }

            default:
                throw new NotSupportedException(
                    $"Network policy analysis is not supported for workload kind '{workloadKind}'.");
        }
    }

    private static bool IsPodReady(V1Pod pod) =>
        pod.Status?.ContainerStatuses is { Count: > 0 } statuses
        && statuses.All(status => status.Ready);

    private static string? ExtractImageTag(string? image)
    {
        if (string.IsNullOrEmpty(image)) return null;
        var colonIndex = image.LastIndexOf(':');
        return colonIndex >= 0 ? image[(colonIndex + 1)..] : null;
    }

    private static DateTimeOffset? TryExtractLogTimestamp(string line)
    {
        var match = LogTimestampPrefixRegex.Match(line);
        if (match.Success && DateTimeOffset.TryParse(match.Groups["timestamp"].Value, out var parsed))
            return parsed;

        return null;
    }

    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct).ConfigureAwait(false);
            return result.Items.Select(n => n.Metadata.Name).OrderBy(n => n).ToList();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads all contexts from the kubeconfig file at <paramref name="kubeconfigPath"/>
    /// (or the default location when <see langword="null"/>) without establishing a
    /// cluster connection. Safe to call from configuration UI before a client is set up.
    /// </summary>
    public static IReadOnlyList<KubeContextInfo> ReadContextsFromKubeconfig(string? kubeconfigPath = null)
    {
        var path = string.IsNullOrWhiteSpace(kubeconfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : kubeconfigPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        var config = KubernetesClientConfiguration.LoadKubeConfig(path);
        var currentContext = config.CurrentContext;

        return (config.Contexts ?? [])
            .Select(ctx => new KubeContextInfo
            {
                Name = ctx.Name,
                Cluster = ctx.ContextDetails?.Cluster,
                User = ctx.ContextDetails?.User,
                Namespace = ctx.ContextDetails?.Namespace,
                IsCurrent = string.Equals(ctx.Name, currentContext, StringComparison.Ordinal)
            })
            .OrderBy(c => c.Name)
            .ToList();
    }

    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ReadContextsFromKubeconfig(_kubeconfigPath));
    }

    public async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            // Helm stores releases as Secrets with type=helm.sh/release.v1 and label owner=helm
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: "owner=helm", cancellationToken: ct).ConfigureAwait(false);

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
        }).ConfigureAwait(false);
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
            return await GetHelmManifestAsync(ns, name, ct).ConfigureAwait(false);

        if (kind.Equals("gateway", StringComparison.OrdinalIgnoreCase))
        {
            return await WithAuthRetryAsync(async () =>
                SerializeCustomObjectYaml(await ReadGatewayApiCustomObjectAsync(ns, "gateways", name, ct).ConfigureAwait(false))).ConfigureAwait(false);
        }

        if (kind.Equals("gatewayclass", StringComparison.OrdinalIgnoreCase))
        {
            return await WithAuthRetryAsync(async () =>
                SerializeCustomObjectYaml(await ReadClusterGatewayApiCustomObjectAsync("gatewayclasses", name, ct).ConfigureAwait(false))).ConfigureAwait(false);
        }

        if (kind.Equals("httproute", StringComparison.OrdinalIgnoreCase))
        {
            return await WithAuthRetryAsync(async () =>
                SerializeCustomObjectYaml(await ReadGatewayApiCustomObjectAsync(ns, "httproutes", name, ct).ConfigureAwait(false))).ConfigureAwait(false);
        }

        return await WithAuthRetryAsync(async () =>
        {
            object resource = kind.ToLowerInvariant() switch
            {
                "deployment" => await _client.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "pod" => await _client.CoreV1.ReadNamespacedPodAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "ingress" => await _client.NetworkingV1.ReadNamespacedIngressAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "service" => await _client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "statefulset" => await _client.AppsV1.ReadNamespacedStatefulSetAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "configmap" => await _client.CoreV1.ReadNamespacedConfigMapAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "secret" => await _client.CoreV1.ReadNamespacedSecretAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "horizontalpodautoscaler" or "hpa" => await _client.AutoscalingV2.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "job" => await _client.BatchV1.ReadNamespacedJobAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                "cronjob" => await _client.BatchV1.ReadNamespacedCronJobAsync(name, ns, cancellationToken: ct).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unsupported resource kind: {kind}")
            };

            return CleanEditableYaml(KubernetesYaml.Serialize(resource));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Strips server-managed/read-only fields (the whole <c>status</c> block, plus
    /// <c>metadata.managedFields</c>, <c>resourceVersion</c>, <c>generation</c>, <c>uid</c>, and
    /// <c>creationTimestamp</c>) from a full API object dump before it's shown for editing —
    /// matching <c>kubectl edit</c> conventions. These fields are populated and owned by the API
    /// server: leaving them in isn't just noisy, a large <c>managedFields</c> block or a
    /// <c>resourceVersion</c> that goes stale while the user is still editing can make the
    /// eventual <c>kubectl apply</c> behave unpredictably against a fast-changing live object
    /// (this resource's own managedFields shows multiple controllers updating it every few
    /// minutes). <c>kubectl apply</c> is patch-based and does not need any of these fields.
    /// </summary>
    /// <remarks>
    /// This operates on the YAML representation-model (node/AST) API, not a typed
    /// Dictionary&lt;object, object&gt; round-trip. A dictionary round-trip loses each scalar's
    /// original type: YamlDotNet's default deserializer reads every plain scalar into that kind
    /// of dictionary as a <see cref="string"/>, so re-serializing turns genuinely-numeric fields
    /// like <c>spec.replicas</c> into quoted strings (which Kubernetes then rejects — a `*int32`
    /// field can't unmarshal a JSON string) while unquoted numeric-looking annotation values
    /// (e.g. a revision "251") also can't round-trip safely. Editing the node tree directly and
    /// only removing specific mapping entries leaves every other node's original style/type
    /// completely untouched.
    /// </remarks>
    internal static string CleanEditableYaml(string rawYaml)
    {
        var yamlStream = new YamlStream();
        using (var reader = new StringReader(rawYaml))
        {
            yamlStream.Load(reader);
        }

        if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
            return rawYaml;

        root.Children.Remove(new YamlScalarNode("status"));

        if (root.Children.TryGetValue(new YamlScalarNode("metadata"), out var metadataNode) &&
            metadataNode is YamlMappingNode metadata)
        {
            foreach (var key in new[] { "managedFields", "resourceVersion", "generation", "uid", "creationTimestamp" })
            {
                metadata.Children.Remove(new YamlScalarNode(key));
            }
        }

        using var writer = new StringWriter();
        yamlStream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private async Task<string> GetHelmManifestAsync(string ns, string releaseName, CancellationToken ct)
    {
        var args = $"get manifest {releaseName} --namespace {ns}{BuildHelmKubeconfigArgs()}";

        var psi = new ProcessStartInfo("helm")
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start helm process.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2 /* ERROR_FILE_NOT_FOUND */)
        {
            throw new InvalidOperationException(
                "helm CLI is not installed or not on PATH. " +
                "Install helm (https://helm.sh/docs/intro/install/) and restart the app.");
        }
        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            if (IsForbiddenError(stderr))
            {
                var token = TryAcquireFreshAzureToken();
                if (token is not null)
                {
                    var retryPsi = new ProcessStartInfo("helm")
                    {
                        Arguments = $"get manifest {releaseName} --namespace {ns}{BuildHelmKubeconfigArgs()} --kube-token {token}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var retryProcess = Process.Start(retryPsi)
                        ?? throw new InvalidOperationException("Failed to start helm process.");
                    var retryStdout = await retryProcess.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                    var retryStderr = await retryProcess.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                    await retryProcess.WaitForExitAsync(ct).ConfigureAwait(false);

                    if (retryProcess.ExitCode != 0)
                        throw new InvalidOperationException($"helm get manifest failed after credential refresh (exit {retryProcess.ExitCode}): {retryStderr}");

                    return retryStdout;
                }
            }

            throw new InvalidOperationException($"helm get manifest failed (exit {process.ExitCode}): {stderr}");
        }

        return stdout;
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

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try { await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AKS connection test failed.");
            return false;
        }
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
                deploymentName, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            await _client.CoreV1.DeleteNamespacedPodAsync(podName, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            await _client.NetworkingV1.DeleteNamespacedIngressAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default)
    {
        await WithAuthRetryAsync(async () =>
        {
            await DeleteGatewayApiCustomObjectAsync(ns, "httproutes", name, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
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
                deploymentName, ns, cancellationToken: ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: $"owner=helm,name={releaseName}", cancellationToken: ct).ConfigureAwait(false);

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
        }).ConfigureAwait(false);
    }

    public async Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            // Find the latest release secret
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(
                ns, labelSelector: $"owner=helm,name={releaseName}", cancellationToken: ct).ConfigureAwait(false);

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
                var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                // Extract the "config" field which contains the user-supplied values
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("config", out var config))
                    return System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                return json;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to decode Helm release values; returning a placeholder.");
                return "# Unable to decode release values";
            }
        }).ConfigureAwait(false);
    }

    public async Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("helm")
        {
            Arguments = $"rollback {releaseName} {targetRevision} --namespace {ns} --wait{BuildHelmKubeconfigArgs()}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start helm process. Ensure 'helm' is on PATH.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException(
                "helm CLI is not installed or not on PATH. " +
                "Install helm (https://helm.sh/docs/intro/install/) and restart the app.");
        }

        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            if (IsForbiddenError(stderr))
            {
                var token = TryAcquireFreshAzureToken();
                if (token is not null)
                {
                    var retryPsi = new ProcessStartInfo("helm")
                    {
                        Arguments = $"rollback {releaseName} {targetRevision} --namespace {ns} --wait{BuildHelmKubeconfigArgs()} --kube-token {token}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var retryProcess = Process.Start(retryPsi)
                        ?? throw new InvalidOperationException("Failed to start helm process. Ensure 'helm' is on PATH.");

                    var retryStderr = await retryProcess.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                    await retryProcess.WaitForExitAsync(ct).ConfigureAwait(false);

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
        await File.WriteAllTextAsync(tempFile, yaml, ct).ConfigureAwait(false);
        try
        {
            var (exitCode, stderr) = await RunKubectlApplyAsync(tempFile, ns, ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                if (IsForbiddenError(stderr))
                {
                    var token = TryAcquireFreshAzureToken();
                    if (token is not null)
                    {
                        var (retryExit, retryStderr) = await RunKubectlApplyWithTokenAsync(tempFile, ns, token, ct).ConfigureAwait(false);
                        if (retryExit != 0)
                        {
                            _logger.LogWarning(
                                "kubectl apply failed for {Kind}/{Name} in {Namespace} after credential refresh (exit {ExitCode}): {Output}",
                                kind, name, ns, retryExit, retryStderr);
                            throw new InvalidOperationException($"kubectl apply failed after credential refresh (exit {retryExit}): {retryStderr}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "kubectl apply failed for {Kind}/{Name} in {Namespace} with a Forbidden error and no fresh Azure credential was available (exit {ExitCode}): {Output}",
                            kind, name, ns, exitCode, stderr);
                        throw new InvalidOperationException($"kubectl apply failed (exit {exitCode}): {stderr}");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "kubectl apply failed for {Kind}/{Name} in {Namespace} (exit {ExitCode}): {Output}",
                        kind, name, ns, exitCode, stderr);
                    throw new InvalidOperationException($"kubectl apply failed (exit {exitCode}): {stderr}");
                }
            }

            _logger.LogInformation("Applied YAML for {Kind}/{Name} in {Namespace}", kind, name, ns);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<string?> ValidateResourceYamlAsync(string ns, string yaml, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"swebkit-validate-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(tempFile, yaml, ct).ConfigureAwait(false);
        try
        {
            var (exitCode, stderr) = await RunKubectlDryRunAsync(tempFile, ns, ct).ConfigureAwait(false);
            if (exitCode != 0)
            {
                _logger.LogWarning(
                    "Server-side dry-run validation failed for a resource in {Namespace} (exit {ExitCode}): {Output}",
                    ns, exitCode, stderr);
                return stderr.Trim();
            }
            return null;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    private async Task<(int ExitCode, string Stderr)> RunKubectlDryRunAsync(string tempFile, string ns, CancellationToken ct)
    {
        // --dry-run=server sends the manifest through the API server's full validation and
        // admission chain (schema checks, webhooks, etc.) without persisting anything —
        // a much stronger check than local YAML parsing.
        return await RunKubectlProcessAsync($"apply -f \"{tempFile}\" --namespace {ns} --dry-run=server{BuildKubeconfigArgs()}", ct).ConfigureAwait(false);
    }

    private async Task<(int ExitCode, string Stderr)> RunKubectlApplyAsync(string tempFile, string ns, CancellationToken ct)
    {
        return await RunKubectlProcessAsync($"apply -f \"{tempFile}\" --namespace {ns}{BuildKubeconfigArgs()}", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts `kubectl` with the given arguments and awaits its exit. Stdout and stderr are
    /// captured continuously (not just after a clean exit) so that if <paramref name="ct"/> is
    /// cancelled (e.g. a caller-side timeout), whatever kubectl had already printed — including a
    /// stuck interactive auth prompt from a kubelogin/exec credential plugin, which is a common
    /// cause of an apply that hangs with zero feedback — can be surfaced instead of a bare
    /// "timed out" message. On cancellation the still-running kubectl process is also killed
    /// rather than abandoned — otherwise a "timed out" result would be a false negative: kubectl
    /// could keep running in the background and still apply the change moments later, leaving
    /// the user unsure whether their save actually went through.
    /// </summary>
    private async Task<(int ExitCode, string Stderr)> RunKubectlProcessAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("kubectl")
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var output = new StringBuilder();
        var outputLock = new object();
        void OnData(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (outputLock) { output.AppendLine(e.Data); }
        }
        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;

        if (!process.Start())
            throw new InvalidOperationException("Failed to start kubectl. Ensure 'kubectl' is on PATH.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var killOnCancel = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        });

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            string captured;
            lock (outputLock) { captured = output.ToString().Trim(); }
            _logger.LogWarning(
                "kubectl was cancelled (caller-side timeout) running 'kubectl {Arguments}'. Captured output before cancellation: {Output}",
                arguments, string.IsNullOrEmpty(captured) ? "(none)" : captured);
            throw new TimeoutException(string.IsNullOrEmpty(captured)
                ? "kubectl produced no output before it was cancelled — it may be stuck waiting on cluster/network connectivity or an interactive credential prompt (e.g. kubelogin device-code sign-in) that never surfaced."
                : $"kubectl was cancelled after producing this output:\n{captured}");
        }

        string combinedOutput;
        lock (outputLock) { combinedOutput = output.ToString().Trim(); }

        return (process.ExitCode, combinedOutput);
    }

    private static bool IsForbiddenError(string stderr)
        => stderr.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);

    internal static string BuildCliKubeconfigArgs(string? kubeconfigPath, string? kubeconfigContext, string contextFlagName)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(kubeconfigPath))
            sb.Append($" --kubeconfig \"{kubeconfigPath}\"");
        if (!string.IsNullOrWhiteSpace(kubeconfigContext))
            sb.Append($" {contextFlagName} {kubeconfigContext}");
        return sb.ToString();
    }

    private string BuildKubeconfigArgs()
        => BuildCliKubeconfigArgs(_kubeconfigPath, _kubeconfigContext, "--context");

    private string BuildHelmKubeconfigArgs()
        => BuildCliKubeconfigArgs(_kubeconfigPath, _kubeconfigContext, "--kube-context");

    private async Task<(int ExitCode, string Stderr)> RunKubectlApplyWithTokenAsync(string tempFile, string ns, string token, CancellationToken ct)
    {
        return await RunKubectlProcessAsync($"apply -f \"{tempFile}\" --namespace {ns}{BuildKubeconfigArgs()} --token {token}", ct).ConfigureAwait(false);
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
                var credential = AzureCredentialFactory.CreateDefault(AzureCredentialOptions);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var accessToken = credential.GetToken(new TokenRequestContext([scope]), cts.Token);
                if (!string.IsNullOrWhiteSpace(accessToken.Token))
                    return accessToken.Token;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Azure credential token acquisition failed for scope {Scope}; trying the next scope.", scope);
            }
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
                    "metrics.k8s.io", "v1beta1", ns, "pods", cancellationToken: ct).ConfigureAwait(false);

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
            catch (Exception ex)
            {
                // Metrics API not installed or unavailable — return empty list
                _logger.LogDebug(ex, "Pod metrics unavailable for namespace {Namespace} (metrics-server may not be installed).", ns);
                return [];
            }
        }).ConfigureAwait(false);
    }
}

internal static class AksAzureAuthHelpers
{
    private static readonly Regex ServerIdRegex = new(
        "--server-id(?:=|\\s+)(?<value>[^\\s\"']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryPrepareKubeconfigForAzureCredentialFallback(K8SConfiguration kubeConfig, string? currentContext)
    {
        ArgumentNullException.ThrowIfNull(kubeConfig);

        var selectedContextName = string.IsNullOrWhiteSpace(currentContext)
            ? kubeConfig.CurrentContext
            : currentContext;

        if (string.IsNullOrWhiteSpace(selectedContextName))
            return false;

        var selectedContext = (kubeConfig.Contexts ?? Enumerable.Empty<Context>())
            .FirstOrDefault(ctx => string.Equals(ctx.Name, selectedContextName, StringComparison.Ordinal));
        var selectedClusterName = selectedContext?.ContextDetails?.Cluster;
        var selectedUserName = selectedContext?.ContextDetails?.User;

        if (string.IsNullOrWhiteSpace(selectedClusterName) || string.IsNullOrWhiteSpace(selectedUserName))
            return false;

        var selectedCluster = (kubeConfig.Clusters ?? Enumerable.Empty<Cluster>())
            .FirstOrDefault(cluster => string.Equals(cluster.Name, selectedClusterName, StringComparison.Ordinal));
        var selectedUser = (kubeConfig.Users ?? Enumerable.Empty<User>())
            .FirstOrDefault(user => string.Equals(user.Name, selectedUserName, StringComparison.Ordinal));
        var credentials = selectedUser?.UserCredentials;

        if (credentials is null)
            return false;

        if (!ShouldUseAzureCredentialFallback(selectedCluster?.ClusterEndpoint?.Server, credentials.Token))
            return false;

        if (credentials.ExternalExecution is null && credentials.AuthProvider is null)
            return false;

        credentials.ExternalExecution = null;
        credentials.AuthProvider = null;
        return true;
    }

    public static bool ShouldUseAzureCredentialFallbackAfterKubeConfigError(
        Exception exception,
        string? kubeconfigPath,
        string? currentContext,
        out KubernetesClientConfiguration fallbackConfig)
    {
        ArgumentNullException.ThrowIfNull(exception);

        fallbackConfig = null!;

        if (!IsBrokenExecCredentialError(exception))
            return false;

        var effectiveKubeconfigPath = string.IsNullOrWhiteSpace(kubeconfigPath)
            ? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : kubeconfigPath;

        if (string.IsNullOrWhiteSpace(effectiveKubeconfigPath) || !File.Exists(effectiveKubeconfigPath))
            return false;

        var kubeConfig = KubernetesClientConfiguration.LoadKubeConfig(effectiveKubeconfigPath, useRelativePaths: true);
        if (!TryPrepareKubeconfigForAzureCredentialFallback(kubeConfig, currentContext))
            return false;

        fallbackConfig = BuildAzureCredentialFallbackConfiguration(kubeConfig, currentContext);
        return true;
    }

    public static KubernetesClientConfiguration BuildAzureCredentialFallbackConfiguration(K8SConfiguration kubeConfig, string? currentContext)
    {
        ArgumentNullException.ThrowIfNull(kubeConfig);

        var selectedContextName = string.IsNullOrWhiteSpace(currentContext)
            ? kubeConfig.CurrentContext
            : currentContext;

        if (string.IsNullOrWhiteSpace(selectedContextName))
            throw new InvalidOperationException("Unable to determine the active kubeconfig context for AKS credential fallback.");

        var selectedContext = (kubeConfig.Contexts ?? Enumerable.Empty<Context>())
            .FirstOrDefault(ctx => string.Equals(ctx.Name, selectedContextName, StringComparison.Ordinal));
        var selectedClusterName = selectedContext?.ContextDetails?.Cluster;
        var selectedUserName = selectedContext?.ContextDetails?.User;

        if (string.IsNullOrWhiteSpace(selectedClusterName))
            throw new InvalidOperationException($"Kubeconfig context '{selectedContextName}' does not reference a cluster.");

        var selectedCluster = (kubeConfig.Clusters ?? Enumerable.Empty<Cluster>())
            .FirstOrDefault(cluster => string.Equals(cluster.Name, selectedClusterName, StringComparison.Ordinal));
        var selectedUser = (kubeConfig.Users ?? Enumerable.Empty<User>())
            .FirstOrDefault(user => string.Equals(user.Name, selectedUserName, StringComparison.Ordinal));
        var endpoint = selectedCluster?.ClusterEndpoint
            ?? throw new InvalidOperationException($"Kubeconfig cluster '{selectedClusterName}' was not found.");
        var credentials = selectedUser?.UserCredentials;

        return new KubernetesClientConfiguration
        {
            Namespace = selectedContext?.ContextDetails?.Namespace,
            Host = endpoint.Server,
            SkipTlsVerify = endpoint.SkipTlsVerify,
            TlsServerName = endpoint.TlsServerName,
            SslCaCerts = BuildCertificateAuthorities(endpoint),
            ClientCertificateData = credentials?.ClientCertificateData,
            ClientCertificateFilePath = credentials?.ClientCertificate,
            ClientCertificateKeyData = credentials?.ClientKeyData,
            ClientKeyFilePath = credentials?.ClientKey,
            Username = credentials?.UserName,
            Password = credentials?.Password,
            AccessToken = credentials?.Token
        };
    }

    private static X509Certificate2Collection BuildCertificateAuthorities(ClusterEndpoint endpoint)
    {
        var certificates = new X509Certificate2Collection();

        if (!string.IsNullOrWhiteSpace(endpoint.CertificateAuthorityData))
        {
            certificates.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(endpoint.CertificateAuthorityData)));
            return certificates;
        }

        if (!string.IsNullOrWhiteSpace(endpoint.CertificateAuthority) && File.Exists(endpoint.CertificateAuthority))
        {
            certificates.ImportFromPemFile(endpoint.CertificateAuthority);
        }

        return certificates;
    }

    private static bool IsBrokenExecCredentialError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is k8s.Exceptions.KubeConfigException kubeConfigException
                && (kubeConfigException.Message.Contains("external exec failed due to", StringComparison.OrdinalIgnoreCase)
                    || (kubeConfigException.Message.Contains("ExecuteExternalCommand", StringComparison.Ordinal)
                        && kubeConfigException.Message.Contains("does not contain any JSON tokens", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            if (current is JsonException)
            {
                var message = current.Message;
                if (message.Contains("does not contain any JSON tokens", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

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

