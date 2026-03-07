using k8s;
using k8s.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SwebKit.Kubernetes.AksClient;

public class KubernetesAksClient : IAksClient
{
    private readonly k8s.Kubernetes _client;

    public KubernetesAksClient(string? kubeconfigContext = null)
    {
        KubernetesClientConfiguration config;
        if (kubeconfigContext is not null)
        {
            var kubeconfigPath = KubernetesClientConfiguration.KubeConfigDefaultLocation;
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(
                kubeconfigPath, kubeconfigContext);
        }
        else
        {
            config = KubernetesClientConfiguration.BuildDefaultConfig();
        }
        _client = new k8s.Kubernetes(config);
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
