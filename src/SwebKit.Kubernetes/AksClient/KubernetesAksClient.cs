using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.IO.Compression;
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

    public async Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
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
    }

    public async Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
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
    }

    public async Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
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
    }

    public async Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("helm")
        {
            Arguments = $"rollback {releaseName} {targetRevision} --namespace {ns} --wait",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start helm process. Ensure 'helm' is on PATH.");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Helm rollback failed (exit code {process.ExitCode}): {stderr}");
        }
    }

    public async Task<IReadOnlyList<Core.Models.PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
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
        catch
        {
            // Metrics API not installed or unavailable — return empty list
            return [];
        }
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
