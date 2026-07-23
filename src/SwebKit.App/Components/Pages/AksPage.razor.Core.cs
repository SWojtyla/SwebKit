using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SwebKit.App.Components.Aks;
using SwebKit.App.Components.Shared;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Components.Pages;

/// <summary>
/// Core state fields, lifecycle methods, bootstrap, commands, and dispose for AksPage.
/// Extracted from AksPage.razor for readability.
/// </summary>
public partial class AksPage
{
    private IAksClient? Client;
    private List<DeploymentInfo> Deployments = [];
    private List<StatefulSetInfo> StatefulSets = [];
    private List<PodInfo> Pods = [];
    private List<ServiceInfo> Services = [];
    private List<IngressInfo> Ingresses = [];
    private List<GatewayClassInfo> GatewayClasses = [];
    private List<GatewayInfo> Gateways = [];
    private List<HttpRouteInfo> HttpRoutes = [];
    private List<HelmReleaseInfo> HelmReleases = [];
    private List<ConfigMapInfo> ConfigMaps = [];
    private List<SecretInfo> Secrets = [];
    private List<HpaInfo> Hpas = [];
    private List<KubernetesEvent> Events = [];
    private int _eventWarningCount;
    private List<JobInfo> Jobs = [];
    private List<CronJobInfo> CronJobs = [];
    private List<string> Namespaces = ["default"];
    private List<KubeContextInfo> Contexts = [];
    private DeploymentInfo? SelectedDeployment;
    private PodInfo? SelectedPod;
    private StatefulSetInfo? SelectedStatefulSet;
    private ConfigMapInfo? SelectedConfigMap;
    private SecretInfo? SelectedSecret;
    private ServiceInfo? SelectedService;
    private IngressInfo? SelectedIngress;
    private GatewayClassInfo? SelectedGatewayClass;
    private GatewayInfo? SelectedGateway;
    private HttpRouteInfo? SelectedHttpRoute;
    private HelmReleaseInfo? SelectedHelmRelease;
    private JobInfo? SelectedJob;
    private CronJobInfo? SelectedCronJob;
    private bool IsLoading;
    private string? ErrorMessage;
    private string? PermissionWarning;
    // Set when the namespace picker came back empty because listing namespaces was RBAC-denied
    // (see AksClientBootstrapResult.NamespacesWarning), not because the cluster has none. Kept
    // separate from PermissionWarning because it's produced during bootstrap, before LoadAsync's
    // per-resource AksAccessDeniedScope exists to merge into.
    private string? _namespaceListWarning;
    private CancellationTokenSource _cts = new();

    // Detail panels component (hosts YAML viewer, Helm panel, logs, scale, etc.)
    private AksDetailPanels _detailPanels = default!;
    private bool _isPanelOpen;

    // Pod metrics
    private List<PodMetrics> PodMetricsList = [];

    // PERF-18: Snapshot record for PageDataCache
    private sealed record AksPageSnapshot(
        List<DeploymentInfo> Deployments,
        List<StatefulSetInfo> StatefulSets,
        List<PodInfo> Pods,
        List<ServiceInfo> Services,
        List<IngressInfo> Ingresses,
        List<GatewayClassInfo> GatewayClasses,
        List<GatewayInfo> Gateways,
        List<HttpRouteInfo> HttpRoutes,
        List<HelmReleaseInfo> HelmReleases,
        List<ConfigMapInfo> ConfigMaps,
        List<SecretInfo> Secrets,
        List<HpaInfo> Hpas,
        List<KubernetesEvent> Events,
        List<JobInfo> Jobs,
        List<CronJobInfo> CronJobs,
        List<PodMetrics> PodMetricsList);

    private string CurrentNamespace = string.Empty;
    private string ActiveContext = string.Empty;
    private string ActiveResourceType = "Deployments";
    private bool _suppressWorkspaceRecent;
    private bool ShowEvents;
    private bool ShowPortForwardSessions;
    private bool _preventGridKey;
    private CancellationTokenSource _bootstrapCts = new();
    private AksBootstrapSignature? _lastBootstrapSignature;

    // Port-forward dialog state
    private bool ShowPortForwardDialog;
    private PodInfo? _pfDialogPod;
    private int _pfDialogRemotePort;

    // Context menu refs and targets
    private ContextMenu DeploymentMenu = default!;
    private ContextMenu PodMenu = default!;
    private ContextMenu ServiceMenu = default!;
    private ContextMenu IngressMenu = default!;
    private ContextMenu GatewayClassMenu = default!;
    private ContextMenu GatewayMenu = default!;
    private ContextMenu HttpRouteMenu = default!;
    private ContextMenu HelmMenu = default!;
    private ContextMenu StatefulSetMenu = default!;
    private ContextMenu ConfigMapMenu = default!;
    private ContextMenu SecretMenu = default!;
    private ContextMenu JobMenu = default!;
    private ContextMenu CronJobMenu = default!;
    private AksConfirmBar Confirm = default!;

    private DeploymentInfo? CtxDeployment;
    private PodInfo? CtxPod;
    private ServiceInfo? CtxService;
    private IngressInfo? CtxIngress;
    private GatewayClassInfo? CtxGatewayClass;
    private GatewayInfo? CtxGateway;
    private HttpRouteInfo? CtxHttpRoute;
    private HelmReleaseInfo? CtxHelm;
    private StatefulSetInfo? CtxStatefulSet;
    private ConfigMapInfo? CtxConfigMap;
    private SecretInfo? CtxSecret;
    private JobInfo? CtxJob;
    private CronJobInfo? CtxCronJob;

    private bool HasAnyData => Deployments.Count > 0 || StatefulSets.Count > 0 || Pods.Count > 0
    || Services.Count > 0
    || Ingresses.Count > 0 || GatewayClasses.Count > 0 || Gateways.Count > 0 || HttpRoutes.Count > 0 || HelmReleases.Count >
0 || ConfigMaps.Count > 0
|| Secrets.Count > 0 || Jobs.Count > 0
    || CronJobs.Count > 0;

    private int ActiveResourceCount => ActiveResourceType switch
    {
        "Deployments" => FilteredDeployments.Count(),
        "StatefulSets" => FilteredStatefulSets.Count(),
        "Pods" => FilteredPods.Count(),
        "Services" => FilteredServices.Count(),
        "Ingresses" => FilteredIngresses.Count(),
        "GatewayClasses" => FilteredGatewayClasses.Count(),
        "Gateways" => FilteredGateways.Count(),
        "HTTPRoutes" => FilteredHttpRoutes.Count(),
        "Helm" => FilteredHelmReleases.Count(),
        "ConfigMaps" => FilteredConfigMaps.Count(),
        "Secrets" => FilteredSecrets.Count(),
        "Jobs" => FilteredJobs.Count(),
        "CronJobs" => FilteredCronJobs.Count(),
        _ => 0
    };

    private string? CurrentCacheKey => !string.IsNullOrEmpty(ActiveContext) && !string.IsNullOrEmpty(CurrentNamespace)
        ? $"aks:{ActiveContext}:{CurrentNamespaceScope.CacheKeyPart}"
        : null;

    private bool HasOpenPanel => _isPanelOpen;

    private void HandlePanelOpenChanged(bool isOpen) => _isPanelOpen = isOpen;

    private bool HasAnyPanel => HasOpenPanel || ShowEvents || ShowPortForwardSessions;

    private bool IsProduction => AppState.Config.IsProduction;

    // ── Easter egg: all pods green ────────────────────────────────────────────
    private bool _allPodsGreenBanner;
    private System.Timers.Timer? _allPodsGreenTimer;

    private void CheckAllPodsGreen()
    {
        var activePods = Pods.Where(p => !IsCompletedPod(p)).ToList();
        if (activePods.Count == 0) return;
        if (activePods.All(p => string.Equals(p.Status, "Running", StringComparison.OrdinalIgnoreCase)))
        {
            _allPodsGreenBanner = true;
            _allPodsGreenTimer?.Dispose();
            _allPodsGreenTimer = new System.Timers.Timer(5000) { AutoReset = false };
            _allPodsGreenTimer.Elapsed += (_, _) =>
            {
                _allPodsGreenTimer?.Dispose();
                _allPodsGreenTimer = null;
                _allPodsGreenBanner = false;
                InvokeAsync(StateHasChanged);
            };
            _allPodsGreenTimer.Start();
        }
    }

    [Parameter] public IAksClient? ClientOverride { get; set; }

    private sealed record AksBootstrapSignature(
        IAksClient? ClientOverride,
        bool UseDemoData,
        string? KubeconfigPath,
        string? KubeconfigContext,
        string DefaultNamespace);

    protected override void OnInitialized()
    {
        EventBus.Subscribe<OpenPortForwardPanelEvent>(OnOpenPortForwardPanel);
        EventBus.Subscribe<RefreshRequestedEvent>(OnRefreshRequested);
        EventBus.Subscribe<AksShortcutEvent>(OnAksShortcut);
        Workspaces.RegisterRestoreHandler("aks", RestoreWorkspaceAsync);
        RegisterAksCommands();
    }

    private void RegisterAksCommands()
    {
        Commands.Register(new AppCommand
        {
            Id = "aks-refresh",
            Label = "Refresh",
            Category = "AKS",
            AreaScope = "aks",
            Shortcut = "F5",
            Execute = () => LoadAsync()
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-restart-deployment",
            Label = "Restart Deployment",
            Category = "AKS",
            AreaScope = "aks",
            IsAvailable = () => SelectedDeployment is not null,
            Execute = async () =>
            {
                if (SelectedDeployment is not null)
                {
                    CtxDeployment = SelectedDeployment; await
OnCtxRestartDeployment();
                }
            }
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-scale-deployment",
            Label = "Scale Deployment",
            Category = "AKS",
            AreaScope = "aks",
            IsAvailable = () => SelectedDeployment is not null,
            Execute = () =>
            {
                if (SelectedDeployment is not null) { CtxDeployment = SelectedDeployment; OnCtxScaleDeployment(); }
                return Task.CompletedTask;
            }
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-view-logs",
            Label = "View Logs",
            Category = "AKS",
            AreaScope = "aks",
            Shortcut = "Alt+L",
            IsAvailable = () => SelectedDeployment is not null || SelectedPod is not null,
            Execute = () => JumpToLogsAsync()
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-open-pod-shell",
            Label = "Open Pod Shell",
            Category = "AKS",
            AreaScope = "aks",
            IsAvailable = () => SelectedPod is not null,
            Execute = async () => { if (SelectedPod is not null) { CtxPod = SelectedPod; await OnCtxOpenPodShell(); } }
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-port-forward",
            Label = "Port-forward Pod",
            Category = "AKS",
            AreaScope = "aks",
            IsAvailable = () => SelectedPod is not null,
            Execute = () =>
            {
                if (SelectedPod is not null) { CtxPod = SelectedPod; OnCtxPortForward(); }
                return Task.CompletedTask;
            }
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-focus-tabs",
            Label = "Focus resource tabs",
            Category = "AKS",
            AreaScope = "aks",
            Shortcut = "Alt+T",
            Execute = FocusResourceTabsAsync
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-focus-grid",
            Label = "Focus resource grid",
            Category = "AKS",
            AreaScope = "aks",
            Shortcut = "Alt+G",
            Execute = FocusGridAsync
        });
        Commands.Register(new AppCommand
        {
            Id = "aks-close-detail",
            Label = "Close detail panel",
            Category = "AKS",
            AreaScope = "aks",
            Shortcut = "Alt+D",
            IsAvailable = () => HasOpenPanel,
            Execute = CloseDetailPanelAsync
        });
    }

    // ── AKS-scoped keyboard shortcut handlers ──────────────────────────────

    private void OnAksShortcut(AksShortcutEvent e)
    {
        _ = InvokeAsync(async () =>
        {
            switch (e.Action)
            {
                case "JumpLogs": await JumpToLogsAsync(); break;
                case "FocusTabs": await FocusResourceTabsAsync(); break;
                case "FocusGrid": await FocusGridAsync(); break;
                case "CloseDetail": await CloseDetailPanelAsync(); break;
            }
            await InvokeAsync(StateHasChanged); // BL-2
        });
    }

    private Task JumpToLogsAsync()
    {
        if (SelectedPod is not null) { CtxPod = SelectedPod; OnCtxViewPodLogs(); }
        else if (SelectedDeployment is not null) { CtxDeployment = SelectedDeployment; OnCtxViewDeploymentLogs(); }
        return Task.CompletedTask;
    }

    private async Task FocusResourceTabsAsync()
    {
        try { await JS.InvokeVoidAsync("SwebKit.focusAksResourceTabs"); }
        catch (OperationCanceledException) { throw; }
        catch { /* focus is best-effort */ }
    }

    private async Task FocusGridAsync()
    {
        try { await JS.InvokeVoidAsync("SwebKit.focusAksGrid"); }
        catch (OperationCanceledException) { throw; }
        catch { /* focus is best-effort */ }
    }

    private async Task CloseDetailPanelAsync()
    {
        if (!HasOpenPanel) return;
        _detailPanels?.ResetAllPanels();
        await InvokeAsync(StateHasChanged);
    }

    private void OnOpenPortForwardPanel(OpenPortForwardPanelEvent _)
    {
        ShowPortForwardSessions = true;
        InvokeAsync(StateHasChanged);
    }

    private void OnRefreshRequested(RefreshRequestedEvent refresh)
    {
        if (!string.Equals(refresh.Area, "aks", StringComparison.Ordinal))
        {
            return;
        }

        _ = InvokeAsync(LoadAsync);
    }

    protected override async Task OnInitializedAsync()
    {
        var config = AppState.Config.AksConfig;
        var signature = new AksBootstrapSignature(
            ClientOverride,
            AppState.UseDemoData,
            config?.KubeconfigPath,
            config?.KubeconfigContext,
            NormalizeDefaultNamespace(config));

        if (_lastBootstrapSignature == signature)
        {
            // Same environment as last render, but we may have arrived here via a workspace
            // snapshot (e.g. Dashboard tile "Open") that registered a pending restore.
            _ = Workspaces.ApplyPendingRestoreAsync("aks");
            return;
        }

        _lastBootstrapSignature = signature;
        SyncFromEnvironment();

        // PERF: The AksClientBootstrapper offloads expensive k8s client creation (kubeconfig
        // parsing, token retrieval, file I/O) to a thread-pool thread, so the UI thread stays
        // responsive. BootstrapAndLoadAsync renders the loading shell before it yields.
        try
        {
            await BootstrapAndLoadAsync(ActiveContext, CurrentNamespace);
        }
        catch (OperationCanceledException)
        {
            // Component disposed or bootstrap superseded — no-op.
        }
    }

    private void SyncFromEnvironment()
    {
        var config = AppState.Config.AksConfig;
        CurrentNamespace = NormalizeDefaultNamespace(config);
        ActiveContext = config?.KubeconfigContext ?? string.Empty;
    }

    private static string NormalizeDefaultNamespace(AksConfig? config) =>
        string.IsNullOrWhiteSpace(config?.DefaultNamespace) ? string.Empty : config.DefaultNamespace.Trim();

    private async Task BootstrapAndLoadAsync(string requestedContext, string requestedNamespace)
    {
        var newBootstrapCts = new CancellationTokenSource();
        var previousBootstrapCts = Interlocked.Exchange(ref _bootstrapCts, newBootstrapCts);
        previousBootstrapCts.Cancel();
        previousBootstrapCts.Dispose();
        var ct = newBootstrapCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            // Check warm-client cache first (cheap; safe to read on UI thread)
            if (!AppState.UseDemoData)
            {
                var warm = AksWarmupCache.TryGet();
                if (warm is not null
                    && warm.Status == AksClientBootstrapStatus.Connected
                    && warm.Client is not null
                    && CanReuseWarmBootstrapResult(warm, requestedContext, requestedNamespace))
                {
                    var warmResult = warm;
                    AksWarmupCache.Invalidate(); // consume once
                    await ApplyBootstrapResultAndLoadAsync(warmResult, ct);
                    return;
                }
            }

            // The bootstrapper offloads expensive k8s client creation (kubeconfig parsing,
            // token retrieval, file I/O) to a thread-pool thread internally. The await here
            // yields only for that real work, while fake bootstrappers in tests complete
            // synchronously so bUnit can wait for initialization.
            var result = await AksBootstrapper.BootstrapAsync(
                new AksClientBootstrapRequest(
                    ClientOverride,
                    AppState.UseDemoData,
                    AppState.Config.AksConfig,
                    requestedContext,
                    requestedNamespace),
                ct);

            ct.ThrowIfCancellationRequested();

            await ApplyBootstrapResultAndLoadAsync(result, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private async Task ApplyBootstrapResultAndLoadAsync(AksClientBootstrapResult result, CancellationToken ct)
    {
        // Apply result on the UI thread so downstream renders and component refs are safe.
        await InvokeAsync(() =>
        {
            Client = result.Client;
            Contexts = result.Contexts.ToList();
            Namespaces = result.Namespaces.ToList();
            ActiveContext = result.ActiveContext;
            CurrentNamespace = result.CurrentNamespace;
            _namespaceListWarning = result.NamespacesWarning;

            switch (result.Status)
            {
                case AksClientBootstrapStatus.NotConfigured:
                    ConnectionState.SetNotConfigured("aks");
                    IsLoading = false;
                    StateHasChanged();
                    return;
                case AksClientBootstrapStatus.Error:
                    ErrorMessage = result.ErrorMessage;
                    ConnectionState.SetError("aks", result.ErrorMessage ?? "AKS bootstrap failed.");
                    IsLoading = false;
                    StateHasChanged();
                    return;
            }

            StateHasChanged();
        });

        if (result.Status != AksClientBootstrapStatus.Connected)
        {
            return;
        }

        await LoadAsync();
        ConnectionState.SetConnected("aks");
        await Workspaces.ApplyPendingRestoreAsync("aks");
    }

    public void Dispose()
    {
        // Replenish the warmup cache with the live client so the next navigation
        // can skip the bootstrap round-trip instead of re-showing the spinner.
        if (!AppState.UseDemoData && Client is not null)
        {
            AksWarmupCache.Store(new AksClientBootstrapResult(
                AksClientBootstrapStatus.Connected,
                Client,
                Contexts,
                Namespaces,
                ActiveContext,
                CurrentNamespace,
                null)
            {
                NamespacesWarning = _namespaceListWarning
            });
        }

        var bootstrapCts = Interlocked.Exchange(ref _bootstrapCts, null!);
        bootstrapCts?.Cancel();
        bootstrapCts?.Dispose();
        var cts = Interlocked.Exchange(ref _cts, null!);
        cts?.Cancel();
        cts?.Dispose();
        EventBus.Unsubscribe<OpenPortForwardPanelEvent>(OnOpenPortForwardPanel);
        EventBus.Unsubscribe<RefreshRequestedEvent>(OnRefreshRequested);
        EventBus.Unsubscribe<AksShortcutEvent>(OnAksShortcut);
        Workspaces.UnregisterRestoreHandler("aks");
        foreach (var id in new[] { "aks-refresh", "aks-restart-deployment", "aks-scale-deployment",
                                   "aks-view-logs", "aks-open-pod-shell", "aks-port-forward",
                                   "aks-focus-tabs", "aks-focus-grid", "aks-close-detail" })
            Commands.Unregister(id);
        Selection.SetSelection("aks", null);
        GC.SuppressFinalize(this);
    }
}
