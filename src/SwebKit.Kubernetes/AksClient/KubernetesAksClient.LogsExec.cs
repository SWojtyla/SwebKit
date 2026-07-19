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
            cancellationToken: ct).ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
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

        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags(_kubeconfigPath, _kubeconfigContext)
            .PortForward(ns, resourceName, localPort, remotePort)
            .Build();

        var psi = new ProcessStartInfo("kubectl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

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

    // Kubernetes object names are DNS-1123 labels/subdomains: lowercase alphanumerics, '-' and
    // '.' only. Enforcing this before the values reach an interactive shell command line closes
    // any command-injection avenue (e.g. a hostile pod/namespace/container name containing shell
    // metacharacters such as '&' or '|' being launched via cmd.exe / wt.exe with UseShellExecute).
    private static readonly Regex KubernetesNamePattern =
        new("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$", RegexOptions.Compiled);

    private static string ValidateKubernetesName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || !KubernetesNamePattern.IsMatch(value))
            throw new ArgumentException($"Invalid Kubernetes {paramName} name: '{value}'.", paramName);
        return value;
    }

    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
    {
        KubectlArgumentBuilder.ValidateKubernetesName(ns, nameof(ns));
        KubectlArgumentBuilder.ValidateKubernetesName(podName, nameof(podName));
        KubectlArgumentBuilder.ValidateKubernetesName(container, nameof(container));

        // Build args with global flags BEFORE the subcommand (kubectl convention).
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags(_kubeconfigPath, _kubeconfigContext)
            .ExecInteractive(ns, podName, container)
            .Add("--")
            .Add("/bin/sh")
            .Build();

        KubectlShellLauncher.Launch(args);
        return Task.CompletedTask;
    }

    // ── Feature 1: Multi-pod log aggregation ─────────────────────────────────

    // How often a live (Follow) aggregated stream re-lists pods for the deployment's selector,
    // so pods created after the stream started (rolling updates, HPA scale-out, evictions) get
    // picked up instead of being silently missing from the merged view.
    private static readonly TimeSpan DeploymentLogPodDiscoveryInterval = TimeSpan.FromSeconds(10);

    public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
        string ns, string deploymentName, LogStreamOptions opts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Resolve pods via selector from deployment spec — authoritative, not name-based
        var deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, ns, cancellationToken: ct).ConfigureAwait(false);
        var matchLabels = deployment.Spec?.Selector?.MatchLabels;
        var labelSelector = matchLabels is not null
            ? string.Join(",", matchLabels.Select(kv => $"{kv.Key}={kv.Value}"))
            : $"app={deploymentName}";

        var pods = await GetPodsAsync(ns, labelSelector, ct).ConfigureAwait(false);
        if (pods.Count == 0) yield break;

        var channel = Channel.CreateUnbounded<AggregatedLogLine>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Tracks which pods already have a running per-pod stream task, so re-discovery never
        // double-tails the same pod. Only completed streams are removed, allowing a pod that
        // gets recreated with the same name (rare, but possible with static pod names) to be
        // re-attached on the next discovery pass.
        var trackedPods = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var activeStreamCount = 0;

        void StartPodStream(PodInfo pod)
        {
            if (!trackedPods.TryAdd(pod.Name, 0)) return;
            Interlocked.Increment(ref activeStreamCount);

            _ = Task.Run(async () =>
            {
                try
                {
                    var container = pod.Containers.FirstOrDefault() ?? string.Empty;
                    await foreach (var line in StreamPodLogsAsync(ns, pod.Name, container, opts, linkedCts.Token).ConfigureAwait(false))
                    {
                        await channel.Writer.WriteAsync(
                            new AggregatedLogLine
                            {
                                PodName = pod.Name,
                                Line = line,
                                Timestamp = TryExtractLogTimestamp(line)
                            },
                            linkedCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Pod-specific stream ended (e.g. pod restarted, evicted, or rolled) — not
                    // overall cancellation. In Follow mode the discovery loop below will pick up
                    // any replacement pod; in a one-shot (non-Follow) window the countdown below
                    // completes the channel once every currently known pod has finished.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "StreamDeploymentLogs: pod '{PodName}' stream failed", pod.Name);
                }
                finally
                {
                    trackedPods.TryRemove(pod.Name, out _);
                    // Only auto-complete for one-shot (non-Follow) windows. A live Follow session
                    // must stay open even if every currently-tailed pod's stream happens to end at
                    // the same time (e.g. a rolling update replacing every replica at once) —
                    // otherwise "Live" silently stops even though the checkbox still shows enabled.
                    var remaining = Interlocked.Decrement(ref activeStreamCount);
                    if (!opts.Follow && remaining == 0)
                        channel.Writer.TryComplete();
                }
            }, linkedCts.Token);
        }

        foreach (var pod in pods)
            StartPodStream(pod);

        Task? discoveryTask = null;
        if (opts.Follow)
        {
            discoveryTask = Task.Run(async () =>
            {
                try
                {
                    while (!linkedCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(DeploymentLogPodDiscoveryInterval, linkedCts.Token).ConfigureAwait(false);
                        var currentPods = await GetPodsAsync(ns, labelSelector, linkedCts.Token).ConfigureAwait(false);
                        foreach (var pod in currentPods)
                            StartPodStream(pod);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "StreamDeploymentLogs: pod re-discovery for deployment '{DeploymentName}' failed", deploymentName);
                }
            }, linkedCts.Token);
        }

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            linkedCts.Cancel();
            if (discoveryTask is not null)
            {
                try { await discoveryTask.ConfigureAwait(false); }
                catch { /* already logged/observed above */ }
            }
        }
    }
}
