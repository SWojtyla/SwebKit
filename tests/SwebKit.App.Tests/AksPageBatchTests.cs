using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class AksPageBatchTests : TestContext
{
    private readonly AppStateService _appState;
    private readonly CaptureNotificationService _notifications;

    public AksPageBatchTests()
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
            DefaultNamespace = "*",
            KubeconfigContext = "test-context"
        };

        _notifications = new CaptureNotificationService();

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton<INotificationService>(_notifications);
        Services.AddSingleton<IPortForwardSessionService>(new FakePortForwardSessionService());
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton(new PageDataCache());
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IPodHealthMonitorService>(new FakePodHealthMonitorService());
        Services.AddSingleton<IAksClientBootstrapper, AksClientBootstrapper>();
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void AksPage_JobsTab_BrowsesJobsFromOverrideClient()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("inventory-sync-001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("settlement-rollup-manual", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_CronJobsTab_BrowsesCronJobsInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "CronJobs");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("inventory-sync", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("settlement-rollup", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_JobsTab_AllNamespacesModeWithMoreThanThreeNamespaces_IncludesDefaultNamespace()
    {
        var client = new TrackingAksClient(
            namespaces: ["default", "orders", "payments", "support"],
            includeDefaultNamespaceBatchData: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("platform-reconcile-001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("default", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_CronJobsTab_AllNamespacesModeWithMoreThanThreeNamespaces_IncludesDefaultNamespace()
    {
        var client = new TrackingAksClient(
            namespaces: ["default", "orders", "payments", "support"],
            includeDefaultNamespaceBatchData: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "CronJobs");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("platform-reconcile", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("default", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_GatewaysTab_BrowsesGatewayApiResourcesInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Gateways");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("orders-edge", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments-edge", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("envoy-gateway", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_GatewayClassesTab_BrowsesClusterScopedGatewayApiClasses()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "GatewayClasses");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("envoy-gateway", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("envoy-internal", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("gateway.envoyproxy.io/gatewayclass-controller", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Default", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_HttpRoutesTab_BrowsesGatewayApiRoutesInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "HTTPRoutes");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("orders-api-route", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments-api-route", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("order-api:80", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payment-gateway:80", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_HttpRouteYaml_UsesSelectedRowNamespaceInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "HTTPRoutes");
        OpenHttpRouteMenu(cut, client.FindHttpRoute("payments", "payments-api-route"));
        ClickContextMenuButton(cut, "View YAML");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.YamlRequests,
                request => request.Namespace == "payments"
                    && request.Kind == "HTTPRoute"
                    && request.Name == "payments-api-route"));
    }

    [Fact]
    public void AksPage_GatewayClassYaml_UsesClusterScopedResourceKind()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "GatewayClasses");
        OpenGatewayClassMenu(cut, client.FindGatewayClass("envoy-gateway"));
        ClickContextMenuButton(cut, "View YAML");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.YamlRequests,
                request => string.IsNullOrEmpty(request.Namespace)
                    && request.Kind == "GatewayClass"
                    && request.Name == "envoy-gateway"));
    }

    [Fact]
    public void AksPage_JobYaml_UsesSelectedRowNamespaceInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");
        OpenJobMenu(cut, client.FindJob("payments", "settlement-rollup-manual"));
        ClickContextMenuButton(cut, "View YAML");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.YamlRequests,
                request => request.Namespace == "payments"
                    && request.Kind == "Job"
                    && request.Name == "settlement-rollup-manual"));
    }

    [Fact]
    public void AksPage_CronJobRunNow_KeepsCronJobsTab_RefreshesJobs_AndShowsCreatedName()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "CronJobs");
        OpenCronJobMenu(cut, client.FindCronJob("payments", "settlement-rollup"));
        ClickContextMenuButton(cut, "Run now");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.TriggerCronJobCalls,
                call => call.Namespace == "payments" && call.Name == "settlement-rollup"));

        cut.WaitForAssertion(() =>
            Assert.Contains(_notifications.All,
                notification => notification.Message == "CronJob triggered"
                    && notification.Detail == "settlement-rollup-manual-001"));

        cut.WaitForAssertion(() => Assert.Equal("CronJobs", ActiveResourceTab(cut)));

        OpenResourceTab(cut, "Jobs");
        cut.WaitForAssertion(() =>
            Assert.Contains("settlement-rollup-manual-001", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void AksPage_JobRerun_KeepsJobsTab_RefreshesJobs_AndShowsCreatedName()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");
        OpenJobMenu(cut, client.FindJob("orders", "inventory-sync-001"));
        ClickContextMenuButton(cut, "Rerun job");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.RerunJobCalls,
                call => call.Namespace == "orders" && call.Name == "inventory-sync-001"));

        cut.WaitForAssertion(() =>
            Assert.Contains(_notifications.All,
                notification => notification.Message == "Job rerun started"
                    && notification.Detail == "inventory-sync-001-rerun-001"));

        cut.WaitForAssertion(() => Assert.Equal("Jobs", ActiveResourceTab(cut)));
        cut.WaitForAssertion(() =>
            Assert.Contains("inventory-sync-001-rerun-001", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void AksPage_CronJobRunNow_WhenCancelled_DoesNotShowFailureNotification()
    {
        var client = new TrackingAksClient(cancelCronJobTrigger: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "CronJobs");
        OpenCronJobMenu(cut, client.FindCronJob("payments", "settlement-rollup"));

        ClickContextMenuButton(cut, "Run now");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.TriggerCronJobCalls,
                call => call.Namespace == "payments" && call.Name == "settlement-rollup"));

        Assert.Empty(_notifications.All);
    }

    [Fact]
    public void AksPage_JobRerun_WhenCancelled_DoesNotShowFailureNotification()
    {
        var client = new TrackingAksClient(cancelJobRerun: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");
        OpenJobMenu(cut, client.FindJob("orders", "inventory-sync-001"));

        ClickContextMenuButton(cut, "Rerun job");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.RerunJobCalls,
                call => call.Namespace == "orders" && call.Name == "inventory-sync-001"));

        Assert.Empty(_notifications.All);
    }

    private IRenderedComponent<AksPage> RenderAksPage(TrackingAksClient client)
        => RenderComponent<AksPage>(parameters => parameters
            .Add(page => page.ClientOverride, client));

    private static void OpenResourceTab(IRenderedComponent<AksPage> cut, string tabText)
    {
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("button.aks-resource-tab"),
                button => string.Equals(button.TextContent.Trim(), tabText, StringComparison.Ordinal)));

        cut.FindAll("button.aks-resource-tab")
            .Single(button => string.Equals(button.TextContent.Trim(), tabText, StringComparison.Ordinal))
            .Click();
    }

    private static string ActiveResourceTab(IRenderedComponent<AksPage> cut)
        => cut.FindAll("button.aks-resource-tab")
            .Single(button => button.ClassList.Contains("active"))
            .TextContent.Trim();

    private static void OpenJobMenu(IRenderedComponent<AksPage> cut, JobInfo job)
    {
        InvokePrivateMenuHelper(cut, "ShowJobMenu", job);
    }

    private static void OpenCronJobMenu(IRenderedComponent<AksPage> cut, CronJobInfo cronJob)
    {
        InvokePrivateMenuHelper(cut, "ShowCronJobMenu", cronJob);
    }

    private static void OpenHttpRouteMenu(IRenderedComponent<AksPage> cut, HttpRouteInfo httpRoute)
    {
        InvokePrivateMenuHelper(cut, "ShowHttpRouteMenu", httpRoute);
    }

    private static void OpenGatewayClassMenu(IRenderedComponent<AksPage> cut, GatewayClassInfo gatewayClass)
    {
        InvokePrivateMenuHelper(cut, "ShowGatewayClassMenu", gatewayClass);
    }

    private static void InvokePrivateMenuHelper<TItem>(IRenderedComponent<AksPage> cut, string methodName, TItem item)
    {
        var method = typeof(AksPage).GetMethod(methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        cut.InvokeAsync(() =>
        {
            method!.Invoke(cut.Instance, [new MouseEventArgs { ClientX = 10, ClientY = 10 }, item]);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static void ClickContextMenuButton(IRenderedComponent<AksPage> cut, string buttonText)
    {
        var normalizedTarget = NormalizeText(buttonText);

        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("button.ctx-item"),
                button => NormalizeText(button.TextContent).Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase)));

        cut.FindAll("button.ctx-item")
            .First(button => NormalizeText(button.TextContent).Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            .Click();
    }

    private static string NormalizeText(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class TrackingAksClient : IAksClient
    {
        private readonly Dictionary<string, List<JobInfo>> _baseJobsByNamespace;
        private readonly Dictionary<string, List<JobInfo>> _createdJobsByNamespace;
        private readonly Dictionary<string, List<CronJobInfo>> _cronJobsByNamespace;
        private readonly List<GatewayClassInfo> _gatewayClasses;
        private readonly Dictionary<string, List<GatewayInfo>> _gatewaysByNamespace;
        private readonly Dictionary<string, List<HttpRouteInfo>> _httpRoutesByNamespace;
        private readonly IReadOnlyList<string> _namespaces;
        private readonly bool _cancelCronJobTrigger;
        private readonly bool _cancelJobRerun;

        public TrackingAksClient(
            IReadOnlyList<string>? namespaces = null,
            bool includeDefaultNamespaceBatchData = false,
            bool cancelCronJobTrigger = false,
            bool cancelJobRerun = false)
        {
            _namespaces = namespaces?.ToList() ?? ["orders", "payments"];
            _cancelCronJobTrigger = cancelCronJobTrigger;
            _cancelJobRerun = cancelJobRerun;

            _baseJobsByNamespace = new Dictionary<string, List<JobInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new JobInfo
                    {
                        Name = "inventory-sync-001",
                        Namespace = "orders",
                        Status = "Succeeded",
                        Succeeded = 1,
                        DesiredCompletions = 1,
                        StartTime = DateTimeOffset.UtcNow.AddMinutes(-18),
                        CompletionTime = DateTimeOffset.UtcNow.AddMinutes(-16),
                        SourceKind = "CronJob",
                        SourceName = "inventory-sync"
                    }
                ],
                ["payments"] =
                [
                    new JobInfo
                    {
                        Name = "settlement-rollup-manual",
                        Namespace = "payments",
                        Status = "Active",
                        Active = 1,
                        DesiredCompletions = 1,
                        StartTime = DateTimeOffset.UtcNow.AddMinutes(-4),
                        SourceKind = "CronJob",
                        SourceName = "settlement-rollup"
                    }
                ]
            };

            _cronJobsByNamespace = new Dictionary<string, List<CronJobInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new CronJobInfo
                    {
                        Name = "inventory-sync",
                        Namespace = "orders",
                        Schedule = "*/15 * * * *",
                        LastScheduleTime = DateTimeOffset.UtcNow.AddMinutes(-15),
                        LastSuccessfulTime = DateTimeOffset.UtcNow.AddMinutes(-15)
                    }
                ],
                ["payments"] =
                [
                    new CronJobInfo
                    {
                        Name = "settlement-rollup",
                        Namespace = "payments",
                        Schedule = "0 */2 * * *",
                        LastScheduleTime = DateTimeOffset.UtcNow.AddMinutes(-30),
                        LastSuccessfulTime = DateTimeOffset.UtcNow.AddMinutes(-30)
                    }
                ]
            };

            _gatewayClasses =
            [
                new GatewayClassInfo
                {
                    Name = "envoy-gateway",
                    ControllerName = "gateway.envoyproxy.io/gatewayclass-controller",
                    Status = "Accepted",
                    Description = "Default Envoy Gateway class for internet-facing traffic.",
                    ParametersReference = "gateway.envoyproxy.io/EnvoyProxy infrastructure/envoy-gateway-config",
                    IsDefault = true
                },
                new GatewayClassInfo
                {
                    Name = "envoy-internal",
                    ControllerName = "gateway.envoyproxy.io/gatewayclass-controller",
                    Status = "Accepted",
                    Description = "Internal Envoy Gateway class for private workloads.",
                    ParametersReference = "gateway.envoyproxy.io/EnvoyProxy infrastructure/envoy-internal-config"
                }
            ];

            _gatewaysByNamespace = new Dictionary<string, List<GatewayInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new GatewayInfo
                    {
                        Name = "orders-edge",
                        Namespace = "orders",
                        GatewayClassName = "envoy-gateway",
                        Status = "Programmed",
                        AttachedRoutes = 1,
                        Addresses = ["20.10.0.10"],
                        Listeners =
                        [
                            new GatewayListenerInfo
                            {
                                Name = "https",
                                Port = 443,
                                Protocol = "HTTPS",
                                Hostname = "orders.example.com",
                                AttachedRoutes = 1
                            }
                        ]
                    }
                ],
                ["payments"] =
                [
                    new GatewayInfo
                    {
                        Name = "payments-edge",
                        Namespace = "payments",
                        GatewayClassName = "envoy-gateway",
                        Status = "Accepted",
                        AttachedRoutes = 1,
                        Addresses = ["20.10.0.11"],
                        Listeners =
                        [
                            new GatewayListenerInfo
                            {
                                Name = "https",
                                Port = 443,
                                Protocol = "HTTPS",
                                Hostname = "payments.example.com",
                                AttachedRoutes = 1
                            }
                        ]
                    }
                ]
            };

            _httpRoutesByNamespace = new Dictionary<string, List<HttpRouteInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new HttpRouteInfo
                    {
                        Name = "orders-api-route",
                        Namespace = "orders",
                        Status = "Accepted",
                        Hostnames = ["orders.example.com"],
                        ParentRefs = ["orders-edge#https"],
                        BackendRefs = ["order-api:80"]
                    }
                ],
                ["payments"] =
                [
                    new HttpRouteInfo
                    {
                        Name = "payments-api-route",
                        Namespace = "payments",
                        Status = "Accepted",
                        Hostnames = ["payments.example.com"],
                        ParentRefs = ["payments-edge#https"],
                        BackendRefs = ["payment-gateway:80"]
                    }
                ]
            };

            if (includeDefaultNamespaceBatchData)
            {
                _baseJobsByNamespace["default"] =
                [
                    new JobInfo
                    {
                        Name = "platform-reconcile-001",
                        Namespace = "default",
                        Status = "Succeeded",
                        Succeeded = 1,
                        DesiredCompletions = 1,
                        StartTime = DateTimeOffset.UtcNow.AddMinutes(-26),
                        CompletionTime = DateTimeOffset.UtcNow.AddMinutes(-24),
                        SourceKind = "CronJob",
                        SourceName = "platform-reconcile"
                    }
                ];

                _cronJobsByNamespace["default"] =
                [
                    new CronJobInfo
                    {
                        Name = "platform-reconcile",
                        Namespace = "default",
                        Schedule = "*/10 * * * *",
                        LastScheduleTime = DateTimeOffset.UtcNow.AddMinutes(-10),
                        LastSuccessfulTime = DateTimeOffset.UtcNow.AddMinutes(-10)
                    }
                ];

                _gatewaysByNamespace["default"] =
                [
                    new GatewayInfo
                    {
                        Name = "default-edge",
                        Namespace = "default",
                        GatewayClassName = "envoy-gateway",
                        Status = "Programmed",
                        AttachedRoutes = 1,
                        Addresses = ["20.10.0.12"],
                        Listeners =
                        [
                            new GatewayListenerInfo
                            {
                                Name = "https",
                                Port = 443,
                                Protocol = "HTTPS",
                                Hostname = "default.example.com",
                                AttachedRoutes = 1
                            }
                        ]
                    }
                ];

                _httpRoutesByNamespace["default"] =
                [
                    new HttpRouteInfo
                    {
                        Name = "platform-route",
                        Namespace = "default",
                        Status = "Accepted",
                        Hostnames = ["default.example.com"],
                        ParentRefs = ["default-edge#https"],
                        BackendRefs = ["platform-api:80"]
                    }
                ];
            }

            foreach (var ns in _namespaces)
            {
                _baseJobsByNamespace.TryAdd(ns, []);
                _cronJobsByNamespace.TryAdd(ns, []);
                _gatewaysByNamespace.TryAdd(ns, []);
                _httpRoutesByNamespace.TryAdd(ns, []);
            }

            _createdJobsByNamespace = _baseJobsByNamespace.Keys
                .Union(_cronJobsByNamespace.Keys, StringComparer.Ordinal)
                .ToDictionary(ns => ns, _ => new List<JobInfo>(), StringComparer.Ordinal);
        }

        public List<(string Namespace, string Kind, string Name)> YamlRequests { get; } = [];
        public List<(string Namespace, string Name)> TriggerCronJobCalls { get; } = [];
        public List<(string Namespace, string Name)> RerunJobCalls { get; } = [];

        public JobInfo FindJob(string ns, string name)
            => _baseJobsByNamespace[ns]
                .Concat(_createdJobsByNamespace[ns])
                .Single(job => job.Name == name);

        public CronJobInfo FindCronJob(string ns, string name)
            => _cronJobsByNamespace[ns].Single(job => job.Name == name);

        public GatewayClassInfo FindGatewayClass(string name)
            => _gatewayClasses.Single(gatewayClass => gatewayClass.Name == name);

        public HttpRouteInfo FindHttpRoute(string ns, string name)
            => _httpRoutesByNamespace[ns].Single(route => route.Name == name);

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

        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayClassInfo>>(_gatewayClasses.ToList());

        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayInfo>>(_gatewaysByNamespace[ns].ToList());

        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HttpRouteInfo>>(_httpRoutesByNamespace[ns].ToList());

        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>([]);

        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_namespaces.ToList());

        public Task<IReadOnlyList<KubeContextInfo>> GetContextsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KubeContextInfo>>([
                new KubeContextInfo
                {
                    Name = "test-context",
                    Cluster = "cluster",
                    User = "tester",
                    Namespace = "orders",
                    IsCurrent = true
                }
            ]);

        public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
        {
            YamlRequests.Add((ns, kind, name));
            return Task.FromResult($"apiVersion: batch/v1\nkind: {kind}\nmetadata:\n  name: {name}\n  namespace: {ns}\n");
        }

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
            => Task.FromResult<IReadOnlyList<CronJobInfo>>(_cronJobsByNamespace[ns].ToList());

        public Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobInfo>>(_baseJobsByNamespace[ns]
                .Concat(_createdJobsByNamespace[ns])
                .OrderBy(job => job.Namespace, StringComparer.Ordinal)
                .ThenBy(job => job.Name, StringComparer.Ordinal)
                .ToList());

        public Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
        {
            TriggerCronJobCalls.Add((ns, cronJobName));

            if (_cancelCronJobTrigger)
                throw new OperationCanceledException();

            var createdJobName = $"{cronJobName}-manual-001";
            _createdJobsByNamespace[ns].Add(new JobInfo
            {
                Name = createdJobName,
                Namespace = ns,
                Status = "Active",
                Active = 1,
                DesiredCompletions = 1,
                StartTime = DateTimeOffset.UtcNow,
                SourceKind = "CronJob",
                SourceName = cronJobName
            });

            return Task.FromResult(createdJobName);
        }

        public Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
        {
            RerunJobCalls.Add((ns, jobName));

            if (_cancelJobRerun)
                throw new OperationCanceledException();

            var createdJobName = $"{jobName}-rerun-001";
            _createdJobsByNamespace[ns].Add(new JobInfo
            {
                Name = createdJobName,
                Namespace = ns,
                Status = "Active",
                Active = 1,
                DesiredCompletions = 1,
                StartTime = DateTimeOffset.UtcNow,
                SourceKind = "Job",
                SourceName = jobName
            });

            return Task.FromResult(createdJobName);
        }

        private static async IAsyncEnumerable<string> EmptyLogLines()
        {
            yield break;
        }

        private static async IAsyncEnumerable<AggregatedLogLine> EmptyAggregatedLogLines()
        {
            yield break;
        }
    }

    private sealed class CaptureNotificationService : INotificationService
    {
        private readonly List<Notification> _notifications = [];

        public IReadOnlyList<Notification> All => _notifications;

        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null)
            => Add(NotificationSeverity.Success, message, detail);

        public void ShowWarning(string message, string? detail = null)
            => Add(NotificationSeverity.Warning, message, detail);

        public void ShowError(string message, string? detail = null, Exception? ex = null)
            => Add(NotificationSeverity.Error, message, detail ?? ex?.Message);

        public void ShowInfo(string message, string? detail = null)
            => Add(NotificationSeverity.Info, message, detail);

        public void Dismiss(Guid id)
            => _notifications.RemoveAll(notification => notification.Id == id);

        public void ClearAll()
            => _notifications.Clear();

        private void Add(NotificationSeverity severity, string message, string? detail)
        {
            _notifications.Add(new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow));
            NotificationsChanged?.Invoke();
        }
    }

    private sealed class FakeSelectionContext : ISelectionContext
    {
        private readonly Dictionary<string, object?> _selections = new(StringComparer.Ordinal);

        public event Action? SelectionChanged;

        public void SetSelection(string area, object? selected)
        {
            _selections[area] = selected;
            SelectionChanged?.Invoke();
        }

        public T? GetSelection<T>(string area) where T : class
            => _selections.TryGetValue(area, out var value) ? value as T : null;
    }

    private sealed class FakePortForwardSessionService : IPortForwardSessionService
    {
        public IReadOnlyList<PortForwardSession> Sessions => [];

        public event Action? SessionsChanged
        {
            add { }
            remove { }
        }

        public Task<PortForwardSession> StartAsync(IAksClient client, string ns, string resourceName, int localPort,
            int remotePort, CancellationToken ct = default)
            => Task.FromResult(new PortForwardSession
            {
                Namespace = ns,
                ResourceName = resourceName,
                LocalPort = localPort,
                RemotePort = remotePort,
                Status = PortForwardStatus.Active
            });

        public Task StopAsync(PortForwardSession session, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopAllAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakePodHealthMonitorService : IPodHealthMonitorService
    {
        public bool IsMonitoring => false;
        public IReadOnlyList<string> MonitoredNamespaces => [];
        public IReadOnlyList<PodHealthEvent> RecentEvents => [];

        public event Action<PodHealthEvent>? PodHealthDetected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public Task AddNamespaceAsync(string ns) => Task.CompletedTask;

        public Task RemoveNamespaceAsync(string ns) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}