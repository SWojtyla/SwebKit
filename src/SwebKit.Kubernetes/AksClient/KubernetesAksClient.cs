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

    public KubernetesAksClient(
        string? kubeconfigContext = null,
        string? kubeconfigPath = null,
        bool enableAzureCredentialFallback = true)
    {
        var config = BuildClientConfiguration(kubeconfigContext, kubeconfigPath);

        if (enableAzureCredentialFallback)
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
