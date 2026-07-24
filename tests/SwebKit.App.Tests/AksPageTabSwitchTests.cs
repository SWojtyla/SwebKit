using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

/// <summary>
/// Covers namespace-switch staleness and tab-switch-mid-load behavior for AksPage: switching to a
/// namespace with no cached snapshot must not leave the previous namespace's data on screen, a tab
/// switched to before its own dataset has arrived must show a loading placeholder rather than an
/// empty grid, and switching tabs repeatedly while a load is in flight must never trigger extra
/// network calls (tab switches are a pure local render-state change, not a fetch trigger).
/// </summary>
[Collection("AppDataSerial")]
public sealed class AksPageTabSwitchTests : TestContext
{
    private readonly AppStateService _appState;

    public AksPageTabSwitchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var uiState = new UiStateRepository();

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        _appState = new AppStateService(new ProfileRepository(), uiState, eventBus);
        _appState.Config.AksConfig = new AksConfig
        {
            DefaultNamespace = "ns-a",
            KubeconfigContext = "test-context"
        };

        var userSettings = new UserSettingsRepository();

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton(userSettings);
        Services.AddSingleton<INotificationService>(new NotificationService(uiState));
        Services.AddSingleton<IPortForwardSessionService>(new FakePortForwardSessionService());
        Services.AddSingleton(new PinnedPortForwardService(userSettings));
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton(new PageDataCache());
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IPodHealthMonitorService>(new FakePodHealthMonitorService());
        Services.AddSingleton<IAksClientBootstrapper>(new FakeAksClientBootstrapper());
        Services.AddSingleton<IAksWarmupCache>(new AksWarmupCache());
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void SwitchingNamespace_WithNoCache_DoesNotLeavePreviousNamespacesDataOnScreen()
    {
        var client = new NamespaceAwareAksClient();
        var cut = RenderComponent<AksPage>(parameters => parameters.Add(page => page.ClientOverride, client));

        cut.WaitForAssertion(() => Assert.Contains("alpha-deploy", cut.Markup, StringComparison.Ordinal));

        // Gate every ns-b dataset so the switch never completes until we release it below — this
        // lets the test observe the instant right after the switch starts, before any ns-b data
        // has arrived, without a race against the (fake, instant) network calls.
        client.GateAllDatasets("ns-b");
        InvokePrivateMethodFireAndForget(cut, "HandleNamespaceChangedAsync", "ns-b");

        cut.WaitForAssertion(() =>
        {
            // ns-a's deployment must be gone immediately — never shown mislabeled under ns-b.
            Assert.DoesNotContain("alpha-deploy", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("beta-deploy", cut.Markup, StringComparison.Ordinal);
        });

        client.ReleaseAllDatasets("ns-b");

        cut.WaitForAssertion(() => Assert.Contains("beta-deploy", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void SwitchingToTabBeforeItsDataArrives_ShowsLoadingPlaceholder_ThenReplacesItWithData()
    {
        var client = new NamespaceAwareAksClient();
        var cut = RenderComponent<AksPage>(parameters => parameters.Add(page => page.ClientOverride, client));
        cut.WaitForAssertion(() => Assert.Contains("alpha-deploy", cut.Markup, StringComparison.Ordinal));

        // Open Pods once so its LazyPanel mounts (BL-4: a panel's content only renders after its
        // first activation) — this also confirms ns-a's ordinary, ungated Pods fetch works.
        OpenResourceTab(cut, "Pods");
        cut.WaitForAssertion(() => Assert.Contains("alpha-pod", cut.Markup, StringComparison.Ordinal));
        OpenResourceTab(cut, "Deployments");

        // Gate ns-b's Pods, then switch namespace — with no cache for ns-b, all lists (including
        // the still-mounted Pods panel's) clear to empty before the gated fetch starts.
        client.GatePods("ns-b");
        InvokePrivateMethodFireAndForget(cut, "HandleNamespaceChangedAsync", "ns-b");
        cut.WaitForAssertion(() => Assert.DoesNotContain("alpha-deploy", cut.Markup, StringComparison.Ordinal));

        OpenResourceTab(cut, "Pods");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Loading pods", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alpha-pod", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("beta-pod", cut.Markup, StringComparison.Ordinal);
        });

        client.ReleasePods("ns-b");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("beta-pod", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Loading pods", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SwitchingTabsRepeatedly_DuringInFlightLoad_NeverTriggersExtraFetches()
    {
        var client = new NamespaceAwareAksClient();
        var cut = RenderComponent<AksPage>(parameters => parameters.Add(page => page.ClientOverride, client));
        cut.WaitForAssertion(() => Assert.Contains("alpha-deploy", cut.Markup, StringComparison.Ordinal));

        OpenResourceTab(cut, "Pods");
        cut.WaitForAssertion(() => Assert.Contains("alpha-pod", cut.Markup, StringComparison.Ordinal));
        OpenResourceTab(cut, "Deployments");

        client.GatePods("ns-b");
        InvokePrivateMethodFireAndForget(cut, "HandleNamespaceChangedAsync", "ns-b");
        cut.WaitForAssertion(() => Assert.DoesNotContain("alpha-deploy", cut.Markup, StringComparison.Ordinal));

        // A user impatiently flipping between tabs before ns-b's Pods has resolved must never
        // re-trigger GetPodsAsync — switching tabs only changes which already-in-flight (or
        // already-loaded) dataset is visible, it never starts or cancels a fetch.
        for (var i = 0; i < 5; i++)
        {
            OpenResourceTab(cut, "Pods");
            OpenResourceTab(cut, "Deployments");
        }
        OpenResourceTab(cut, "Pods");

        // One call from the initial ns-a load, one for ns-b — never more, however many times the
        // user flipped tabs while ns-b's fetch was gated.
        Assert.Equal(1, client.GetPodsCallCount("ns-a"));
        Assert.Equal(1, client.GetPodsCallCount("ns-b"));

        client.ReleasePods("ns-b");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("beta-pod", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Loading pods", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal(1, client.GetPodsCallCount("ns-b"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void OpenResourceTab(IRenderedComponent<AksPage> cut, string tabText)
    {
        // Find-then-click as one dispatched operation, and wait for that dispatch to finish before
        // returning: a background render (the gated load's continuations firing concurrently) can
        // otherwise replace the render tree between a separate Find and Click call (stale element
        // reference), and letting clicks queue up unobserved makes render/assertion timing in the
        // rest of the test nondeterministic.
        cut.InvokeAsync(() =>
        {
            cut.FindAll("button.aks-resource-tab")
                .Single(button => string.Equals(button.TextContent.Trim(), tabText, StringComparison.Ordinal))
                .Click();
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Starts an async private method and blocks until it fully completes. Only safe to use when
    /// nothing in the test needs to observe an intermediate (gated/in-flight) state — for that,
    /// use <see cref="InvokePrivateMethodFireAndForget"/> instead, otherwise this deadlocks: it
    /// would block the test thread on a task that can only complete after a gate the test hasn't
    /// released yet.
    /// </summary>
    private static void InvokePrivateMethod(IRenderedComponent<AksPage> cut, string methodName, params object?[] args)
    {
        var method = typeof(AksPage).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        cut.InvokeAsync(async () =>
        {
            var result = method!.Invoke(cut.Instance, args);
            if (result is Task task)
                await task;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Starts an async private method without waiting for it to finish — mirrors how a real UI
    /// event dispatches a handler and returns immediately. Use this whenever the method being
    /// invoked can be gated (blocked on a <see cref="TaskCompletionSource"/> the test controls) so
    /// the test can assert on the in-flight state before releasing the gate.
    /// </summary>
    private static void InvokePrivateMethodFireAndForget(IRenderedComponent<AksPage> cut, string methodName, params object?[] args)
    {
        var method = typeof(AksPage).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        cut.InvokeAsync(() => { _ = (Task)method!.Invoke(cut.Instance, args)!; });
    }

    // ── Fake IAksClient with controllable per-namespace gates ────────────────────

    private sealed class NamespaceAwareAksClient : IAksClient
    {
        private readonly Dictionary<string, List<DeploymentInfo>> _deploymentsByNamespace;
        private readonly Dictionary<string, List<PodInfo>> _podsByNamespace;
        private readonly Dictionary<string, TaskCompletionSource> _podGates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource> _allGates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _podsCallCounts = new(StringComparer.Ordinal);
        private readonly Lock _lock = new();

        public NamespaceAwareAksClient()
        {
            _deploymentsByNamespace = new(StringComparer.Ordinal)
            {
                ["ns-a"] = [new DeploymentInfo { Name = "alpha-deploy", Namespace = "ns-a", Replicas = 1, ReadyReplicas = 1, Status = "Available" }],
                ["ns-b"] = [new DeploymentInfo { Name = "beta-deploy", Namespace = "ns-b", Replicas = 1, ReadyReplicas = 1, Status = "Available" }],
            };
            _podsByNamespace = new(StringComparer.Ordinal)
            {
                ["ns-a"] = [new PodInfo { Name = "alpha-pod", Namespace = "ns-a", Phase = "Running", Status = "Running", Ready = true, ReadyContainers = 1, TotalContainers = 1 }],
                ["ns-b"] = [new PodInfo { Name = "beta-pod", Namespace = "ns-b", Phase = "Running", Status = "Running", Ready = true, ReadyContainers = 1, TotalContainers = 1 }],
            };
        }

        public void GatePods(string ns)
        {
            lock (_lock) { _podGates[ns] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); }
        }

        public void ReleasePods(string ns)
        {
            lock (_lock) { if (_podGates.TryGetValue(ns, out var gate)) gate.TrySetResult(); }
        }

        /// <summary>Gates every dataset for <paramref name="ns"/> — used to freeze a namespace switch
        /// mid-flight so the test can observe the instant before any of its data has arrived.</summary>
        public void GateAllDatasets(string ns)
        {
            lock (_lock) { _allGates[ns] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); }
        }

        public void ReleaseAllDatasets(string ns)
        {
            lock (_lock) { if (_allGates.TryGetValue(ns, out var gate)) gate.TrySetResult(); }
        }

        public int GetPodsCallCount(string ns)
        {
            lock (_lock) { return _podsCallCounts.GetValueOrDefault(ns); }
        }

        private Task WaitForGatesAsync(string ns, CancellationToken ct)
        {
            Task? podGateTask;
            Task? allGateTask;
            lock (_lock)
            {
                podGateTask = _podGates.TryGetValue(ns, out var podGate) ? podGate.Task : null;
                allGateTask = _allGates.TryGetValue(ns, out var allGate) ? allGate.Task : null;
            }

            var gates = new[] { podGateTask, allGateTask }.Where(t => t is not null).Select(t => t!).ToArray();
            return gates.Length == 0 ? Task.CompletedTask : Task.WhenAll(gates).WaitAsync(ct);
        }

        public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
        {
            await WaitForNamespaceGateAsync(ns, ct);
            return _deploymentsByNamespace.TryGetValue(ns, out var d) ? d.ToList() : [];
        }

        private Task WaitForNamespaceGateAsync(string ns, CancellationToken ct)
        {
            Task? allGateTask;
            lock (_lock) { allGateTask = _allGates.TryGetValue(ns, out var gate) ? gate.Task : null; }
            return allGateTask ?? Task.CompletedTask;
        }

        public async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
        {
            lock (_lock) { _podsCallCounts[ns] = _podsCallCounts.GetValueOrDefault(ns) + 1; }
            await WaitForGatesAsync(ns, ct);
            return _podsByNamespace.TryGetValue(ns, out var p) ? p.ToList() : [];
        }

        public Task<IReadOnlyList<KubernetesEvent>> GetEventsAsync(string ns, string? involvedObjectName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubernetesEvent>>([]);
        public async IAsyncEnumerable<string> StreamPodLogsAsync(string ns, string podName, string container, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
        public Task<PortForwardSession> StartPortForwardAsync(string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default) => Task.FromResult(new PortForwardSession { Namespace = ns, ResourceName = resourceName, LocalPort = localPort, RemotePort = remotePort, Status = PortForwardStatus.Active });
        public Task StopPortForwardAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default) => Task.CompletedTask;
        public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default) => Task.FromResult(new IngressAnalysis { Namespace = ns, IngressName = ingressName, Summary = string.Empty });
        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default) => Task.FromResult(new NetworkPolicyAnalysis { Namespace = ns, WorkloadKind = workloadKind, WorkloadName = workloadName, Summary = string.Empty });
        public async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["ns-a", "ns-b"]);
        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KubeContextInfo>>([new KubeContextInfo { Name = "test-context", IsCurrent = true }]);
        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task RestartDeploymentAsync(string ns, string deploymentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePodAsync(string ns, string podName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>([]);
        public Task<string> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default) => Task.CompletedTask;
        public async Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default) => Task.CompletedTask;
        public async IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName, LogStreamOptions opts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
        public async Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default) => Task.CompletedTask;
        public async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(string ns, string podName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContainerDetail>>([]);
        public async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GatewayClassInfo>>([]);
        public async Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default) { await WaitForNamespaceGateAsync(ns, ct); return []; }
        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class FakeAksClientBootstrapper : IAksClientBootstrapper
    {
        public async Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
        {
            var client = request.ClientOverride ?? throw new InvalidOperationException("Test requires ClientOverride.");
            var namespaces = await client.GetNamespacesAsync(ct);
            var activeContext = request.RequestedContext ?? request.Config?.KubeconfigContext ?? "test-context";
            var currentNamespace = request.RequestedNamespace ?? request.Config?.DefaultNamespace ?? namespaces.FirstOrDefault() ?? "default";

            return new AksClientBootstrapResult(
                AksClientBootstrapStatus.Connected,
                client,
                [new KubeContextInfo { Name = activeContext, IsCurrent = true }],
                namespaces,
                activeContext,
                currentNamespace,
                null);
        }
    }

    private sealed class FakeSelectionContext : ISelectionContext
    {
        public event Action? SelectionChanged;
        public void SetSelection(string area, object? selected) => SelectionChanged?.Invoke();
        public T? GetSelection<T>(string area) where T : class => null;
    }

    private sealed class FakePortForwardSessionService : IPortForwardSessionService
    {
        public IReadOnlyList<PortForwardSession> Sessions => [];
        public event Action? SessionsChanged { add { } remove { } }
        public Task<PortForwardSession> StartAsync(IAksClient client, string ns, string resourceName, int localPort, int remotePort, CancellationToken ct = default)
            => Task.FromResult(new PortForwardSession { Namespace = ns, ResourceName = resourceName, LocalPort = localPort, RemotePort = remotePort, Status = PortForwardStatus.Active });
        public Task StopAsync(PortForwardSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePodHealthMonitorService : IPodHealthMonitorService
    {
        public bool IsMonitoring => false;
        public IReadOnlyList<string> MonitoredNamespaces => [];
        public IReadOnlyList<PodHealthEvent> RecentEvents => [];
        public event Action<PodHealthEvent>? PodHealthDetected { add { } remove { } }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task AddNamespaceAsync(string ns) => Task.CompletedTask;
        public Task RemoveNamespaceAsync(string ns) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
