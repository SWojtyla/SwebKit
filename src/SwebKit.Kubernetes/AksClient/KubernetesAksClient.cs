using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text;

namespace SwebKit.Kubernetes.AksClient;

public class KubernetesAksClient : IAksClient
{
    private const string DefaultAksServerAppId = "6dae42f8-4368-4678-94ff-3960e28e3630";
    private readonly k8s.Kubernetes _client;
    private readonly string? _kubeconfigPath;
    private readonly string? _kubeconfigContext;

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
                var credential = new DefaultAzureCredential();
                var accessToken = credential.GetToken(new TokenRequestContext([scope]), default);
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
        var result = await _client.AppsV1.ListNamespacedDeploymentAsync(ns, cancellationToken: ct);
        return result.Items.Select(d => new DeploymentInfo
        {
            Name = d.Metadata.Name,
            Namespace = d.Metadata.NamespaceProperty ?? ns,
            Replicas = d.Spec?.Replicas ?? 0,
            ReadyReplicas = d.Status?.ReadyReplicas ?? 0,
            Status = d.Status?.Conditions?.FirstOrDefault(c => c.Type == "Available")?.Status ?? "Unknown",
            Labels = d.Metadata.Labels is not null ? new Dictionary<string, string>(d.Metadata.Labels) : []
        }).ToList();
    }

    public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        var result = await _client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: labelSelector, cancellationToken: ct);
        return result.Items.Select(p => new PodInfo
        {
            Name = p.Metadata.Name,
            Namespace = p.Metadata.NamespaceProperty ?? ns,
            Phase = p.Status?.Phase ?? "Unknown",
            Ready = p.Status?.ContainerStatuses?.All(c => c.Ready) ?? false,
            NodeName = p.Spec?.NodeName,
            StartTime = p.Status?.StartTime.HasValue == true ? new DateTimeOffset(p.Status.StartTime.Value) : null,
            Containers = p.Spec?.Containers?.Select(c => c.Name).ToList() ?? [],
            Labels = p.Metadata.Labels is not null ? new Dictionary<string, string>(p.Metadata.Labels) : []
        }).ToList();
    }

    public async Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
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
    }

    public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
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
    }

    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        var result = await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct);
        return result.Items.Select(n => n.Metadata.Name).OrderBy(n => n).ToList();
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
        object resource = kind.ToLowerInvariant() switch
        {
            "deployment" => await _client.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct),
            "pod" => await _client.CoreV1.ReadNamespacedPodAsync(name, ns, cancellationToken: ct),
            "ingress" => await _client.NetworkingV1.ReadNamespacedIngressAsync(name, ns, cancellationToken: ct),
            "service" => await _client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct),
            _ => throw new ArgumentException($"Unsupported resource kind: {kind}")
        };

        return KubernetesYaml.Serialize(resource);
    }

    public async IAsyncEnumerable<string> StreamPodLogsAsync(
        string ns, string podName, string container,
        LogStreamOptions opts, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
            podName, ns,
            container: string.IsNullOrEmpty(container) ? null : container,
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
            IsActive = false
        };

        var psi = new ProcessStartInfo("kubectl")
        {
            Arguments = $"port-forward {resourceName} {localPort}:{remotePort} -n {ns}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start kubectl port-forward.");
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.Contains("Forwarding from") == true) session.IsActive = true;
        };
        process.BeginOutputReadLine();

        PortForwardProcessRegistry.Register(session.SessionId, process);
        return Task.FromResult(session);
    }

    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default)
    {
        PortForwardProcessRegistry.Stop(session.SessionId);
        session.IsActive = false;
        return Task.CompletedTask;
    }

    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
    {
        var args = $"exec -it {podName} -n {ns} -c {container} -- /bin/sh";
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
    }

    public async Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
    {
        await _client.CoreV1.DeleteNamespacedPodAsync(podName, ns, cancellationToken: ct);
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

internal static class PortForwardProcessRegistry
{
    private static readonly Dictionary<Guid, Process> _processes = [];
    private static readonly Lock _lock = new();

    public static void Register(Guid id, Process process) { lock (_lock) _processes[id] = process; }
    public static void Stop(Guid id)
    {
        lock (_lock)
        {
            if (_processes.Remove(id, out var p) && !p.HasExited) p.Kill(entireProcessTree: true);
        }
    }
}
