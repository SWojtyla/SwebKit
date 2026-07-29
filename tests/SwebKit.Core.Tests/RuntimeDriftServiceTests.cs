using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class RuntimeDriftServiceTests
{
    private readonly RuntimeDriftService _service = new();

    // ── NotConfigured — missing binding ──────────────────────────────────────

    [Fact]
    public async Task GetDrift_NullBinding_ReturnsNotConfigured()
    {
        var comp = Comp("api", targetTag: "v1.2.0", binding: null);

        var result = await _service.GetDriftAsync(comp, new FakeAksClient(), CancellationToken.None);

        Assert.Equal(RuntimeDriftState.NotConfigured, result.State);
        Assert.Equal("api", result.ComponentName);
    }

    [Fact]
    public async Task GetDrift_EmptyNamespace_ReturnsNotConfigured()
    {
        var comp = Comp("api", "v1.2.0", new RuntimeBinding { Namespace = "", WorkloadName = "api" });

        var result = await _service.GetDriftAsync(comp, new FakeAksClient());

        Assert.Equal(RuntimeDriftState.NotConfigured, result.State);
    }

    [Fact]
    public async Task GetDrift_EmptyWorkloadName_ReturnsNotConfigured()
    {
        var comp = Comp("api", "v1.2.0", new RuntimeBinding { Namespace = "default", WorkloadName = "  " });

        var result = await _service.GetDriftAsync(comp, new FakeAksClient());

        Assert.Equal(RuntimeDriftState.NotConfigured, result.State);
    }

    // ── Unknown — no pods found ───────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_NoPodFound_ReturnsUnknown()
    {
        var comp = Comp("api", "v1.2.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(pods: []);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Unknown, result.State);
        Assert.Contains("No pods found", result.Note);
    }

    // ── Unknown — no target tag set ───────────────────────────────────────────

    [Fact]
    public async Task GetDrift_NoTargetTag_ReturnsUnknown()
    {
        var comp = Comp("api", targetTag: null, new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.2.0", "v1.2.0")]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Unknown, result.State);
        Assert.Contains("No target tag", result.Note);
        Assert.Equal("v1.2.0", result.ObservedTag);
    }

    // ── Matched ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_TagsMatch_ReturnsMatched()
    {
        var comp = Comp("api", "v1.2.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.2.0", "v1.2.0")]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Matched, result.State);
        Assert.Equal("v1.2.0", result.TargetTag);
        Assert.Equal("v1.2.0", result.ObservedTag);
    }

    [Fact]
    public async Task GetDrift_TagsMatchCaseInsensitive_ReturnsMatched()
    {
        var comp = Comp("api", "V1.2.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.2.0", "v1.2.0")]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Matched, result.State);
    }

    // ── Drifted ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_TagsMismatch_ReturnsDrifted()
    {
        var comp = Comp("api", "v1.3.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.2.0", "v1.2.0")]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Drifted, result.State);
        Assert.Equal("v1.3.0", result.TargetTag);
        Assert.Equal("v1.2.0", result.ObservedTag);
    }

    // ── Container name filter ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_SpecificContainerName_UsesCorrectContainer()
    {
        var comp = Comp("api", "v2.0.0", new RuntimeBinding
        {
            Namespace = "default",
            WorkloadName = "api",
            ContainerName = "sidecar"
        });
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers:
            [
                Container("app", "myrepo/api:v2.0.0", "v2.0.0"),
                Container("sidecar", "myrepo/sidecar:v1.0.0", "v1.0.0")
            ]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Drifted, result.State);
        Assert.Equal("v1.0.0", result.ObservedTag);
    }

    // ── Tag extraction from image string ─────────────────────────────────────

    [Fact]
    public async Task GetDrift_ContainerWithNoExplicitTag_ExtractsTagFromImage()
    {
        var comp = Comp("api", "v1.2.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        // ImageTag is null, but Image contains a tag suffix.
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.2.0", imageTag: null)]);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Matched, result.State);
        Assert.Equal("v1.2.0", result.ObservedTag);
    }

    // ── Batch overload ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_Batch_SkipsOutOfScopeComponents()
    {
        var inScope = Comp("api", "v1.0.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var outOfScope = new ComponentScope
        {
            ComponentName = "worker",
            ProjectName = "proj",
            RepositoryId = "",
            InScope = false,
            TargetTag = "v1.0.0",
            RuntimeBinding = new RuntimeBinding { Namespace = "default", WorkloadName = "worker" }
        };
        var fakeAks = new FakeAksClient(
            pods: [Pod("api-abc123", "default")],
            containers: [Container("app", "myrepo/api:v1.0.0", "v1.0.0")]);

        var results = await _service.GetDriftAsync([inScope, outOfScope], fakeAks);

        Assert.Single(results);
        Assert.Equal("api", results[0].ComponentName);
    }

    // ── AKS exception → Unknown ───────────────────────────────────────────────

    [Fact]
    public async Task GetDrift_AksThrows_ReturnsUnknown()
    {
        var comp = Comp("api", "v1.0.0", new RuntimeBinding { Namespace = "default", WorkloadName = "api" });
        var fakeAks = new FakeAksClient(throwOnPods: true);

        var result = await _service.GetDriftAsync(comp, fakeAks);

        Assert.Equal(RuntimeDriftState.Unknown, result.State);
        Assert.Contains("Query failed", result.Note);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ComponentScope Comp(string name, string? targetTag, RuntimeBinding? binding) =>
        new()
        {
            ComponentName = name,
            ProjectName = "proj",
            RepositoryId = "",
            InScope = true,
            TargetTag = targetTag,
            RuntimeBinding = binding
        };

    private static PodInfo Pod(string name, string ns) =>
        new() { Name = name, Namespace = ns, Phase = "Running", Status = "Running" };

    private static ContainerDetail Container(string name, string image, string? imageTag) =>
        new() { Name = name, Image = image, ImageTag = imageTag };

    // ── Fake AKS client ───────────────────────────────────────────────────────

    private sealed class FakeAksClient : IAksClient
    {
        private readonly IReadOnlyList<PodInfo> _pods;
        private readonly IReadOnlyList<ContainerDetail> _containers;
        private readonly bool _throwOnPods;

        public FakeAksClient(
            IReadOnlyList<PodInfo>? pods = null,
            IReadOnlyList<ContainerDetail>? containers = null,
            bool throwOnPods = false)
        {
            _pods = pods ?? [];
            _containers = containers ?? [];
            _throwOnPods = throwOnPods;
        }

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
        {
            if (_throwOnPods) throw new InvalidOperationException("Simulated AKS failure.");
            return Task.FromResult(_pods);
        }

        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default)
            => Task.FromResult(_containers);

        // ── Unused members ────────────────────────────────────────────────────
        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);
        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
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
        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
        public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(new HelmReleaseValues { UserValues = string.Empty, ComputedValues = string.Empty });
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
        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
    }
}
