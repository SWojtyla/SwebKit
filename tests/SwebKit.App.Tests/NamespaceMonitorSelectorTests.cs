using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class NamespaceMonitorSelectorTests : TestContext
{
    public NamespaceMonitorSelectorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddSingleton<IPodHealthMonitorService>(new TestPodHealthMonitorService());
        Services.AddSingleton<INotificationService>(new TestNotificationService());
    }

    [Fact]
    public void NamespaceMonitorSelector_NamespacesLoaded_RendersFilterInput()
    {
        var cut = RenderSelector("default", "kube-system", "payments");

        cut.WaitForAssertion(() =>
        {
            var filter = cut.Find("input.ns-monitor-filter-input");
            Assert.Equal("Filter namespaces", filter.GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void NamespaceMonitorSelector_Filtering_NarrowsVisibleItemsCaseInsensitively()
    {
        var cut = RenderSelector("default", "kube-system", "payments");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".ns-monitor-item-label").Count));

        cut.Find("input.ns-monitor-filter-input").Input("KUBE-SYS");

        cut.WaitForAssertion(() =>
        {
            var labels = cut.FindAll(".ns-monitor-item-label")
                .Select(label => label.TextContent.Trim())
                .ToList();

            var visibleNamespace = Assert.Single(labels);
            Assert.Equal("kube-system", visibleNamespace);
        });
    }

    [Fact]
    public void NamespaceMonitorSelector_Filtering_NoMatches_ShowsEmptyMessage()
    {
        var cut = RenderSelector("default", "kube-system", "payments");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".ns-monitor-item-label").Count));

        cut.Find("input.ns-monitor-filter-input").Input("no-match-value");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No namespaces match the current filter.", cut.Markup);
            Assert.Empty(cut.FindAll(".ns-monitor-item"));
        });
    }

    private IRenderedComponent<NamespaceMonitorSelector> RenderSelector(params string[] namespaces)
        => RenderComponent<NamespaceMonitorSelector>(ps => ps
            .Add(p => p.AksClient, new TestAksClient(namespaces))
            .Add(p => p.CurrentClusterContext, "test-context"));

    private sealed class TestAksClient(params string[] namespaces) : IAksClient
    {
        private readonly IReadOnlyList<string> _namespaces = namespaces.ToList();

        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeploymentInfo>>([]);

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PodInfo>>([]);

        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);

        public IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts,
            CancellationToken ct = default)
            => EmptyLogLines();

        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort,
            CancellationToken ct = default)
            => Task.FromResult(new PortForwardSession
            {
                Namespace = ns,
                ResourceName = resourceName,
                LocalPort = localPort,
                RemotePort = remotePort,
                Status = PortForwardStatus.Active
            });

        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IngressInfo>>([]);

        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayInfo>>([]);

        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HttpRouteInfo>>([]);

        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);

        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
            => Task.FromResult(_namespaces);

        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KubeContextInfo>>([]);

        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);

        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PodMetrics>>([]);

        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
            => Task.CompletedTask;

        public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName,
            LogStreamOptions opts, CancellationToken ct = default)
            => EmptyAggregatedLogLines();

        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatefulSetInfo>>([]);

        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConfigMapInfo>>([]);

        public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecretInfo>>([]);

        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<string, string>());

        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);

        public Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HpaInfo>>([]);

        public Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CronJobInfo>>([]);

        private static async IAsyncEnumerable<string> EmptyLogLines()
        {
            yield break;
        }

        private static async IAsyncEnumerable<AggregatedLogLine> EmptyAggregatedLogLines()
        {
            yield break;
        }
    }

    private sealed class TestPodHealthMonitorService : IPodHealthMonitorService
    {
        private readonly HashSet<string> _monitoredNamespaces = [];

        public bool IsMonitoring { get; private set; }
        public IReadOnlyList<string> MonitoredNamespaces => _monitoredNamespaces.ToList();
        public IReadOnlyList<PodHealthEvent> RecentEvents => [];

        public event Action<PodHealthEvent>? PodHealthDetected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            IsMonitoring = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsMonitoring = false;
            return Task.CompletedTask;
        }

        public Task AddNamespaceAsync(string ns)
        {
            _monitoredNamespaces.Add(ns);
            return Task.CompletedTask;
        }

        public Task RemoveNamespaceAsync(string ns)
        {
            _monitoredNamespaces.Remove(ns);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestNotificationService : INotificationService
    {
        public IReadOnlyList<Notification> All => [];

        public event Action? NotificationsChanged
        {
            add { }
            remove { }
        }

        public void ShowSuccess(string message, string? detail = null)
        {
        }

        public void ShowWarning(string message, string? detail = null)
        {
        }

        public void ShowError(string message, string? detail = null, Exception? ex = null)
        {
        }

        public void ShowInfo(string message, string? detail = null)
        {
        }

        public void Dismiss(Guid id)
        {
        }

        public void ClearAll()
        {
        }
    }
}