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

[Collection("AppDataSerial")]
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
        var userSettings = new UserSettingsRepository();

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton(userSettings);
        Services.AddSingleton<INotificationService>(_notifications);
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
    public void AksPage_HelmRollback_ShowsProgressAndSuccessNotification()
    {
        _appState.Config.AksConfig!.DefaultNamespace = "orders";

        var client = new TrackingAksClient(namespaces: ["orders"], deferHelmRollback: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Helm");
        OpenHelmMenu(cut, client.FindHelmRelease("orders", "orders-api"));
        ClickContextMenuButton(cut, "Rollback...");

        cut.WaitForAssertion(() =>
            Assert.Contains("Select a revision to rollback to:", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button.aks-rollback-btn.destructive")
            .Single(button => NormalizeText(button.TextContent).Contains("Rollback", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("button.confirm-btn.confirm-yes")));
        cut.Find("button.confirm-btn.confirm-yes").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.RollbackHelmCalls,
                call => call.Namespace == "orders"
                    && call.ReleaseName == "orders-api"
                    && call.Revision == 2);
            Assert.Contains("Rolling back to revision 2...", cut.Markup, StringComparison.Ordinal);
        });

        client.CompleteDeferredHelmRollback();

        cut.WaitForAssertion(() =>
            Assert.Contains(_notifications.All,
                notification => notification.Severity == NotificationSeverity.Success
                    && notification.Message == "Helm rollback complete"
                    && notification.Detail == "orders-api → revision 2"));
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
    public void AksPage_JobsTab_MultiNamespaceSelection_LoadsOnlySelectedNamespaces()
    {
        _appState.Config.AksConfig!.DefaultNamespace = "orders,payments";
        var client = new TrackingAksClient(
            namespaces: ["default", "orders", "payments"],
            includeDefaultNamespaceBatchData: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Jobs");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("inventory-sync-001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("settlement-rollup-manual", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("platform-reconcile-001", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AksPage_IngressesTab_MultiNamespaceSelection_LoadsOnlySelectedNamespaces()
    {
        _appState.Config.AksConfig!.DefaultNamespace = "orders,payments";
        var client = new TrackingAksClient(
            namespaces: ["default", "orders", "payments"],
            includeDefaultNamespaceBatchData: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Ingresses");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("orders-public", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments-public", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("platform-public", cut.Markup, StringComparison.Ordinal);
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
    public void AksPage_ServicesTab_BrowsesServicesInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Services");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("order-api", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payment-gateway", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("20.10.0.21", cut.Markup, StringComparison.Ordinal);
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
    public void AksPage_HttpRoutesTab_RendersAllRoutesWhenThreeArePresent()
    {
        var client = new TrackingAksClient(
            namespaces: ["default", "orders", "payments"],
            includeDefaultNamespaceBatchData: true);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "HTTPRoutes");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("platform-route", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders-api-route", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments-api-route", cut.Markup, StringComparison.Ordinal);
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
    public void AksPage_ServiceYaml_UsesSelectedRowNamespaceInAllNamespacesMode()
    {
        var client = new TrackingAksClient();
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Services");
        OpenServiceMenu(cut, client.FindService("payments", "payment-gateway"));
        ClickContextMenuButton(cut, "View YAML");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.YamlRequests,
                request => request.Namespace == "payments"
                    && request.Kind == "Service"
                    && request.Name == "payment-gateway"));
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

    [Fact]
    public void AksPage_DeploymentInspectButton_OpensNetworkAnalysisPanel()
    {
        var client = new TrackingAksClient(namespaces: ["orders"]);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Deployments");

        cut.WaitForAssertion(() => Assert.Contains("order-api", cut.Markup, StringComparison.Ordinal));

        // DeploymentGrid/StatefulSetGrid/PodGrid/IngressGrid all share the "aks-analysis-btn" class and
        // are kept mounted simultaneously (BL-4 perf decision — see docs/pitfalls/blazor-maui.md):
        // only the active tab's wrapper div lacks the `hidden` attribute, so scope the lookup to that.
        var visibleAnalysisButtons = cut.FindAll("button.aks-analysis-btn")
            .Where(button => button.Closest("[hidden]") is null)
            .ToList();
        Assert.Single(visibleAnalysisButtons);
        visibleAnalysisButtons[0].Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(client.NetworkPolicyAnalysisCalls,
                call => call.Namespace == "orders"
                    && call.WorkloadKind == "Deployment"
                    && call.WorkloadName == "order-api"));

        cut.WaitForAssertion(() =>
            Assert.Contains("Network Policies: order-api", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void AksPage_PodAnalyzeNetwork_ContextMenu_UsesSelectedRowNamespace()
    {
        var client = new TrackingAksClient(namespaces: ["orders"]);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Pods");
        OpenPodMenu(cut, client.FindPod("orders", "order-api-7b4d9-xk2m1"));
        ClickContextMenuButton(cut, "Analyze network");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.NetworkPolicyAnalysisCalls,
                call => call.Namespace == "orders"
                    && call.WorkloadKind == "Pod"
                    && call.WorkloadName == "order-api-7b4d9-xk2m1"));

        cut.WaitForAssertion(() =>
            Assert.Contains("Network Policies: order-api-7b4d9-xk2m1", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void AksPage_IngressInspect_KeyboardShortcut_OpensIngressAnalysisPanel()
    {
        var client = new TrackingAksClient(namespaces: ["orders"]);
        var cut = RenderAksPage(client);

        OpenResourceTab(cut, "Ingresses");
        InvokePrivateMethod(cut, "SelectIngress", client.FindIngress("orders", "orders-public"));
        InvokePrivateMethod(cut, "HandleLetterActionAsync", "i");

        cut.WaitForAssertion(() =>
            Assert.Contains(client.IngressAnalysisCalls,
                call => call.Namespace == "orders"
                    && call.IngressName == "orders-public"));

        cut.WaitForAssertion(() =>
            Assert.Contains("Ingress Analysis: orders-public", cut.Markup, StringComparison.Ordinal));
    }

    private IRenderedComponent<AksPage> RenderAksPage(TrackingAksClient client)
        => RenderComponent<AksPage>(parameters => parameters
            .Add(page => page.ClientOverride, client));

    private static void OpenResourceTab(IRenderedComponent<AksPage> cut, string tabText)
    {
        var directButtons = cut.FindAll("button.aks-resource-tab")
            .Where(button => string.Equals(button.TextContent.Trim(), tabText, StringComparison.Ordinal))
            .ToList();

        if (directButtons.Count == 1)
        {
            directButtons[0].Click();
            return;
        }

        cut.FindAll("button.aks-resource-tab--toggle")
            .Single(button => NormalizeText(button.TextContent).Contains("Network", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("button.aks-resource-subtab"),
                button => string.Equals(button.TextContent.Trim(), tabText, StringComparison.Ordinal)));

        cut.FindAll("button.aks-resource-subtab")
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

    private static void OpenServiceMenu(IRenderedComponent<AksPage> cut, ServiceInfo service)
    {
        InvokePrivateMenuHelper(cut, "ShowServiceMenu", service);
    }

    private static void OpenPodMenu(IRenderedComponent<AksPage> cut, PodInfo pod)
    {
        InvokePrivateMenuHelper(cut, "ShowPodMenu", pod);
    }

    private static void OpenCronJobMenu(IRenderedComponent<AksPage> cut, CronJobInfo cronJob)
    {
        InvokePrivateMenuHelper(cut, "ShowCronJobMenu", cronJob);
    }

    private static void OpenHelmMenu(IRenderedComponent<AksPage> cut, HelmReleaseInfo helmRelease)
    {
        InvokePrivateMenuHelper(cut, "ShowHelmMenu", helmRelease);
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

    private static void InvokePrivateMethod(IRenderedComponent<AksPage> cut, string methodName, params object?[] args)
    {
        var method = typeof(AksPage).GetMethod(methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        cut.InvokeAsync(async () =>
        {
            var result = method!.Invoke(cut.Instance, args);
            if (result is Task task)
            {
                await task;
            }
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
        private readonly Dictionary<string, List<DeploymentInfo>> _deploymentsByNamespace;
        private readonly Dictionary<string, List<StatefulSetInfo>> _statefulSetsByNamespace;
        private readonly Dictionary<string, List<PodInfo>> _podsByNamespace;
        private readonly Dictionary<string, List<JobInfo>> _baseJobsByNamespace;
        private readonly Dictionary<string, List<JobInfo>> _createdJobsByNamespace;
        private readonly Dictionary<string, List<CronJobInfo>> _cronJobsByNamespace;
        private readonly Dictionary<string, List<ServiceInfo>> _servicesByNamespace;
        private readonly Dictionary<string, List<IngressInfo>> _ingressesByNamespace;
        private readonly List<GatewayClassInfo> _gatewayClasses;
        private readonly Dictionary<string, List<GatewayInfo>> _gatewaysByNamespace;
        private readonly Dictionary<string, List<HttpRouteInfo>> _httpRoutesByNamespace;
        private readonly Dictionary<string, List<HelmReleaseInfo>> _helmReleasesByNamespace;
        private readonly Dictionary<(string Namespace, string ReleaseName), List<HelmRevisionInfo>> _helmHistoryByRelease;
        private readonly IReadOnlyList<string> _namespaces;
        private readonly bool _cancelCronJobTrigger;
        private readonly bool _cancelJobRerun;
        private readonly TaskCompletionSource _deferredHelmRollback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _deferHelmRollback;

        public TrackingAksClient(
            IReadOnlyList<string>? namespaces = null,
            bool includeDefaultNamespaceBatchData = false,
            bool cancelCronJobTrigger = false,
            bool cancelJobRerun = false,
            bool deferHelmRollback = false)
        {
            _namespaces = namespaces?.ToList() ?? ["orders", "payments"];
            _cancelCronJobTrigger = cancelCronJobTrigger;
            _cancelJobRerun = cancelJobRerun;
            _deferHelmRollback = deferHelmRollback;

            _deploymentsByNamespace = new Dictionary<string, List<DeploymentInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new DeploymentInfo
                    {
                        Name = "order-api",
                        Namespace = "orders",
                        Replicas = 2,
                        ReadyReplicas = 2,
                        Status = "Available",
                        Labels = new Dictionary<string, string> { ["app"] = "order-api", ["team"] = "commerce" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "order-api" }
                    }
                ],
                ["payments"] =
                [
                    new DeploymentInfo
                    {
                        Name = "payment-gateway",
                        Namespace = "payments",
                        Replicas = 1,
                        ReadyReplicas = 1,
                        Status = "Available",
                        Labels = new Dictionary<string, string> { ["app"] = "payment-gateway", ["team"] = "payments" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "payment-gateway" }
                    }
                ]
            };

            _statefulSetsByNamespace = new Dictionary<string, List<StatefulSetInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new StatefulSetInfo
                    {
                        Name = "order-cache",
                        Namespace = "orders",
                        Replicas = 1,
                        ReadyReplicas = 1,
                        CurrentRevision = "order-cache-77f7f9f5d9",
                        UpdateRevision = "order-cache-77f7f9f5d9",
                        Labels = new Dictionary<string, string> { ["app"] = "order-cache" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "order-cache" }
                    }
                ],
                ["payments"] =
                [
                    new StatefulSetInfo
                    {
                        Name = "settlement-store",
                        Namespace = "payments",
                        Replicas = 1,
                        ReadyReplicas = 1,
                        CurrentRevision = "settlement-store-6ccf5c98c6",
                        UpdateRevision = "settlement-store-6ccf5c98c6",
                        Labels = new Dictionary<string, string> { ["app"] = "settlement-store" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "settlement-store" }
                    }
                ]
            };

            _podsByNamespace = new Dictionary<string, List<PodInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new PodInfo
                    {
                        Name = "order-api-7b4d9-xk2m1",
                        Namespace = "orders",
                        Phase = "Running",
                        Status = "Running",
                        Ready = true,
                        ReadyContainers = 2,
                        TotalContainers = 2,
                        Containers = ["order-api", "istio-proxy"],
                        PodIP = "10.16.34.21",
                        Labels = new Dictionary<string, string> { ["app"] = "order-api", ["pod-template-hash"] = "7b4d9" }
                    }
                ],
                ["payments"] =
                [
                    new PodInfo
                    {
                        Name = "payment-gateway-6d7c9-p2m4r",
                        Namespace = "payments",
                        Phase = "Running",
                        Status = "Running",
                        Ready = true,
                        ReadyContainers = 2,
                        TotalContainers = 2,
                        Containers = ["payment-gateway", "istio-proxy"],
                        PodIP = "10.16.35.18",
                        Labels = new Dictionary<string, string> { ["app"] = "payment-gateway", ["pod-template-hash"] = "6d7c9" }
                    }
                ]
            };

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

            _servicesByNamespace = new Dictionary<string, List<ServiceInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new ServiceInfo
                    {
                        Name = "order-api",
                        Namespace = "orders",
                        Type = "ClusterIP",
                        ClusterIp = "10.0.12.10",
                        Ports =
                        [
                            new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "8080" }
                        ]
                    }
                ],
                ["payments"] =
                [
                    new ServiceInfo
                    {
                        Name = "payment-gateway",
                        Namespace = "payments",
                        Type = "LoadBalancer",
                        ClusterIp = "10.0.12.24",
                        ExternalAddresses = ["20.10.0.21"],
                        Ports =
                        [
                            new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "8080" }
                        ]
                    }
                ]
            };

            _helmReleasesByNamespace = new Dictionary<string, List<HelmReleaseInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new HelmReleaseInfo
                    {
                        Name = "orders-api",
                        Namespace = "orders",
                        Revision = 3,
                        Status = "deployed",
                        Chart = "orders-api-1.2.3",
                        AppVersion = "1.2.3",
                        Updated = DateTimeOffset.UtcNow.AddMinutes(-8)
                    }
                ],
                ["payments"] =
                [
                    new HelmReleaseInfo
                    {
                        Name = "payments-gateway",
                        Namespace = "payments",
                        Revision = 7,
                        Status = "deployed",
                        Chart = "payments-gateway-4.5.6",
                        AppVersion = "4.5.6",
                        Updated = DateTimeOffset.UtcNow.AddMinutes(-11)
                    }
                ]
            };

            _helmHistoryByRelease = new Dictionary<(string Namespace, string ReleaseName), List<HelmRevisionInfo>>()
            {
                [("orders", "orders-api")] =
                [
                    new HelmRevisionInfo
                    {
                        Revision = 3,
                        Status = "deployed",
                        Chart = "orders-api-1.2.3",
                        AppVersion = "1.2.3",
                        Updated = DateTimeOffset.UtcNow.AddMinutes(-8),
                        Description = "Upgrade complete"
                    },
                    new HelmRevisionInfo
                    {
                        Revision = 2,
                        Status = "superseded",
                        Chart = "orders-api-1.2.2",
                        AppVersion = "1.2.2",
                        Updated = DateTimeOffset.UtcNow.AddHours(-2),
                        Description = "Previous stable release"
                    }
                ],
                [("payments", "payments-gateway")] =
                [
                    new HelmRevisionInfo
                    {
                        Revision = 7,
                        Status = "deployed",
                        Chart = "payments-gateway-4.5.6",
                        AppVersion = "4.5.6",
                        Updated = DateTimeOffset.UtcNow.AddMinutes(-11),
                        Description = "Upgrade complete"
                    },
                    new HelmRevisionInfo
                    {
                        Revision = 6,
                        Status = "superseded",
                        Chart = "payments-gateway-4.5.5",
                        AppVersion = "4.5.5",
                        Updated = DateTimeOffset.UtcNow.AddHours(-3),
                        Description = "Previous stable release"
                    }
                ]
            };

            _ingressesByNamespace = new Dictionary<string, List<IngressInfo>>(StringComparer.Ordinal)
            {
                ["orders"] =
                [
                    new IngressInfo
                    {
                        Name = "orders-public",
                        Namespace = "orders",
                        IngressClass = "nginx",
                        Addresses = ["20.10.0.30"],
                        Rules =
                        [
                            new IngressRule
                            {
                                Host = "orders.example.com",
                                Paths =
                                [
                                    new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "order-api", ServicePort = 80 }
                                ]
                            }
                        ]
                    }
                ],
                ["payments"] =
                [
                    new IngressInfo
                    {
                        Name = "payments-public",
                        Namespace = "payments",
                        IngressClass = "nginx",
                        Addresses = ["20.10.0.31"],
                        Rules =
                        [
                            new IngressRule
                            {
                                Host = "payments.example.com",
                                Paths =
                                [
                                    new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "payment-gateway", ServicePort = 80 }
                                ]
                            }
                        ]
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

                _servicesByNamespace["default"] =
                [
                    new ServiceInfo
                    {
                        Name = "platform-api",
                        Namespace = "default",
                        Type = "ClusterIP",
                        ClusterIp = "10.0.12.12",
                        Ports =
                        [
                            new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "8080" }
                        ]
                    }
                ];

                _deploymentsByNamespace["default"] =
                [
                    new DeploymentInfo
                    {
                        Name = "platform-api",
                        Namespace = "default",
                        Replicas = 1,
                        ReadyReplicas = 1,
                        Status = "Available",
                        Labels = new Dictionary<string, string> { ["app"] = "platform-api" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "platform-api" }
                    }
                ];

                _statefulSetsByNamespace["default"] =
                [
                    new StatefulSetInfo
                    {
                        Name = "platform-store",
                        Namespace = "default",
                        Replicas = 1,
                        ReadyReplicas = 1,
                        CurrentRevision = "platform-store-75bc98f55c",
                        UpdateRevision = "platform-store-75bc98f55c",
                        Labels = new Dictionary<string, string> { ["app"] = "platform-store" },
                        SelectorLabels = new Dictionary<string, string> { ["app"] = "platform-store" }
                    }
                ];

                _podsByNamespace["default"] =
                [
                    new PodInfo
                    {
                        Name = "platform-api-5f84c-rw8lq",
                        Namespace = "default",
                        Phase = "Running",
                        Status = "Running",
                        Ready = true,
                        ReadyContainers = 2,
                        TotalContainers = 2,
                        Containers = ["platform-api", "istio-proxy"],
                        PodIP = "10.16.30.12",
                        Labels = new Dictionary<string, string> { ["app"] = "platform-api", ["pod-template-hash"] = "5f84c" }
                    }
                ];

                _ingressesByNamespace["default"] =
                [
                    new IngressInfo
                    {
                        Name = "platform-public",
                        Namespace = "default",
                        IngressClass = "nginx",
                        Addresses = ["20.10.0.12"],
                        Rules =
                        [
                            new IngressRule
                            {
                                Host = "default.example.com",
                                Paths =
                                [
                                    new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "platform-api", ServicePort = 80 }
                                ]
                            }
                        ]
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
                _deploymentsByNamespace.TryAdd(ns, []);
                _statefulSetsByNamespace.TryAdd(ns, []);
                _podsByNamespace.TryAdd(ns, []);
                _baseJobsByNamespace.TryAdd(ns, []);
                _cronJobsByNamespace.TryAdd(ns, []);
                _servicesByNamespace.TryAdd(ns, []);
                _ingressesByNamespace.TryAdd(ns, []);
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
        public List<(string Namespace, string ReleaseName, int Revision)> RollbackHelmCalls { get; } = [];
        public List<(string Namespace, string IngressName)> IngressAnalysisCalls { get; } = [];
        public List<(string Namespace, string WorkloadKind, string WorkloadName)> NetworkPolicyAnalysisCalls { get; } = [];

        public DeploymentInfo FindDeployment(string ns, string name)
            => _deploymentsByNamespace[ns].Single(deployment => deployment.Name == name);

        public PodInfo FindPod(string ns, string name)
            => _podsByNamespace[ns].Single(pod => pod.Name == name);

        public JobInfo FindJob(string ns, string name)
            => _baseJobsByNamespace[ns]
                .Concat(_createdJobsByNamespace[ns])
                .Single(job => job.Name == name);

        public CronJobInfo FindCronJob(string ns, string name)
            => _cronJobsByNamespace[ns].Single(job => job.Name == name);

        public HelmReleaseInfo FindHelmRelease(string ns, string name)
            => _helmReleasesByNamespace[ns].Single(release => release.Name == name);

        public ServiceInfo FindService(string ns, string name)
            => _servicesByNamespace[ns].Single(service => service.Name == name);

        public GatewayClassInfo FindGatewayClass(string name)
            => _gatewayClasses.Single(gatewayClass => gatewayClass.Name == name);

        public IngressInfo FindIngress(string ns, string name)
            => _ingressesByNamespace[ns].Single(ingress => ingress.Name == name);

        public HttpRouteInfo FindHttpRoute(string ns, string name)
            => _httpRoutesByNamespace[ns].Single(route => route.Name == name);

        public void CompleteDeferredHelmRollback()
            => _deferredHelmRollback.TrySetResult();

        public Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeploymentInfo>>(_deploymentsByNamespace[ns].ToList());

        public Task<IReadOnlyList<PodInfo>> GetPodsAsync(string ns, string? labelSelector = null, CancellationToken ct = default)
        {
            var pods = _podsByNamespace[ns].ToList();

            if (!string.IsNullOrWhiteSpace(labelSelector))
            {
                var parts = labelSelector.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    pods = pods
                        .Where(pod => pod.Labels.TryGetValue(parts[0], out var value)
                            && string.Equals(value, parts[1], StringComparison.Ordinal))
                        .ToList();
                }
            }

            return Task.FromResult<IReadOnlyList<PodInfo>>(pods);
        }

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

        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ServiceInfo>>(_servicesByNamespace[ns].ToList());

        public Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IngressInfo>>(_ingressesByNamespace[ns].ToList());

        public Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default)
        {
            IngressAnalysisCalls.Add((ns, ingressName));

            var ingress = FindIngress(ns, ingressName);
            var path = ingress.Rules.SelectMany(rule => rule.Paths.Select(routePath => (rule, routePath))).First();
            var service = _servicesByNamespace[ns].FirstOrDefault(candidate =>
                string.Equals(candidate.Name, path.routePath.ServiceName, StringComparison.Ordinal));
            var matchingPods = service is null
                ? []
                : _podsByNamespace[ns]
                    .Where(pod => MatchesSelector(pod.Labels, service.SelectorLabels))
                    .Select(pod => pod.Name)
                    .ToList();

            return Task.FromResult(new IngressAnalysis
            {
                Namespace = ns,
                IngressName = ingressName,
                IngressClass = ingress.IngressClass,
                Summary = $"{ingressName} routes Kubernetes ingress traffic to {service?.Name ?? "an unresolved Service"}.",
                Addresses = ingress.Addresses.ToList(),
                Findings =
                [
                    service is null
                        ? $"Service {path.routePath.ServiceName} was not found in namespace {ns}."
                        : $"Service {service.Name} exposes {matchingPods.Count} matching pod(s) through Kubernetes Service selectors."
                ],
                Backends =
                [
                    new IngressBackendAnalysis
                    {
                        Host = path.rule.Host ?? "*",
                        Path = path.routePath.Path,
                        PathType = path.routePath.PathType,
                        ServiceName = path.routePath.ServiceName,
                        ServiceNamespace = ns,
                        RequestedPort = path.routePath.ServicePort?.ToString() ?? "default",
                        ServiceExists = service is not null,
                        ServiceType = service?.Type,
                        ServicePortResolved = service?.Ports.Any(port => port.Port == path.routePath.ServicePort) == true,
                        ResolvedServicePort = service?.Ports.FirstOrDefault(port => port.Port == path.routePath.ServicePort) is { } port
                            ? $"{port.Port}/{port.Protocol} -> {port.TargetPort ?? port.Port.ToString()}"
                            : null,
                        HasSelector = service?.SelectorLabels.Count > 0,
                        MatchingPodCount = matchingPods.Count,
                        ReadyPodCount = matchingPods.Count,
                        MatchingPods = matchingPods,
                        Findings = service is null
                            ? [$"Ingress backend {path.routePath.ServiceName}:{path.routePath.ServicePort} does not resolve to a Service object."]
                            : [$"Service {service.Name} selects {matchingPods.Count} ready pod(s) in namespace {ns}."]
                    }
                ]
            });
        }

        public Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(string ns, string workloadKind, string workloadName, CancellationToken ct = default)
        {
            NetworkPolicyAnalysisCalls.Add((ns, workloadKind, workloadName));

            var selector = ResolveSelector(ns, workloadKind, workloadName);
            var matchingPods = ResolveMatchingPods(ns, workloadKind, workloadName, selector);
            var matchingServices = _servicesByNamespace[ns]
                .Where(service => service.SelectorLabels.Count > 0 && MatchesSelector(selector, service.SelectorLabels))
                .ToList();
            var exposedIngresses = _ingressesByNamespace[ns]
                .Where(ingress => ingress.Rules.SelectMany(rule => rule.Paths)
                    .Any(path => matchingServices.Any(service => string.Equals(service.Name, path.ServiceName, StringComparison.Ordinal))))
                .Select(ingress => ingress.Name)
                .ToList();
            var selectorLabel = selector.TryGetValue("app", out var appLabel) ? appLabel : workloadName;

            return Task.FromResult(new NetworkPolicyAnalysis
            {
                Namespace = ns,
                WorkloadKind = workloadKind,
                WorkloadName = workloadName,
                Summary = $"{workloadKind} {workloadName} is isolated on ingress and still reachable through the selected Kubernetes Services.",
                MatchingPodCount = matchingPods.Count,
                MatchingPods = matchingPods,
                SelectorLabels = selector,
                Services = matchingServices.Select(service => $"{service.Name} ({service.Type})").ToList(),
                ExposedByIngresses = exposedIngresses,
                IngressIsolated = true,
                EgressIsolated = false,
                Findings =
                [
                    $"Matched {matchingPods.Count} pod(s) for {workloadKind} {workloadName}.",
                    matchingServices.Count == 0
                        ? "No Services select the workload."
                        : $"{matchingServices.Count} Service object(s) select the workload."
                ],
                Policies =
                [
                    new NetworkPolicyMatch
                    {
                        Name = $"{selectorLabel}-allow-from-ingress",
                        PolicyTypes = ["Ingress"],
                        IngressRules = ["Allow traffic from ingress controller pods on TCP/80."]
                    },
                    new NetworkPolicyMatch
                    {
                        Name = $"{selectorLabel}-egress-dependencies",
                        PolicyTypes = ["Egress"],
                        EgressRules = ["Allow egress to kube-dns and upstream dependency endpoints."]
                    }
                ]
            });
        }

        public Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayClassInfo>>(_gatewayClasses.ToList());

        public Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GatewayInfo>>(_gatewaysByNamespace[ns].ToList());

        public Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HttpRouteInfo>>(_httpRoutesByNamespace[ns].ToList());

        public Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmReleaseInfo>>(_helmReleasesByNamespace[ns].ToList());

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

        public Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HelmRevisionInfo>>(_helmHistoryByRelease[(ns, releaseName)].ToList());

        public Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
            => Task.FromResult(new HelmReleaseValues { UserValues = string.Empty, ComputedValues = string.Empty });

        public Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
        {
            RollbackHelmCalls.Add((ns, releaseName, targetRevision));

            if (!_deferHelmRollback)
            {
                return Task.CompletedTask;
            }

            return _deferredHelmRollback.Task.WaitAsync(ct);
        }

        public Task<IReadOnlyList<PodMetrics>> GetPodMetricsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PodMetrics>>([]);

        public Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
            => Task.CompletedTask;

        public IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(string ns, string deploymentName,
            LogStreamOptions opts, CancellationToken ct = default)
            => EmptyAggregatedLogLines();

        public Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatefulSetInfo>>(_statefulSetsByNamespace[ns].ToList());

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

        private Dictionary<string, string> ResolveSelector(string ns, string workloadKind, string workloadName)
        {
            if (string.Equals(workloadKind, "Deployment", StringComparison.Ordinal))
            {
                return new Dictionary<string, string>(FindDeployment(ns, workloadName).SelectorLabels, StringComparer.Ordinal);
            }

            if (string.Equals(workloadKind, "StatefulSet", StringComparison.Ordinal))
            {
                return new Dictionary<string, string>(
                    _statefulSetsByNamespace[ns].Single(statefulSet => statefulSet.Name == workloadName).SelectorLabels,
                    StringComparer.Ordinal);
            }

            if (string.Equals(workloadKind, "Pod", StringComparison.Ordinal))
            {
                return new Dictionary<string, string>(FindPod(ns, workloadName).Labels, StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal) { ["app"] = workloadName };
        }

        private List<string> ResolveMatchingPods(string ns, string workloadKind, string workloadName, IReadOnlyDictionary<string, string> selector)
        {
            if (string.Equals(workloadKind, "Pod", StringComparison.Ordinal))
            {
                return [workloadName];
            }

            return _podsByNamespace[ns]
                .Where(pod => MatchesSelector(pod.Labels, selector))
                .Select(pod => pod.Name)
                .ToList();
        }

        private static bool MatchesSelector(IReadOnlyDictionary<string, string> candidate, IReadOnlyDictionary<string, string> selector)
            => selector.All(label => candidate.TryGetValue(label.Key, out var value)
                && string.Equals(value, label.Value, StringComparison.Ordinal));

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

    private sealed class FakeAksClientBootstrapper : IAksClientBootstrapper
    {
        public async Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
        {
            var client = request.ClientOverride ?? new TrackingAksClient();
            var namespaces = await client.GetNamespacesAsync(ct);
            var activeContext = request.RequestedContext
                ?? request.Config?.KubeconfigContext
                ?? "test-context";
            var currentNamespace = request.RequestedNamespace
                ?? request.Config?.DefaultNamespace
                ?? namespaces.FirstOrDefault()
                ?? "default";

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