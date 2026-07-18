using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Core.Constants;

namespace SwebKit.Core.Tests;

public class DemoAksClientTests
{
    private readonly DemoAksClient _client = new();

    [Fact]
    public async Task GetDeploymentsAsync_ReturnsNonEmptyList()
    {
        var result = await _client.GetDeploymentsAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.Equal("default", d.Namespace);
        });
    }

    [Fact]
    public async Task GetPodsAsync_ReturnsPodsForAllDeployments()
    {
        var pods = await _client.GetPodsAsync("ecommerce");

        Assert.NotEmpty(pods);
        Assert.All(pods, p =>
        {
            Assert.Equal("ecommerce", p.Namespace);
            Assert.NotEmpty(p.Containers);
        });
    }

    [Fact]
    public async Task GetPodsAsync_LabelSelector_FiltersPods()
    {
        var all = await _client.GetPodsAsync("default");
        var filtered = await _client.GetPodsAsync("default", "app=order-api");

        Assert.True(filtered.Count < all.Count);
        Assert.All(filtered, p => Assert.Equal("order-api", p.Labels["app"]));
    }

    [Fact]
    public async Task ScaleDeploymentAsync_CompletesWithoutError()
    {
        await _client.ScaleDeploymentAsync("default", "order-api", 5);
    }

    [Fact]
    public async Task RestartDeploymentAsync_CompletesWithoutError()
    {
        await _client.RestartDeploymentAsync("default", "order-api");
    }

    [Fact]
    public async Task DeletePodAsync_CompletesWithoutError()
    {
        await _client.DeletePodAsync("default", "order-api-abc-xyz");
    }

    [Fact]
    public async Task GetHelmReleasesAsync_ReturnsNonEmptyList()
    {
        var releases = await _client.GetHelmReleasesAsync("default");

        Assert.NotEmpty(releases);
        Assert.Contains(releases, r => r.Status == "deployed");
        Assert.Contains(releases, r => r.Status == "failed");
    }

    [Fact]
    public async Task GetHelmReleaseHistoryAsync_ReturnsOrderedRevisions()
    {
        var history = await _client.GetHelmReleaseHistoryAsync("default", "order-api");

        Assert.Equal(4, history.Count);
        Assert.Equal(1, history[0].Revision);
        Assert.Equal(4, history[3].Revision);
        Assert.Equal("deployed", history[3].Status);
        Assert.All(history, r => Assert.NotNull(r.Chart));
    }

    [Fact]
    public async Task GetHelmReleaseValuesAsync_ReturnsYamlString()
    {
        var values = await _client.GetHelmReleaseValuesAsync("default", "order-api");

        Assert.Contains("replicaCount", values);
        Assert.Contains("order-api", values);
        Assert.Contains("image", values);
    }

    [Fact]
    public async Task RollbackHelmReleaseAsync_CompletesWithoutError()
    {
        await _client.RollbackHelmReleaseAsync("default", "order-api", 3);
    }

    [Fact]
    public async Task GetPodMetricsAsync_ReturnsMetricsForAllPods()
    {
        var metrics = await _client.GetPodMetricsAsync("default");

        Assert.NotEmpty(metrics);
        Assert.All(metrics, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.PodName));
            Assert.Equal("default", m.Namespace);
            Assert.NotEmpty(m.Containers);
            Assert.All(m.Containers, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
                Assert.True(c.CpuCores >= 0);
                Assert.True(c.MemoryBytes > 0);
            });
        });
    }

    [Fact]
    public async Task GetIngressesAsync_ReturnsNonEmptyList()
    {
        var ingresses = await _client.GetIngressesAsync("default");

        Assert.NotEmpty(ingresses);
        Assert.All(ingresses, i => Assert.NotEmpty(i.Rules));
    }

    [Fact]
    public async Task AnalyzeIngressAsync_ReturnsBackendEvidence()
    {
        var analysis = await _client.AnalyzeIngressAsync("default", "main-ingress");

        Assert.Equal("default", analysis.Namespace);
        Assert.Equal("main-ingress", analysis.IngressName);
        Assert.NotEmpty(analysis.Backends);
        Assert.Contains(analysis.Backends, backend => backend.ServiceName == "order-api" && backend.MatchingPodCount > 0);
        Assert.NotEmpty(analysis.Findings);
    }

    [Fact]
    public async Task AnalyzeNetworkPoliciesAsync_ReturnsPolicyEvidenceForDeployment()
    {
        var analysis = await _client.AnalyzeNetworkPoliciesAsync("default", "Deployment", "order-api");

        Assert.Equal("default", analysis.Namespace);
        Assert.Equal("Deployment", analysis.WorkloadKind);
        Assert.Equal("order-api", analysis.WorkloadName);
        Assert.True(analysis.MatchingPodCount > 0);
        Assert.Contains(analysis.Services, service => service.Contains("order-api", StringComparison.Ordinal));
        Assert.NotEmpty(analysis.Policies);
    }

    [Fact]
    public async Task GetServicesAsync_ReturnsServicesWithPorts()
    {
        var services = await _client.GetServicesAsync("default");

        Assert.NotEmpty(services);
        Assert.Contains(services, service => service.Type == "LoadBalancer");
        Assert.All(services, service =>
        {
            Assert.Equal("default", service.Namespace);
            Assert.NotEmpty(service.Ports);
        });
    }

    [Fact]
    public async Task GetGatewayClassesAsync_ReturnsClusterScopedGatewayClasses()
    {
        var gatewayClasses = await _client.GetGatewayClassesAsync();

        Assert.NotEmpty(gatewayClasses);
        Assert.Contains(gatewayClasses, gatewayClass => gatewayClass.IsDefault);
        Assert.All(gatewayClasses, gatewayClass =>
        {
            Assert.False(string.IsNullOrWhiteSpace(gatewayClass.Name));
            Assert.False(string.IsNullOrWhiteSpace(gatewayClass.ControllerName));
        });
    }

    [Fact]
    public async Task GetNamespacesAsync_ReturnsKnownNamespaces()
    {
        var namespaces = await _client.GetNamespacesAsync();

        Assert.Contains("default", namespaces);
        Assert.Contains("ecommerce", namespaces);
        Assert.Contains("payments", namespaces);
        Assert.Contains("infrastructure", namespaces);
        Assert.Contains("monitoring", namespaces);
    }

    [Fact]
    public async Task GetNamespacesAsync_IncludesDefaultForAllNamespaceJobsAndCronJobs()
    {
        var namespaces = await _client.GetNamespacesAsync();
        var client = (Abstractions.IAksClient)_client;

        var jobs = await client.GetJobsAsync(namespaces);
        var cronJobs = await client.GetCronJobsAsync(namespaces);

        Assert.Contains(jobs, job => job.Namespace == "default");
        Assert.Contains(cronJobs, cronJob => cronJob.Namespace == "default");
    }

    [Fact]
    public async Task GetContextsAsync_ReturnsContextsWithOneCurrent()
    {
        var contexts = await _client.GetContextsAsync();

        Assert.NotEmpty(contexts);
        Assert.Single(contexts, c => c.IsCurrent);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByInvolvedObject()
    {
        var all = await _client.GetEventsAsync("default");
        var filtered = await _client.GetEventsAsync("default", "order-api");

        Assert.True(filtered.Count < all.Count);
        Assert.All(filtered, e =>
            Assert.Contains("order-api", e.InvolvedObjectName!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue()
    {
        Assert.True(await _client.TestConnectionAsync());
    }

    [Fact]
    public async Task MultiNamespace_GetDeploymentsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetDeploymentsAsync(new List<string> { "ns1", "ns2" });

        // Should have deployments from both namespaces
        Assert.Contains(result, d => d.Namespace == "ns1");
        Assert.Contains(result, d => d.Namespace == "ns2");
        // Should have roughly double the single-namespace count
        var single = await _client.GetDeploymentsAsync("ns1");
        Assert.True(result.Count > single.Count);
    }

    [Fact]
    public async Task MultiNamespace_GetPodsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetPodsAsync(new List<string> { "a", "b" });

        Assert.Contains(result, p => p.Namespace == "a");
        Assert.Contains(result, p => p.Namespace == "b");
    }

    [Fact]
    public async Task MultiNamespace_GetCronJobsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetCronJobsAsync(new List<string> { "ns1", "ns2" });

        Assert.Contains(result, cronJob => cronJob.Namespace == "ns1");
        Assert.Contains(result, cronJob => cronJob.Namespace == "ns2");
    }

    [Fact]
    public async Task MultiNamespace_GetJobsAsync_MergesResults()
    {
        var client = (Abstractions.IAksClient)_client;
        var result = await client.GetJobsAsync(new List<string> { "jobs-a", "jobs-b" });

        Assert.Contains(result, job => job.Namespace == "jobs-a");
        Assert.Contains(result, job => job.Namespace == "jobs-b");
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsBatchJobsWithStatusAndProvenance()
    {
        var jobs = await _client.GetJobsAsync("default");

        Assert.NotEmpty(jobs);
        Assert.Contains(jobs, job => job.Status == "Active");
        Assert.Contains(jobs, job => job.SourceKind == "CronJob" && !string.IsNullOrWhiteSpace(job.SourceName));
        Assert.All(jobs, job => Assert.Equal("default", job.Namespace));
    }

    [Fact]
    public async Task GetResourceYamlAsync_Job_ReturnsBatchV1Yaml()
    {
        var job = (await _client.GetJobsAsync("default")).First();

        var yaml = await _client.GetResourceYamlAsync("default", "Job", job.Name);

        Assert.Contains("apiVersion: batch/v1", yaml);
        Assert.Contains("kind: Job", yaml);
        Assert.Contains($"name: {job.Name}", yaml);
    }

    [Fact]
    public async Task GetResourceYamlAsync_GatewayClass_ReturnsClusterScopedGatewayApiYaml()
    {
        var gatewayClass = (await _client.GetGatewayClassesAsync()).First();

        var yaml = await _client.GetResourceYamlAsync(string.Empty, "GatewayClass", gatewayClass.Name);
        var normalizedYaml = yaml.ReplaceLineEndings("\n");

        Assert.Contains("apiVersion: gateway.networking.k8s.io/v1", yaml);
        Assert.Contains("kind: GatewayClass", yaml);
        Assert.Contains($"name: {gatewayClass.Name}", yaml);
        Assert.DoesNotContain($"metadata:\n  name: {gatewayClass.Name}\n  namespace:", normalizedYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResourceYamlAsync_Service_ReturnsServiceYaml()
    {
        var service = (await _client.GetServicesAsync("default")).First();

        var yaml = await _client.GetResourceYamlAsync("default", "Service", service.Name);

        Assert.Contains("apiVersion: v1", yaml);
        Assert.Contains("kind: Service", yaml);
        Assert.Contains($"name: {service.Name}", yaml);
    }

    [Fact]
    public async Task TriggerCronJobAsync_CreatesVisibleJobWithCronJobProvenance()
    {
        var createdName = await _client.TriggerCronJobAsync("default", "inventory-sync");

        Assert.StartsWith("inventory-sync-manual-", createdName, StringComparison.Ordinal);

        var jobs = await _client.GetJobsAsync("default");
        var createdJob = Assert.Single(jobs, job => job.Name == createdName);
        Assert.Equal("CronJob", createdJob.SourceKind);
        Assert.Equal("inventory-sync", createdJob.SourceName);
        Assert.Equal("Active", createdJob.Status);

        var yaml = await _client.GetResourceYamlAsync("default", "Job", createdName);
        Assert.Contains($"{AksBatchAnnotations.SourceKind}: CronJob", yaml);
        Assert.Contains($"{AksBatchAnnotations.SourceName}: inventory-sync", yaml);
    }

    [Fact]
    public async Task RerunJobAsync_CreatesSiblingJobWithJobProvenance()
    {
        var sourceJob = (await _client.GetJobsAsync("default"))
            .First(job => job.SourceKind == "CronJob");

        var createdName = await _client.RerunJobAsync("default", sourceJob.Name);

        Assert.NotEqual(sourceJob.Name, createdName);
        Assert.StartsWith($"{sourceJob.Name}-rerun-", createdName, StringComparison.Ordinal);

        var jobs = await _client.GetJobsAsync("default");
        var createdJob = Assert.Single(jobs, job => job.Name == createdName);
        Assert.Equal("Job", createdJob.SourceKind);
        Assert.Equal(sourceJob.Name, createdJob.SourceName);
        Assert.Equal(sourceJob.DesiredCompletions ?? 1, createdJob.DesiredCompletions);
    }

    // ── New feature tests ──

    [Fact]
    public async Task GetStatefulSetsAsync_ReturnsExpectedRecords()
    {
        var result = await _client.GetStatefulSetsAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.True(s.Replicas >= 0);
            Assert.True(s.ReadyReplicas >= 0);
        });
    }

    [Fact]
    public async Task GetStatefulSetsAsync_DegradedHasLessReadyThanReplicas()
    {
        var result = await _client.GetStatefulSetsAsync("default");

        // Demo data always includes at least one degraded stateful set
        Assert.Contains(result, s => s.ReadyReplicas < s.Replicas);
    }

    [Fact]
    public async Task GetConfigMapsAsync_ReturnsNonEmptyList()
    {
        var result = await _client.GetConfigMapsAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, cm =>
        {
            Assert.False(string.IsNullOrWhiteSpace(cm.Name));
            Assert.NotNull(cm.Data);
        });
    }

    [Fact]
    public async Task GetSecretsAsync_ReturnsNonEmptyListWithKeys()
    {
        var result = await _client.GetSecretsAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.NotEmpty(s.Keys);
        });
    }

    [Fact]
    public async Task GetSecretValuesAsync_ReturnsDecodedValues()
    {
        var secrets = await _client.GetSecretsAsync("default");
        var first = secrets.First();

        var values = await _client.GetSecretValuesAsync("default", first.Name);

        Assert.NotNull(values);
        Assert.NotEmpty(values);
        Assert.All(values, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.NotNull(kv.Value);
        });
    }

    [Fact]
    public async Task GetContainerDetailsAsync_IncludesConfigMapRefAndSecretRef()
    {
        var pods = await _client.GetPodsAsync("default");
        var pod = pods.First();

        var containers = await _client.GetContainerDetailsAsync("default", pod.Name);

        Assert.NotEmpty(containers);

        var allEnv = containers.SelectMany(c => c.EnvVars).ToList();
        Assert.Contains(allEnv, e => e.Source == Models.EnvVarSourceKind.ConfigMapRef);
        Assert.Contains(allEnv, e => e.Source == Models.EnvVarSourceKind.SecretRef);
    }

    [Fact]
    public async Task GetContainerDetailsAsync_AllContainersHaveImageAndName()
    {
        var pods = await _client.GetPodsAsync("default");
        var pod = pods.First();

        var containers = await _client.GetContainerDetailsAsync("default", pod.Name);

        Assert.All(containers, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Image));
        });
    }

    [Fact]
    public async Task GetHpasAsync_ReturnsNonEmptyList()
    {
        var result = await _client.GetHpasAsync("default");

        Assert.NotEmpty(result);
        Assert.All(result, h =>
        {
            Assert.False(string.IsNullOrWhiteSpace(h.Name));
            Assert.False(string.IsNullOrWhiteSpace(h.TargetName));
            Assert.True(h.MinReplicas >= 0);
            Assert.True(h.MaxReplicas >= h.MinReplicas);
        });
    }

    [Fact]
    public async Task GetHpasAsync_AllTargetNamesMatchKnownWorkloads()
    {
        var deployments = (await _client.GetDeploymentsAsync("default")).Select(d => d.Name).ToHashSet();
        var statefulSets = (await _client.GetStatefulSetsAsync("default")).Select(s => s.Name).ToHashSet();
        var knownWorkloads = deployments.Concat(statefulSets).ToHashSet();

        var hpas = await _client.GetHpasAsync("default");

        Assert.All(hpas, h => Assert.Contains(h.TargetName, knownWorkloads));
    }

    [Fact]
    public async Task GetHpasAsync_MarksKedaManagedHpaWithScaledObjectName()
    {
        var hpas = await _client.GetHpasAsync("default");

        var keda = Assert.Single(hpas, h => h.IsKedaManaged);
        Assert.False(string.IsNullOrWhiteSpace(keda.ScaledObjectName));
        Assert.All(hpas, h => Assert.False(h.IsScalingDisabled));
    }

    [Fact]
    public async Task SetHpaScalingEnabledAsync_TogglesDisabledStateAcrossReads()
    {
        var hpa = (await _client.GetHpasAsync("default")).First();

        await _client.SetHpaScalingEnabledAsync("default", hpa.Name, enabled: false);
        var afterDisable = (await _client.GetHpasAsync("default")).First(h => h.Name == hpa.Name);
        Assert.True(afterDisable.IsScalingDisabled);

        await _client.SetHpaScalingEnabledAsync("default", hpa.Name, enabled: true);
        var afterEnable = (await _client.GetHpasAsync("default")).First(h => h.Name == hpa.Name);
        Assert.False(afterEnable.IsScalingDisabled);
    }

    [Fact]
    public async Task SetHpaScalingEnabledAsync_DisableIsScopedToTargetHpa()
    {
        var hpas = (await _client.GetHpasAsync("default")).ToList();
        var target = hpas[0];

        await _client.SetHpaScalingEnabledAsync("default", target.Name, enabled: false);

        var reread = (await _client.GetHpasAsync("default")).ToList();
        Assert.True(reread.Single(h => h.Name == target.Name).IsScalingDisabled);
        Assert.All(reread.Where(h => h.Name != target.Name), h => Assert.False(h.IsScalingDisabled));
    }

    [Fact]
    public async Task StreamDeploymentLogsAsync_EmitsLinesWithPodName()
    {
        var lines = new List<AggregatedLogLine>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var opts = new Models.LogStreamOptions { Follow = false, TailLines = 20 };

        try
        {
            await foreach (var line in _client.StreamDeploymentLogsAsync("default", "order-api", opts, cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 10) cts.Cancel();
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        Assert.NotEmpty(lines);
        Assert.All(lines, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.PodName));
            Assert.False(string.IsNullOrWhiteSpace(l.Line));
        });
    }

    [Fact]
    public async Task StreamPodLogsAsync_SinceSeconds_ReturnsRecentWindow()
    {
        var lines = new List<string>();
        var opts = new LogStreamOptions { Follow = false, SinceSeconds = 5 };

        await foreach (var line in _client.StreamPodLogsAsync("default", "order-api-pod", "order-api", opts))
            lines.Add(line);

        Assert.Equal(5, lines.Count);
    }

    [Fact]
    public async Task StreamPodLogsAsync_PreviousContainer_EmitsMarkedLines_AndDoesNotFollow()
    {
        var lines = new List<string>();
        var opts = new LogStreamOptions
        {
            Follow = true,
            TailLines = 3,
            PreviousContainer = true
        };

        await foreach (var line in _client.StreamPodLogsAsync("default", "order-api-pod", "order-api", opts))
            lines.Add(line);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Contains("[PREVIOUS]", line));
    }

    [Fact]
    public async Task StreamDeploymentLogsAsync_PreviousContainer_EmitsMarkedLines_AndDoesNotFollow()
    {
        var lines = new List<AggregatedLogLine>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var opts = new LogStreamOptions
        {
            Follow = true,
            TailLines = 1,
            PreviousContainer = true
        };

        await foreach (var line in _client.StreamDeploymentLogsAsync("default", "order-api", opts, cts.Token))
            lines.Add(line);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains("[PREVIOUS]", line.Line));
    }

    [Fact]
    public async Task RestartStatefulSetAsync_CompletesWithoutError()
    {
        await _client.RestartStatefulSetAsync("default", "order-queue");
    }

    [Fact]
    public async Task ScaleStatefulSetAsync_CompletesWithoutError()
    {
        await _client.ScaleStatefulSetAsync("default", "order-queue", 3);
    }

    [Fact]
    public async Task GetResourceQuotasAsync_ReturnsQuotasWithUsage()
    {
        var quotas = await _client.GetResourceQuotasAsync("default");

        Assert.NotEmpty(quotas);
        Assert.All(quotas, q =>
        {
            Assert.Equal("default", q.Namespace);
            Assert.NotEmpty(q.HardLimits);
            Assert.NotEmpty(q.Used);
        });
    }

    [Fact]
    public async Task GetLimitRangesAsync_ReturnsLimitsForNamespace()
    {
        var ranges = await _client.GetLimitRangesAsync("default");

        Assert.NotEmpty(ranges);
        Assert.All(ranges, r =>
        {
            Assert.Equal("default", r.Namespace);
            Assert.NotEmpty(r.Limits);
        });
    }

    [Fact]
    public async Task GetPodDisruptionBudgetsAsync_ReturnsPdbsWithStatus()
    {
        var pdbs = await _client.GetPodDisruptionBudgetsAsync("default");

        Assert.NotEmpty(pdbs);
        Assert.All(pdbs, pdb =>
        {
            Assert.Equal("default", pdb.Namespace);
            Assert.True(pdb.ExpectedPods > 0);
        });
    }

    [Fact]
    public async Task GetProbeFailureSummaryAsync_ReturnsRestartEvidence()
    {
        var summary = await _client.GetProbeFailureSummaryAsync("default", "Deployment", "order-api");

        Assert.Equal("default", summary.Namespace);
        Assert.True(summary.TotalPods > 0);
        Assert.NotEmpty(summary.Findings);
        Assert.Contains(summary.Pods, p => p.LivenessProbeConfigured || p.ReadinessProbeConfigured);
    }

    [Fact]
    public async Task GetPlacementAnalysisAsync_ReturnsDeclaredConstraints()
    {
        var analysis = await _client.GetPlacementAnalysisAsync("default", "Deployment", "order-api");

        Assert.Equal("default", analysis.Namespace);
        Assert.NotEmpty(analysis.Findings);
        Assert.True(analysis.HasNodeSelector || analysis.HasPodAntiAffinity || analysis.HasNodeAffinity);
    }

    [Fact]
    public async Task PreviewHelmUpgradeAsync_ReturnsDegradedOrUnsupportedInDemoMode()
    {
        var preview = await _client.PreviewHelmUpgradeAsync("default", "order-api");

        Assert.Equal("default", preview.Namespace);
        Assert.Equal("order-api", preview.ReleaseName);
        Assert.True(preview.Capability == HelmPreviewCapability.Unsupported
            || preview.Capability == HelmPreviewCapability.Degraded);
        Assert.False(string.IsNullOrWhiteSpace(preview.CapabilityNote));
    }
}
