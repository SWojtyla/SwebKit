using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>Controllable <see cref="IAksClient"/> for timeline tests — each source leg is
/// individually configurable (data or a thrown exception) so coverage reporting can be
/// exercised per source.</summary>
internal sealed class FakeTimelineAksClient : IAksClient
{
    public IReadOnlyList<KubernetesEvent> Events { get; set; } = [];
    public IReadOnlyList<PodInfo> Pods { get; set; } = [];
    public IReadOnlyList<DeploymentInfo> Deployments { get; set; } = [];
    public IReadOnlyList<HelmReleaseInfo> HelmReleases { get; set; } = [];
    public IReadOnlyList<HelmRevisionInfo> HelmHistory { get; set; } = [];
    public IReadOnlyList<string> Namespaces { get; set; } = ["default"];
    public Exception? EventsError { get; set; }
    public Exception? PodsError { get; set; }
    public Exception? DeploymentsError { get; set; }
    public Exception? HelmError { get; set; }
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default)
        => EventsError is not null ? Task.FromException<IReadOnlyList<KubernetesEvent>>(EventsError)
            : Task.FromResult(Events);

    public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
    {
        CallCount++;
        return PodsError is not null ? Task.FromException<IReadOnlyList<PodInfo>>(PodsError)
            : Task.FromResult<IReadOnlyList<PodInfo>>(Pods.Where(p => ns == "" || p.Namespace == ns).ToList());
    }

    public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
        => DeploymentsError is not null ? Task.FromException<IReadOnlyList<DeploymentInfo>>(DeploymentsError)
            : Task.FromResult<IReadOnlyList<DeploymentInfo>>(Deployments.Where(d => ns == "" || d.Namespace == ns).ToList());

    public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
        => Task.FromResult(HelmReleases);

    // Deliberately NOT overriding GetHelmRevisionsAsync — the interface default fans out
    // releases → per-release history, which is exactly the honesty path this fake exercises.
    public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
        => HelmError is not null ? Task.FromException<IReadOnlyList<HelmRevisionInfo>>(HelmError)
            : Task.FromResult(HelmHistory);

    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
        => Task.FromResult(Namespaces);

    public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);

    // ── Unused members ────────────────────────────────────────────────────────
    public IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<string>();
    public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromException<PortForwardSession>(new NotSupportedException());
    public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
    public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
    public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngressInfo>>([]);
    public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromException<IngressAnalysis>(new NotSupportedException());
    public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromException<NetworkPolicyAnalysis>(new NotSupportedException());
    public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);
    public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);
    public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
    public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(new HelmReleaseValues { UserValues = "", ComputedValues = "" });
    public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PodMetrics>>([]);
    public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
    public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default) => AsyncEnumerable.Empty<AggregatedLogLine>();
    public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);
    public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);
    public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecretInfo>>([]);
    public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
    public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HpaInfo>>([]);
    public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);
}

/// <summary>Pool that hands back one fixed client (or null) — same shape as
/// FakeConnectionPoolForWorkspaceInvestigation in SwebKit.Agents.Tests.</summary>
internal sealed class FakeTimelineConnectionPool(IAksClient? aksClient) : IMonitoringConnectionPool
{
    public IAksClient? GetAksClient() => aksClient;
    public IAksClient? GetAksClient(string? context) => aksClient;
    public IServiceBusClient? GetServiceBusClient(string alias) => null;
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => ValueTask.FromResult<IRedisClient?>(null);
    public void InvalidateStaleConnections() { }
    public void EvictServiceBusClient(string alias) { }
    public void EvictAksClients() { }
    public void EvictRedisClient(string key) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Minimal <see cref="IAccessReportService"/> double — serves a canned known denial
/// and captures observed denials.</summary>
internal sealed class FakeAccessReportService : IAccessReportService
{
    public AccessDenial? KnownDenial { get; set; }
    public List<(AccessDenial Denial, string ConnectionKey)> Recorded { get; } = [];

    public Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default) =>
        Task.FromResult(new AccessReport([], DateTimeOffset.UtcNow));
    public void Invalidate(string featureArea, string? connectionKey = null) { }
    public void InvalidateAll() { }
    public void RecordObservedDenial(AccessDenial denial, string connectionKey) => Recorded.Add((denial, connectionKey));
    public bool TryGetKnownDenial(string featureArea, string connectionKey, string capability, out AccessDenial denial)
    {
        denial = KnownDenial!;
        return KnownDenial is not null;
    }
}

public class GetChangeTimelineToolTests
{
    private static readonly DateTimeOffset Since = DateTimeOffset.UtcNow.AddHours(-1);

    private static (MonitoringAlertEvaluationService Engine, InMemoryAlertHistoryRepository History, GetChangeTimelineTool Tool) Build(
        IAksClient? aksClient = null,
        IAlertRuleRepository? repo = null,
        IAccessReportService? accessReport = null,
        bool demoMode = false,
        params IAlertSignalSource[] sources)
    {
        var history = new InMemoryAlertHistoryRepository();
        var engine = new MonitoringAlertEvaluationService(
            repo ?? new AlertRuleRepository(),
            new FakeConnectionPool(),
            sources,
            new ProfileRepository(),
            new InMemoryMonitoringSilenceRepository(),
            history,
            NullLogger<MonitoringAlertEvaluationService>.Instance);
        var demo = new DemoModeService { IsDemoMode = demoMode };
        var tool = new GetChangeTimelineTool(
            engine, history, new FakeTimelineConnectionPool(aksClient), demo,
            new ProfileRepository(), accessReport);
        return (engine, history, tool);
    }

    private static JsonElement Args(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement CoverageFor(JsonElement result, string source) =>
        result.GetProperty("coverage").GetProperty("sources").EnumerateArray()
            .Single(s => s.GetProperty("source").GetString() == source);

    [Fact]
    public async Task InvalidSince_ReturnsError()
    {
        using var sandbox = new AppDataSandbox();
        var (_, _, tool) = Build();

        var result = Parse(await tool.ExecuteAsync(Args(new { since_iso = "not-a-date" }), CancellationToken.None));

        Assert.True(result.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrEmpty(error.GetString()));
    }

    [Fact]
    public async Task MergesAllSources_NewestFirst_WithConfidenceLabels()
    {
        using var _ = new AppDataSandbox();
        var now = DateTimeOffset.UtcNow;
        var aks = new FakeTimelineAksClient
        {
            Events = [new KubernetesEvent { Name = "e1", Namespace = "prod", Type = "Warning", Reason = "BackOff", Message = "restarting", LastTimestamp = now.AddMinutes(-5), Count = 3 }],
            Pods = [new PodInfo { Name = "api-1", Namespace = "prod", LastRestartTime = now.AddMinutes(-10), LastRestartReason = "OOMKilled", RestartCount = 4 }],
            Deployments = [new DeploymentInfo { Name = "api", Namespace = "prod", Status = "Available", LastUpdateTime = now.AddMinutes(-2) }],
            HelmReleases = [new HelmReleaseInfo { Name = "api", Namespace = "prod" }],
            HelmHistory = [new HelmRevisionInfo { Revision = 3, Status = "deployed", Chart = "api-1.3.0", Updated = now.AddMinutes(-8) }],
        };
        var (_, history, tool) = Build(aksClient: aks);
        await history.AppendAsync(new AlertHistoryEntry
        {
            RuleId = "r1", RuleName = "pod health", Source = AlertRuleSource.AksPodHealth,
            Kind = AlertHistoryKind.Fired, Severity = AlertSeverity.Critical,
            At = now.AddMinutes(-1), Message = "pod down",
        });

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), @namespace = "prod" }), CancellationToken.None));

        var entries = result.GetProperty("entries");
        Assert.Equal(5, result.GetProperty("entry_count").GetInt32());
        // Newest first: alert (-1m), deployment (-2m), event (-5m), helm (-8m), pod restart (-10m).
        Assert.Equal("alert_fired", entries[0].GetProperty("source").GetString());
        Assert.Equal("deployment_update", entries[1].GetProperty("source").GetString());
        Assert.Equal("kubernetes_event", entries[2].GetProperty("source").GetString());
        Assert.Equal("helm_revision", entries[3].GetProperty("source").GetString());
        Assert.Equal("pod_restart", entries[4].GetProperty("source").GetString());
        // Confidence labels per the doc's semantics.
        Assert.Equal("high", entries[0].GetProperty("confidence").GetString());
        Assert.Equal("low", entries[1].GetProperty("confidence").GetString());
        Assert.Equal("high", entries[3].GetProperty("confidence").GetString());
        // Helm revision rows carry the release name — the interface-default fan-out stamped it.
        Assert.Contains("api", entries[3].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task DedupesPersistedAndRingBuffer_FiringAppearsOnce()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        await repo.UpsertAsync(new MonitoringAlertRule
        {
            Id = "r1", Name = "pod rule", Source = AlertRuleSource.AksPodHealth, Enabled = true,
            IntervalSeconds = 10, AksPodParams = new AksPodAlertParams { Namespace = "prod" },
        });
        var (engine, _, tool) = Build(repo: repo, aksClient: null,
            sources: new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing));
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), area = "monitoring" }), CancellationToken.None));

        Assert.Equal(1, result.GetProperty("entry_count").GetInt32());
        Assert.Equal("alert_fired", result.GetProperty("entries")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task SinceFilter_ExcludesOlderEntries()
    {
        using var _ = new AppDataSandbox();
        var now = DateTimeOffset.UtcNow;
        var aks = new FakeTimelineAksClient
        {
            Events =
            [
                new KubernetesEvent
                {
                    Name = "new", Namespace = "prod", Type = "Normal", Reason = "Scheduled",
                    InvolvedObjectKind = "Pod", InvolvedObjectName = "new-pod",
                    Message = "created", LastTimestamp = now.AddMinutes(-10),
                },
                new KubernetesEvent
                {
                    Name = "old", Namespace = "prod", Type = "Normal", Reason = "Killing",
                    InvolvedObjectKind = "Pod", InvolvedObjectName = "old-pod",
                    Message = "deleted", LastTimestamp = now.AddHours(-3),
                },
            ],
        };
        var (_, _, tool) = Build(aksClient: aks);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), @namespace = "prod", area = "aks" }), CancellationToken.None));

        var entries = result.GetProperty("entries");
        Assert.Equal(1, result.GetProperty("entry_count").GetInt32());
        Assert.Contains("new-pod", entries[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Coverage_AksNotConfigured_SourcesUnchecked_ButAlertsStillReturned()
    {
        using var _ = new AppDataSandbox();
        var (_, history, tool) = Build(aksClient: null);
        await history.AppendAsync(new AlertHistoryEntry
        {
            RuleId = "r1", RuleName = "rule", Kind = AlertHistoryKind.Fired, At = DateTimeOffset.UtcNow,
        });

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O") }), CancellationToken.None));

        Assert.Equal("unchecked", CoverageFor(result, "kubernetes_events").GetProperty("status").GetString());
        Assert.Equal("unchecked", CoverageFor(result, "pod_restarts").GetProperty("status").GetString());
        Assert.Equal("unchecked", CoverageFor(result, "deployment_updates").GetProperty("status").GetString());
        Assert.Equal("unchecked", CoverageFor(result, "helm_revisions").GetProperty("status").GetString());
        Assert.Equal("checked", CoverageFor(result, "alert_history").GetProperty("status").GetString());
        Assert.Equal(1, result.GetProperty("entry_count").GetInt32());
    }

    [Fact]
    public async Task Coverage_RbacDenied_AllAksSourcesAccessDenied_AndDenialRecorded()
    {
        using var _ = new AppDataSandbox();
        var denied = new AksAccessDeniedException("Forbidden: user cannot list");
        var aks = new FakeTimelineAksClient
        {
            EventsError = denied, PodsError = denied, DeploymentsError = denied, HelmError = denied,
            // The interface-default GetHelmRevisionsAsync lists releases first, then history per
            // release — a release row is required for the history leg (and its HelmError) to run.
            HelmReleases = [new HelmReleaseInfo { Name = "api", Namespace = "default" }],
        };
        var report = new FakeAccessReportService();
        var (_, _, tool) = Build(aksClient: aks, accessReport: report, demoMode: true);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O") }), CancellationToken.None));

        foreach (var s in new[] { "kubernetes_events", "pod_restarts", "deployment_updates", "helm_revisions" })
        {
            var row = CoverageFor(result, s);
            Assert.Equal("access_denied", row.GetProperty("status").GetString());
            Assert.Equal("kubernetes.read", row.GetProperty("capability").GetString());
            Assert.False(row.GetProperty("cached").GetBoolean());
        }
        // The observed-denial feed ran — the access report's kubernetes.read row flips red.
        Assert.NotEmpty(report.Recorded);
        Assert.All(report.Recorded, r => Assert.Equal("aks", r.ConnectionKey));
        Assert.Equal(0, result.GetProperty("entry_count").GetInt32());
    }

    [Fact]
    public async Task Coverage_KnownDenial_SkipsClusterCalls_CachedTrue()
    {
        using var _ = new AppDataSandbox();
        var aks = new FakeTimelineAksClient();
        var report = new FakeAccessReportService
        {
            KnownDenial = new AccessDenial("Aks", "kubernetes.read", "Azure Kubernetes Service RBAC Reader", "ask admin", "probed 403"),
        };
        var (_, _, tool) = Build(aksClient: aks, accessReport: report, demoMode: true);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O") }), CancellationToken.None));

        var row = CoverageFor(result, "kubernetes_events");
        Assert.Equal("access_denied", row.GetProperty("status").GetString());
        Assert.True(row.GetProperty("cached").GetBoolean());
        Assert.Equal(0, aks.CallCount);
    }

    [Fact]
    public async Task Coverage_OneLegErrors_OthersStillChecked()
    {
        using var _ = new AppDataSandbox();
        var now = DateTimeOffset.UtcNow;
        var aks = new FakeTimelineAksClient
        {
            EventsError = new InvalidOperationException("boom"),
            Pods = [new PodInfo { Name = "api-1", Namespace = "prod", LastRestartTime = now.AddMinutes(-10) }],
        };
        var (_, _, tool) = Build(aksClient: aks);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), @namespace = "prod" }), CancellationToken.None));

        Assert.Equal("error", CoverageFor(result, "kubernetes_events").GetProperty("status").GetString());
        Assert.Equal("checked", CoverageFor(result, "pod_restarts").GetProperty("status").GetString());
        Assert.Equal(1, result.GetProperty("entry_count").GetInt32());
    }

    [Fact]
    public async Task AreaFilter_Monitoring_SkipsAksSources()
    {
        using var _ = new AppDataSandbox();
        var aks = new FakeTimelineAksClient();
        var (_, _, tool) = Build(aksClient: aks);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), area = "monitoring" }), CancellationToken.None));

        Assert.Equal("skipped", CoverageFor(result, "kubernetes_events").GetProperty("status").GetString());
        Assert.Equal("checked", CoverageFor(result, "alert_history").GetProperty("status").GetString());
        Assert.Equal(0, aks.CallCount);
    }

    [Fact]
    public async Task AllNamespaces_FansOutAcrossNamespaces()
    {
        using var _ = new AppDataSandbox();
        var now = DateTimeOffset.UtcNow;
        var aks = new FakeTimelineAksClient
        {
            Namespaces = ["a", "b"],
            Pods =
            [
                new PodInfo { Name = "p-a", Namespace = "a", LastRestartTime = now.AddMinutes(-5) },
                new PodInfo { Name = "p-b", Namespace = "b", LastRestartTime = now.AddMinutes(-3) },
            ],
        };
        var (_, _, tool) = Build(aksClient: aks);

        var result = Parse(await tool.ExecuteAsync(
            Args(new { since_iso = Since.ToString("O"), @namespace = "*", area = "aks" }), CancellationToken.None));

        Assert.Equal(2, result.GetProperty("entry_count").GetInt32());
        var summaries = result.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("summary").GetString()).ToList();
        Assert.Contains(summaries, s => s!.Contains("b/p-b"));
        Assert.Contains(summaries, s => s!.Contains("a/p-a"));
    }

    [Fact]
    public async Task DemoClient_PopulatesDeploymentLastUpdateTime()
    {
        // The demo client must populate the new field honestly — a null-only LastUpdateTime
        // would silently disable the deployment_update source in demo mode.
        var deployments = await new DemoAksClient().GetDeploymentsAsync("prod");

        Assert.NotEmpty(deployments);
        Assert.All(deployments, d => Assert.NotNull(d.LastUpdateTime));
    }
}
