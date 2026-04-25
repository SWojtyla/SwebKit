using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    private const string ResourceKindPods = "Pods";
    private const string ResourceKindDeployments = "Deployments";
    private const string ResourceKindStatefulSets = "StatefulSets";
    private const string ResourceKindJobs = "Jobs";
    private const string ResourceKindCronJobs = "CronJobs";
    private const string ResourceKindServices = "Services";
    private const string ResourceKindIngresses = "Ingresses";
    private const string ResourceKindGatewayClasses = "GatewayClasses";
    private const string ResourceKindGateways = "Gateways";
    private const string ResourceKindHttpRoutes = "HTTPRoutes";

    private static readonly IReadOnlyList<string> DefaultResourceKinds =
    [
        ResourceKindPods,
        ResourceKindDeployments,
        ResourceKindStatefulSets,
        ResourceKindJobs,
        ResourceKindCronJobs,
        ResourceKindServices,
        ResourceKindIngresses,
        ResourceKindGatewayClasses,
        ResourceKindGateways,
        ResourceKindHttpRoutes,
    ];

    private readonly Dictionary<string, IReadOnlyList<AksResourceBrowseItemViewModel>> _resourceBrowseCache =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedResourceKinds = new(StringComparer.Ordinal);
    private bool _suppressResourceSelectionSideEffects;

    public IReadOnlyList<string> ResourceKinds => DefaultResourceKinds;

    [ObservableProperty]
    public partial string SelectedResourceKind { get; set; } = ResourceKindPods;

    [ObservableProperty]
    public partial string ResourceFilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<AksResourceBrowseItemViewModel> ResourceItems { get; set; } = [];

    [ObservableProperty]
    public partial AksResourceBrowseItemViewModel? SelectedResourceItem { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<AksResourceFactItemViewModel> SelectedResourceFacts { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> SelectedResourceHighlights { get; set; } = [];

    [ObservableProperty]
    public partial string? ResourceLoadMessage { get; set; }

    public string ResourceExplorerDescription =>
        $"{ActiveResourceCountLabel} in {ResolveResourceScopeLabel()}. Keep a pod selected below if you want logs, shell, and port-forward access to stay visible while you inspect other resources.";

    public string ActiveResourceCountLabel => FormatResourceCountLabel(ResourceItems.Count, SelectedResourceKind);

    public string ResourceEmptyTitle => SelectedResourceKindFailed
        ? $"{SelectedResourceKind} could not be loaded"
        : $"No {ResolveResourceSingularName(SelectedResourceKind, plural: true)} found";

    public string ResourceEmptyMessage =>
        SelectedResourceKindFailed
            ? $"The native explorer could not load {ResolveResourceSingularName(SelectedResourceKind, plural: true)} for {ResolveResourceScopeLabel()}. Check connectivity or permissions and refresh."
            : $"The current {ResolveResourceScopeLabel()} does not expose any {ResolveResourceSingularName(SelectedResourceKind, plural: true)} for the native explorer yet.";

    public bool ShowResourceLoadMessage => !string.IsNullOrWhiteSpace(ResourceLoadMessage);

    public string ResourceLoadMessageTitle => HasLoadedExplorerData
        ? "Some AKS resources were not loaded"
        : "AKS explorer load failed";

    public Visibility ResourceListVisibility => ResourceItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ResourceEmptyStateVisibility =>
        Client is not null && !IsLoading && ErrorMessage is null && ResourceItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool HasSelectedResource => SelectedResourceItem is not null;

    public Visibility SelectedResourceContentVisibility => SelectedResourceItem is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedResourceEmptyStateVisibility => SelectedResourceItem is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectedResourceHighlightsVisibility => SelectedResourceHighlights.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string SelectedResourceTitle => SelectedResourceItem?.Name ?? "Resource detail";

    public string SelectedResourceSubtitle => SelectedResourceItem?.DetailSubtitle
        ?? "Select a pod, workload, batch resource, or network edge from the native explorer to inspect its current scope details.";

    public string PodMetricValueText => Pods.Count.ToString("N0", CultureInfo.CurrentCulture);

    public string PodMetricDetailText
    {
        get
        {
            var healthy = Pods.Count(pod => string.Equals(pod.Health, "Healthy", StringComparison.Ordinal));
            var warning = Pods.Count(pod => string.Equals(pod.Health, "Warning", StringComparison.Ordinal));
            var error = Pods.Count(pod => string.Equals(pod.Health, "Error", StringComparison.Ordinal));
            return $"{healthy:N0} healthy · {warning:N0} warning · {error:N0} error";
        }
    }

    public string WorkloadMetricValueText =>
        (GetResourceCount(ResourceKindDeployments)
         + GetResourceCount(ResourceKindStatefulSets)
         + GetResourceCount(ResourceKindJobs)
         + GetResourceCount(ResourceKindCronJobs)).ToString("N0", CultureInfo.CurrentCulture);

    public string WorkloadMetricDetailText =>
        $"{GetResourceCount(ResourceKindDeployments):N0} deployments · {GetResourceCount(ResourceKindStatefulSets):N0} statefulsets · {GetResourceCount(ResourceKindJobs) + GetResourceCount(ResourceKindCronJobs):N0} batch resources";

    public string NetworkMetricValueText =>
        (GetResourceCount(ResourceKindServices)
         + GetResourceCount(ResourceKindIngresses)
         + GetResourceCount(ResourceKindGatewayClasses)
         + GetResourceCount(ResourceKindGateways)
         + GetResourceCount(ResourceKindHttpRoutes)).ToString("N0", CultureInfo.CurrentCulture);

    public string NetworkMetricDetailText =>
        $"{GetResourceCount(ResourceKindServices):N0} services · {GetResourceCount(ResourceKindIngresses):N0} ingresses · {GetResourceCount(ResourceKindGatewayClasses) + GetResourceCount(ResourceKindGateways) + GetResourceCount(ResourceKindHttpRoutes):N0} gateway API resources";

    public string FocusMetricValueText => ResourceItems.Count.ToString("N0", CultureInfo.CurrentCulture);

    public string FocusMetricValueLabel => SelectedResourceKind;

    public string FocusMetricDetailText => ResolveResourceScopeLabel();

    private bool SelectedResourceKindFailed => _failedResourceKinds.Contains(SelectedResourceKind);

    private bool HasLoadedExplorerData => _resourceBrowseCache.Values.Any(items => items.Count > 0);

    partial void OnSelectedResourceKindChanged(string value)
    {
        RefreshVisibleResourceItems();
        OnPropertyChanged(nameof(ActiveResourceCountLabel));
        OnPropertyChanged(nameof(ResourceExplorerDescription));
        OnPropertyChanged(nameof(ResourceEmptyTitle));
        OnPropertyChanged(nameof(ResourceEmptyMessage));
        OnPropertyChanged(nameof(FocusMetricValueLabel));
    }

    partial void OnResourceFilterTextChanged(string value)
    {
        RefreshVisibleResourceItems();
    }

    partial void OnResourceItemsChanged(IReadOnlyList<AksResourceBrowseItemViewModel> value)
    {
        OnPropertyChanged(nameof(ActiveResourceCountLabel));
        OnPropertyChanged(nameof(ResourceExplorerDescription));
        OnPropertyChanged(nameof(ResourceListVisibility));
        OnPropertyChanged(nameof(ResourceEmptyStateVisibility));
        OnPropertyChanged(nameof(FocusMetricValueText));
    }

    partial void OnSelectedResourceItemChanged(AksResourceBrowseItemViewModel? value)
    {
        ResetSelectedResourceActionState(value);
        SelectedResourceFacts = value?.DetailFacts ?? [];
        SelectedResourceHighlights = value?.Highlights ?? [];

        OnPropertyChanged(nameof(HasSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceContentVisibility));
        OnPropertyChanged(nameof(SelectedResourceEmptyStateVisibility));
        OnPropertyChanged(nameof(SelectedResourceTitle));
        OnPropertyChanged(nameof(SelectedResourceSubtitle));

        if (_suppressResourceSelectionSideEffects || !string.Equals(SelectedResourceKind, ResourceKindPods, StringComparison.Ordinal))
        {
            return;
        }

        var selectedPod = value?.PodItem;
        if (!ReferenceEquals(SelectedPod, selectedPod))
        {
            SelectedPod = selectedPod;
        }
    }

    partial void OnSelectedResourceHighlightsChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(SelectedResourceHighlightsVisibility));
    }

    partial void OnResourceLoadMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowResourceLoadMessage));
        OnPropertyChanged(nameof(ResourceLoadMessageTitle));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ResourceEmptyStateVisibility));
    }

    [RelayCommand]
    private async Task SelectResourceItemAsync(AksResourceBrowseItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedResourceItem, item))
        {
            if (item.PodItem is not null)
            {
                await SelectPodAsync(item.PodItem);
            }

            return;
        }

        SelectedResourceItem = item;
    }

    [RelayCommand]
    private void ClearSelectedResource()
    {
        if (SelectedResourceItem is null)
        {
            return;
        }

        SelectedResourceItem = null;
    }

    private async Task LoadResourceScopeAsync(CancellationToken ct)
    {
        Pods.Clear();
        ResourceItems = [];
        _resourceBrowseCache.Clear();
        _failedResourceKinds.Clear();
        ResourceLoadMessage = null;

        if (Client is null)
        {
            SelectedPod = null;
            SelectedResourceItem = null;
            return;
        }

        var namespaces = ResolveNamespacesForLoad();
        if (namespaces.Count == 0)
        {
            RefreshVisibleResourceItems();
            ReconcileSelectedPodAfterLoad();
            ReconcileSelectedResourceAfterLoad();
            RefreshResourceMetricProperties();
            return;
        }

        var loadWarnings = new List<string>();

        var podResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindPods,
            (ns, token) => Client.GetPodsAsync(ns, labelSelector: null, token),
            ct);
        var deploymentResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindDeployments,
            (ns, token) => Client.GetDeploymentsAsync(ns, token),
            ct);
        var statefulSetResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindStatefulSets,
            (ns, token) => Client.GetStatefulSetsAsync(ns, token),
            ct);
        var jobResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindJobs,
            (ns, token) => Client.GetJobsAsync(ns, token),
            ct);
        var cronJobResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindCronJobs,
            (ns, token) => Client.GetCronJobsAsync(ns, token),
            ct);
        var serviceResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindServices,
            (ns, token) => Client.GetServicesAsync(ns, token),
            ct);
        var ingressResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindIngresses,
            (ns, token) => Client.GetIngressesAsync(ns, token),
            ct);
        var gatewayClassResult = await LoadClusterScopedResourceAsync(
            ResourceKindGatewayClasses,
            token => Client.GetGatewayClassesAsync(token),
            ct);
        var gatewayResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindGateways,
            (ns, token) => Client.GetGatewaysAsync(ns, token),
            ct);
        var httpRouteResult = await LoadAcrossNamespacesAsync(
            namespaces,
            ResourceKindHttpRoutes,
            (ns, token) => Client.GetHttpRoutesAsync(ns, token),
            ct);

        TrackResourceLoadFailures(loadWarnings, ResourceKindPods, podResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindDeployments, deploymentResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindStatefulSets, statefulSetResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindJobs, jobResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindCronJobs, cronJobResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindServices, serviceResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindIngresses, ingressResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindGatewayClasses, gatewayClassResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindGateways, gatewayResult.FailedNamespaces);
        TrackResourceLoadFailures(loadWarnings, ResourceKindHttpRoutes, httpRouteResult.FailedNamespaces);

        var podItems = podResult.Items
            .OrderBy(pod => pod.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pod => pod.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pod => new AksPodItemViewModel(pod))
            .ToList();

        foreach (var podItem in podItems)
        {
            Pods.Add(podItem);
        }

        _resourceBrowseCache[ResourceKindPods] = podItems
            .Select(CreatePodBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindDeployments] = deploymentResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateDeploymentBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindStatefulSets] = statefulSetResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateStatefulSetBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindJobs] = jobResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateJobBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindCronJobs] = cronJobResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCronJobBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindServices] = serviceResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateServiceBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindIngresses] = ingressResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateIngressBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindGatewayClasses] = gatewayClassResult.Items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateGatewayClassBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindGateways] = gatewayResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateGatewayBrowseItem)
            .ToList();

        _resourceBrowseCache[ResourceKindHttpRoutes] = httpRouteResult.Items
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateHttpRouteBrowseItem)
            .ToList();

        ResourceLoadMessage = BuildResourceLoadMessage(loadWarnings, HasLoadedExplorerData);

        ReconcileSelectedPodAfterLoad();
        RefreshVisibleResourceItems();
        ReconcileSelectedResourceAfterLoad();
        RefreshResourceMetricProperties();
    }

    private void ClearResourceExplorerState()
    {
        ResourceItems = [];
        SelectedResourceFacts = [];
        SelectedResourceHighlights = [];
        SelectedResourceItem = null;
        _failedResourceKinds.Clear();
        ResourceLoadMessage = null;
        _resourceBrowseCache.Clear();
        RefreshResourceMetricProperties();
    }

    private void RefreshVisibleResourceItems()
    {
        if (!_resourceBrowseCache.TryGetValue(SelectedResourceKind, out var items))
        {
            items = [];
        }

        if (!string.IsNullOrWhiteSpace(ResourceFilterText))
        {
            items = items
                .Where(item => item.SearchText.Contains(ResourceFilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ResourceItems = items;

        var nextSelection = SelectedResourceItem is not null && string.Equals(SelectedResourceItem.Kind, SelectedResourceKind, StringComparison.Ordinal)
            ? ResourceItems.FirstOrDefault(item => item.Matches(SelectedResourceItem))
            : null;

        if (nextSelection is null
            && string.Equals(SelectedResourceKind, ResourceKindPods, StringComparison.Ordinal)
            && SelectedPod is not null)
        {
            nextSelection = ResourceItems.FirstOrDefault(item => item.PodItem is not null
                && string.Equals(item.PodItem.Namespace, SelectedPod.Namespace, StringComparison.Ordinal)
                && string.Equals(item.PodItem.Name, SelectedPod.Name, StringComparison.Ordinal));
        }

        _suppressResourceSelectionSideEffects = true;
        try
        {
            SelectedResourceItem = nextSelection;
        }
        finally
        {
            _suppressResourceSelectionSideEffects = false;
        }
    }

    private void ReconcileSelectedResourceAfterLoad()
    {
        if (SelectedResourceItem is not null)
        {
            var matchingResource = ResourceItems.FirstOrDefault(item => item.Matches(SelectedResourceItem));
            if (!ReferenceEquals(matchingResource, SelectedResourceItem))
            {
                _suppressResourceSelectionSideEffects = true;
                try
                {
                    SelectedResourceItem = matchingResource;
                }
                finally
                {
                    _suppressResourceSelectionSideEffects = false;
                }
            }

            return;
        }

        if (!string.Equals(SelectedResourceKind, ResourceKindPods, StringComparison.Ordinal) || SelectedPod is null)
        {
            return;
        }

        var matchingPodResource = ResourceItems.FirstOrDefault(item => item.PodItem is not null
            && string.Equals(item.PodItem.Namespace, SelectedPod.Namespace, StringComparison.Ordinal)
            && string.Equals(item.PodItem.Name, SelectedPod.Name, StringComparison.Ordinal));

        if (matchingPodResource is null)
        {
            return;
        }

        _suppressResourceSelectionSideEffects = true;
        try
        {
            SelectedResourceItem = matchingPodResource;
        }
        finally
        {
            _suppressResourceSelectionSideEffects = false;
        }
    }

    private IReadOnlyList<string> ResolveNamespacesForLoad()
    {
        if (string.IsNullOrWhiteSpace(SelectedNamespace))
        {
            return [];
        }

        if (!string.Equals(SelectedNamespace, "*", StringComparison.Ordinal))
        {
            return [SelectedNamespace];
        }

        return NamespaceOptions
            .Where(option => !string.Equals(option, "*", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ResourceScopeLoadResult<T>> LoadAcrossNamespacesAsync<T>(
        IReadOnlyList<string> namespaces,
        string resourceKind,
        Func<string, CancellationToken, Task<IReadOnlyList<T>>> loader,
        CancellationToken ct)
    {
        var items = new List<T>();
        var failedNamespaces = new List<string>();

        foreach (var ns in namespaces)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var namespaceItems = await loader(ns, ct);
                items.AddRange(namespaceItems);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedNamespaces.Add(ns);
                _logger.LogWarning(ex, "AKS {ResourceKind} load failed for namespace {Namespace}.", resourceKind, ns);
            }
        }

        return new ResourceScopeLoadResult<T>(items, failedNamespaces);
    }

    private async Task<ResourceScopeLoadResult<T>> LoadClusterScopedResourceAsync<T>(
        string resourceKind,
        Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
        CancellationToken ct)
    {
        try
        {
            var items = await loader(ct);
            return new ResourceScopeLoadResult<T>(items, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AKS {ResourceKind} load failed for cluster scope.", resourceKind);
            return new ResourceScopeLoadResult<T>([], ["cluster scope"]);
        }
    }

    private void TrackResourceLoadFailures(List<string> warnings, string resourceKind, IReadOnlyList<string> failedNamespaces)
    {
        if (failedNamespaces.Count == 0)
        {
            return;
        }

        _failedResourceKinds.Add(resourceKind);
        warnings.Add($"{resourceKind} ({FormatFailedScopeLabel(failedNamespaces)})");
    }

    private static string? BuildResourceLoadMessage(IReadOnlyList<string> warnings, bool hasLoadedResources)
    {
        if (warnings.Count == 0)
        {
            return null;
        }

        return hasLoadedResources
            ? $"Some explorer datasets could not be loaded: {string.Join(", ", warnings)}. The remaining AKS resources stay available."
            : $"The native explorer could not load any datasets: {string.Join(", ", warnings)}. Check connectivity or permissions and refresh.";
    }

    private static string FormatFailedScopeLabel(IReadOnlyList<string> failedNamespaces)
    {
        if (failedNamespaces.Count == 1)
        {
            return failedNamespaces[0];
        }

        var preview = string.Join(", ", failedNamespaces.Take(2));
        return failedNamespaces.Count <= 2
            ? preview
            : $"{preview} +{failedNamespaces.Count - 2}";
    }

    private int GetResourceCount(string resourceKind)
        => _resourceBrowseCache.TryGetValue(resourceKind, out var items) ? items.Count : 0;

    private void RefreshResourceMetricProperties()
    {
        OnPropertyChanged(nameof(PodMetricValueText));
        OnPropertyChanged(nameof(PodMetricDetailText));
        OnPropertyChanged(nameof(WorkloadMetricValueText));
        OnPropertyChanged(nameof(WorkloadMetricDetailText));
        OnPropertyChanged(nameof(NetworkMetricValueText));
        OnPropertyChanged(nameof(NetworkMetricDetailText));
        OnPropertyChanged(nameof(FocusMetricValueText));
        OnPropertyChanged(nameof(FocusMetricDetailText));
        OnPropertyChanged(nameof(ResourceExplorerDescription));
        OnPropertyChanged(nameof(ResourceEmptyStateVisibility));
        OnPropertyChanged(nameof(ResourceEmptyTitle));
        OnPropertyChanged(nameof(ResourceEmptyMessage));
        OnPropertyChanged(nameof(ResourceLoadMessageTitle));
    }

    private string ResolveResourceScopeLabel()
    {
        if (string.IsNullOrWhiteSpace(SelectedNamespace))
        {
            return "the current cluster scope";
        }

        return string.Equals(SelectedNamespace, "*", StringComparison.Ordinal)
            ? "all namespaces"
            : $"namespace '{SelectedNamespace}'";
    }

    private static string ResolveResourceSingularName(string resourceKind, bool plural)
        => resourceKind switch
        {
            ResourceKindDeployments => plural ? "deployments" : "deployment",
            ResourceKindStatefulSets => plural ? "statefulsets" : "statefulset",
            ResourceKindJobs => plural ? "jobs" : "job",
            ResourceKindCronJobs => plural ? "cronjobs" : "cronjob",
            ResourceKindServices => plural ? "services" : "service",
            ResourceKindIngresses => plural ? "ingresses" : "ingress",
            ResourceKindGatewayClasses => plural ? "gateway classes" : "gateway class",
            ResourceKindGateways => plural ? "gateways" : "gateway",
            ResourceKindHttpRoutes => plural ? "HTTP routes" : "HTTP route",
            _ => plural ? "pods" : "pod",
        };

    private static string FormatResourceCountLabel(int count, string resourceKind)
    {
        var name = ResolveResourceSingularName(resourceKind, plural: count != 1);
        return count == 1 ? $"1 {name}" : $"{count:N0} {name}";
    }

    private static AksResourceBrowseItemViewModel CreatePodBrowseItem(AksPodItemViewModel pod)
        => new(
            Kind: ResourceKindPods,
            ApiKind: "Pod",
            Name: pod.Name,
            Namespace: pod.Namespace,
            StatusLabel: pod.Health,
            NamespaceLabel: $"Namespace · {pod.Namespace}",
            SummaryLine: $"{pod.Status} · Ready {pod.Ready}",
            SecondaryLine: $"Restarts {pod.Restarts} · Node {pod.Node}",
            DetailSubtitle: $"{pod.Namespace} · {pod.Status}",
            ActionLabel: "Logs",
            SearchText: string.Join(' ', pod.Namespace, pod.Name, pod.Health, pod.Status, pod.Ready, pod.Node, string.Join(' ', pod.Containers)),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Health", pod.Health),
                new AksResourceFactItemViewModel("Status", pod.Status),
                new AksResourceFactItemViewModel("Ready", pod.Ready),
                new AksResourceFactItemViewModel("Restarts", pod.Restarts),
                new AksResourceFactItemViewModel("Node", pod.Node),
                new AksResourceFactItemViewModel("Containers", pod.Containers.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: pod.Containers.Select(container => $"Container · {container}").ToList(),
            CanAnalyzeNetworkPolicies: true,
            NetworkPolicyKind: "Pod",
            PodItem: pod);

    private static AksResourceBrowseItemViewModel CreateDeploymentBrowseItem(DeploymentInfo deployment)
    {
        var selectorSummary = BuildSelectorSummary(deployment.SelectorLabels);
        var status = string.IsNullOrWhiteSpace(deployment.Status)
            ? deployment.ReadyReplicas >= deployment.Replicas ? "Ready" : "Degraded"
            : deployment.Status;

        var highlights = BuildDictionaryHighlights("Selector", deployment.SelectorLabels);
        if (!string.IsNullOrWhiteSpace(selectorSummary))
        {
            highlights.Add($"Selector summary · {selectorSummary}");
        }

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindDeployments,
            ApiKind: "Deployment",
            Name: deployment.Name,
            Namespace: deployment.Namespace,
            StatusLabel: status,
            NamespaceLabel: $"Namespace · {deployment.Namespace}",
            SummaryLine: $"Ready {deployment.ReadyReplicas}/{deployment.Replicas}",
            SecondaryLine: string.IsNullOrWhiteSpace(selectorSummary) ? "No selector labels were surfaced for this workload." : selectorSummary,
            DetailSubtitle: $"{deployment.Namespace} · deployment",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', deployment.Namespace, deployment.Name, status, selectorSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", status),
                new AksResourceFactItemViewModel("Namespace", deployment.Namespace),
                new AksResourceFactItemViewModel("Ready replicas", deployment.ReadyReplicas.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Desired replicas", deployment.Replicas.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Selector labels", deployment.SelectorLabels.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: highlights,
            CanEditYaml: true,
            CanAnalyzeNetworkPolicies: true,
            NetworkPolicyKind: "Deployment",
            CanRestart: true,
            ScaleReplicaCount: deployment.Replicas);
    }

    private static AksResourceBrowseItemViewModel CreateStatefulSetBrowseItem(StatefulSetInfo statefulSet)
    {
        var selectorSummary = BuildSelectorSummary(statefulSet.SelectorLabels);
        var status = statefulSet.ReadyReplicas >= statefulSet.Replicas ? "Ready" : "Degraded";

        var revisionSummary = string.Join(
            " · ",
            new[]
            {
                string.IsNullOrWhiteSpace(statefulSet.CurrentRevision) ? null : $"Current {statefulSet.CurrentRevision}",
                string.IsNullOrWhiteSpace(statefulSet.UpdateRevision) ? null : $"Update {statefulSet.UpdateRevision}",
            }.Where(value => value is not null));

        var highlights = BuildDictionaryHighlights("Selector", statefulSet.SelectorLabels);
        if (!string.IsNullOrWhiteSpace(revisionSummary))
        {
            highlights.Add($"Revisions · {revisionSummary}");
        }

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindStatefulSets,
            ApiKind: "StatefulSet",
            Name: statefulSet.Name,
            Namespace: statefulSet.Namespace,
            StatusLabel: status,
            NamespaceLabel: $"Namespace · {statefulSet.Namespace}",
            SummaryLine: $"Ready {statefulSet.ReadyReplicas}/{statefulSet.Replicas}",
            SecondaryLine: string.IsNullOrWhiteSpace(revisionSummary)
                ? (string.IsNullOrWhiteSpace(selectorSummary) ? "No revision metadata was surfaced for this statefulset." : selectorSummary)
                : revisionSummary,
            DetailSubtitle: $"{statefulSet.Namespace} · statefulset",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', statefulSet.Namespace, statefulSet.Name, status, selectorSummary, revisionSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", status),
                new AksResourceFactItemViewModel("Namespace", statefulSet.Namespace),
                new AksResourceFactItemViewModel("Ready replicas", statefulSet.ReadyReplicas.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Desired replicas", statefulSet.Replicas.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Current revision", statefulSet.CurrentRevision ?? "—"),
                new AksResourceFactItemViewModel("Update revision", statefulSet.UpdateRevision ?? "—"),
            ],
            Highlights: highlights,
            CanEditYaml: true,
            CanAnalyzeNetworkPolicies: true,
            NetworkPolicyKind: "StatefulSet",
            CanRestart: true,
            ScaleReplicaCount: statefulSet.Replicas);
    }

    private static AksResourceBrowseItemViewModel CreateJobBrowseItem(JobInfo job)
    {
        var completionSummary = job.DesiredCompletions is int desiredCompletions
            ? $"Succeeded {job.Succeeded}/{desiredCompletions}"
            : $"Succeeded {job.Succeeded}";
        var executionSummary = $"Active {job.Active} · Failed {job.Failed}";
        var sourceSummary = string.IsNullOrWhiteSpace(job.SourceKind) || string.IsNullOrWhiteSpace(job.SourceName)
            ? "Manual or controller-owned job"
            : $"Source {job.SourceKind} {job.SourceName}";

        var highlights = BuildDictionaryHighlights("Label", job.Labels);
        if (!string.IsNullOrWhiteSpace(sourceSummary))
        {
            highlights.Add($"Source · {sourceSummary}");
        }

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindJobs,
            ApiKind: "Job",
            Name: job.Name,
            Namespace: job.Namespace,
            StatusLabel: string.IsNullOrWhiteSpace(job.Status) ? "Unknown" : job.Status,
            NamespaceLabel: $"Namespace · {job.Namespace}",
            SummaryLine: completionSummary,
            SecondaryLine: $"{executionSummary} · {sourceSummary}",
            DetailSubtitle: $"{job.Namespace} · job",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', job.Namespace, job.Name, job.Status, completionSummary, executionSummary, sourceSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", job.Status),
                new AksResourceFactItemViewModel("Namespace", job.Namespace),
                new AksResourceFactItemViewModel("Active", job.Active.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Succeeded", job.Succeeded.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Failed", job.Failed.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Desired completions", job.DesiredCompletions?.ToString(CultureInfo.CurrentCulture) ?? "—"),
                new AksResourceFactItemViewModel("Started", FormatTimestamp(job.StartTime)),
                new AksResourceFactItemViewModel("Completed", FormatTimestamp(job.CompletionTime)),
            ],
            Highlights: highlights,
            CanRerun: true);
    }

    private static AksResourceBrowseItemViewModel CreateCronJobBrowseItem(CronJobInfo cronJob)
    {
        var status = cronJob.Suspend ? "Suspended" : cronJob.ActiveCount > 0 ? "Running" : "Scheduled";
        var schedule = string.IsNullOrWhiteSpace(cronJob.Schedule) ? "No schedule surfaced" : cronJob.Schedule;
        var lastExecution = cronJob.LastSuccessfulTime ?? cronJob.LastScheduleTime;
        var highlights = BuildDictionaryHighlights("Label", cronJob.Labels);
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            highlights.Add($"Schedule · {schedule}");
        }

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindCronJobs,
            ApiKind: "CronJob",
            Name: cronJob.Name,
            Namespace: cronJob.Namespace,
            StatusLabel: status,
            NamespaceLabel: $"Namespace · {cronJob.Namespace}",
            SummaryLine: schedule,
            SecondaryLine: $"Active jobs {cronJob.ActiveCount} · Last run {FormatTimestamp(lastExecution)}",
            DetailSubtitle: $"{cronJob.Namespace} · cronjob",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', cronJob.Namespace, cronJob.Name, status, schedule),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", status),
                new AksResourceFactItemViewModel("Namespace", cronJob.Namespace),
                new AksResourceFactItemViewModel("Schedule", schedule),
                new AksResourceFactItemViewModel("Active jobs", cronJob.ActiveCount.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Last schedule", FormatTimestamp(cronJob.LastScheduleTime)),
                new AksResourceFactItemViewModel("Last success", FormatTimestamp(cronJob.LastSuccessfulTime)),
            ],
            Highlights: highlights,
            CanTrigger: true);
    }

    private static AksResourceBrowseItemViewModel CreateServiceBrowseItem(ServiceInfo service)
    {
        var portsSummary = BuildServicePortsSummary(service.Ports);
        var addressSummary = service.ExternalAddresses.Count == 0
            ? $"Cluster IP {service.ClusterIp}"
            : $"Cluster IP {service.ClusterIp} · {service.ExternalAddresses.Count:N0} external address(es)";

        var highlights = BuildServiceHighlights(service);

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindServices,
            ApiKind: "Service",
            Name: service.Name,
            Namespace: service.Namespace,
            StatusLabel: service.Type,
            NamespaceLabel: $"Namespace · {service.Namespace}",
            SummaryLine: string.IsNullOrWhiteSpace(portsSummary) ? "No ports were reported for this service." : portsSummary,
            SecondaryLine: addressSummary,
            DetailSubtitle: $"{service.Namespace} · service",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', service.Namespace, service.Name, service.Type, portsSummary, addressSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Type", service.Type),
                new AksResourceFactItemViewModel("Namespace", service.Namespace),
                new AksResourceFactItemViewModel("Cluster IP", service.ClusterIp),
                new AksResourceFactItemViewModel("Ports", service.Ports.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("External addresses", service.ExternalAddresses.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Selector labels", service.SelectorLabels.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: highlights);
    }

    private static AksResourceBrowseItemViewModel CreateIngressBrowseItem(IngressInfo ingress)
    {
        var hostSummary = BuildIngressHostSummary(ingress);
        var addressSummary = ingress.Addresses.Count == 0
            ? "No ingress addresses were reported yet."
            : string.Join(" · ", ingress.Addresses);
        var status = ingress.Addresses.Count == 0 ? "Pending" : "Ready";

        var highlights = BuildIngressHighlights(ingress);

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindIngresses,
            ApiKind: "Ingress",
            Name: ingress.Name,
            Namespace: ingress.Namespace,
            StatusLabel: status,
            NamespaceLabel: $"Namespace · {ingress.Namespace}",
            SummaryLine: string.IsNullOrWhiteSpace(hostSummary) ? "No rules were reported for this ingress." : hostSummary,
            SecondaryLine: string.IsNullOrWhiteSpace(ingress.IngressClass)
                ? addressSummary
                : $"Class {ingress.IngressClass} · {addressSummary}",
            DetailSubtitle: $"{ingress.Namespace} · ingress",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', ingress.Namespace, ingress.Name, status, ingress.IngressClass, hostSummary, addressSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", status),
                new AksResourceFactItemViewModel("Namespace", ingress.Namespace),
                new AksResourceFactItemViewModel("Ingress class", string.IsNullOrWhiteSpace(ingress.IngressClass) ? "—" : ingress.IngressClass),
                new AksResourceFactItemViewModel("Rules", ingress.Rules.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Addresses", ingress.Addresses.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: highlights,
            CanEditYaml: true,
            CanAnalyzeIngress: true);
    }

    private static AksResourceBrowseItemViewModel CreateGatewayClassBrowseItem(GatewayClassInfo gatewayClass)
    {
        var status = string.IsNullOrWhiteSpace(gatewayClass.Status) ? "Pending" : gatewayClass.Status;
        var controller = string.IsNullOrWhiteSpace(gatewayClass.ControllerName) ? "Controller not reported" : gatewayClass.ControllerName;
        var highlights = BuildDictionaryHighlights("Label", gatewayClass.Labels);
        if (!string.IsNullOrWhiteSpace(gatewayClass.Description))
        {
            highlights.Add($"Description · {gatewayClass.Description}");
        }

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindGatewayClasses,
            ApiKind: "GatewayClass",
            Name: gatewayClass.Name,
            Namespace: string.Empty,
            StatusLabel: status,
            NamespaceLabel: "Cluster scope",
            SummaryLine: controller,
            SecondaryLine: gatewayClass.IsDefault
                ? "Marked as the default gateway class."
                : string.IsNullOrWhiteSpace(gatewayClass.ParametersReference)
                    ? "No parameters reference was surfaced."
                    : $"Parameters {gatewayClass.ParametersReference}",
            DetailSubtitle: "cluster-scoped · gateway class",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', gatewayClass.Name, status, controller, gatewayClass.Description, gatewayClass.ParametersReference),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", status),
                new AksResourceFactItemViewModel("Scope", "Cluster"),
                new AksResourceFactItemViewModel("Controller", controller),
                new AksResourceFactItemViewModel("Default", gatewayClass.IsDefault ? "Yes" : "No"),
                new AksResourceFactItemViewModel("Parameters", gatewayClass.ParametersReference ?? "—"),
            ],
            Highlights: highlights);
    }

    private static AksResourceBrowseItemViewModel CreateGatewayBrowseItem(GatewayInfo gateway)
    {
        var addressSummary = gateway.Addresses.Count == 0
            ? "No gateway addresses were reported yet."
            : string.Join(" · ", gateway.Addresses);
        var classSummary = string.IsNullOrWhiteSpace(gateway.GatewayClassName)
            ? "No gateway class surfaced"
            : $"Class {gateway.GatewayClassName}";
        var highlights = new List<string>();
        highlights.AddRange(gateway.Addresses.Select(address => $"Address · {address}"));
        highlights.AddRange(gateway.Listeners.Select(listener =>
            $"Listener · {listener.Name} {listener.Protocol ?? "?"}/{listener.Port} · Host {listener.Hostname ?? "*"} · Routes {listener.AttachedRoutes}"));
        highlights.AddRange(BuildDictionaryHighlights("Label", gateway.Labels));

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindGateways,
            ApiKind: "Gateway",
            Name: gateway.Name,
            Namespace: gateway.Namespace,
            StatusLabel: string.IsNullOrWhiteSpace(gateway.Status) ? "Pending" : gateway.Status,
            NamespaceLabel: $"Namespace · {gateway.Namespace}",
            SummaryLine: $"Attached routes {gateway.AttachedRoutes}",
            SecondaryLine: $"{classSummary} · {addressSummary}",
            DetailSubtitle: $"{gateway.Namespace} · gateway",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', gateway.Namespace, gateway.Name, gateway.Status, gateway.GatewayClassName, addressSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", gateway.Status),
                new AksResourceFactItemViewModel("Namespace", gateway.Namespace),
                new AksResourceFactItemViewModel("Gateway class", gateway.GatewayClassName ?? "—"),
                new AksResourceFactItemViewModel("Attached routes", gateway.AttachedRoutes.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Addresses", gateway.Addresses.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Listeners", gateway.Listeners.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: highlights);
    }

    private static AksResourceBrowseItemViewModel CreateHttpRouteBrowseItem(HttpRouteInfo httpRoute)
    {
        var hostSummary = httpRoute.Hostnames.Count == 0
            ? "No hostnames were reported for this route."
            : string.Join(", ", httpRoute.Hostnames.Take(3));
        var parentSummary = httpRoute.ParentRefs.Count == 0
            ? "No parent refs were surfaced."
            : string.Join(" · ", httpRoute.ParentRefs.Take(2));
        var highlights = new List<string>();
        highlights.AddRange(httpRoute.Hostnames.Select(hostname => $"Hostname · {hostname}"));
        highlights.AddRange(httpRoute.ParentRefs.Select(parent => $"Parent · {parent}"));
        highlights.AddRange(httpRoute.BackendRefs.Select(backend => $"Backend · {backend}"));
        highlights.AddRange(BuildDictionaryHighlights("Label", httpRoute.Labels));

        return new AksResourceBrowseItemViewModel(
            Kind: ResourceKindHttpRoutes,
            ApiKind: "HTTPRoute",
            Name: httpRoute.Name,
            Namespace: httpRoute.Namespace,
            StatusLabel: string.IsNullOrWhiteSpace(httpRoute.Status) ? "Pending" : httpRoute.Status,
            NamespaceLabel: $"Namespace · {httpRoute.Namespace}",
            SummaryLine: hostSummary,
            SecondaryLine: $"{parentSummary} · Backends {httpRoute.BackendRefs.Count}",
            DetailSubtitle: $"{httpRoute.Namespace} · HTTP route",
            ActionLabel: "Inspect",
            SearchText: string.Join(' ', httpRoute.Namespace, httpRoute.Name, httpRoute.Status, hostSummary, parentSummary),
            DetailFacts:
            [
                new AksResourceFactItemViewModel("Status", httpRoute.Status),
                new AksResourceFactItemViewModel("Namespace", httpRoute.Namespace),
                new AksResourceFactItemViewModel("Hostnames", httpRoute.Hostnames.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Parents", httpRoute.ParentRefs.Count.ToString(CultureInfo.CurrentCulture)),
                new AksResourceFactItemViewModel("Backends", httpRoute.BackendRefs.Count.ToString(CultureInfo.CurrentCulture)),
            ],
            Highlights: highlights);
    }

    private static string BuildSelectorSummary(IReadOnlyDictionary<string, string> labels)
    {
        if (labels.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", labels
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string BuildServicePortsSummary(IReadOnlyList<ServicePortInfo> ports)
    {
        if (ports.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", ports
            .Select(port =>
            {
                var target = string.IsNullOrWhiteSpace(port.TargetPort) ? string.Empty : $" -> {port.TargetPort}";
                return $"{port.Port}/{port.Protocol}{target}";
            }));
    }

    private static string BuildIngressHostSummary(IngressInfo ingress)
    {
        var hosts = ingress.Rules
            .Select(rule => string.IsNullOrWhiteSpace(rule.Host) ? "*" : rule.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return hosts.Count == 0
            ? string.Empty
            : string.Join(", ", hosts);
    }

    private static List<string> BuildDictionaryHighlights(string label, IReadOnlyDictionary<string, string> values)
        => values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{label} · {pair.Key}={pair.Value}")
            .ToList();

    private static IReadOnlyList<string> BuildServiceHighlights(ServiceInfo service)
    {
        var highlights = new List<string>();
        foreach (var port in service.Ports)
        {
            var targetPort = string.IsNullOrWhiteSpace(port.TargetPort) ? "default target" : port.TargetPort;
            highlights.Add($"Port · {port.Port}/{port.Protocol} -> {targetPort}");
        }

        highlights.AddRange(service.ExternalAddresses.Select(address => $"External address · {address}"));
        highlights.AddRange(BuildDictionaryHighlights("Selector", service.SelectorLabels));

        return highlights;
    }

    private static IReadOnlyList<string> BuildIngressHighlights(IngressInfo ingress)
    {
        var highlights = new List<string>();
        foreach (var address in ingress.Addresses)
        {
            highlights.Add($"Address · {address}");
        }

        foreach (var rule in ingress.Rules)
        {
            var host = string.IsNullOrWhiteSpace(rule.Host) ? "*" : rule.Host;
            if (rule.Paths.Count == 0)
            {
                highlights.Add($"Route · {host}");
                continue;
            }

            foreach (var path in rule.Paths)
            {
                var backend = string.IsNullOrWhiteSpace(path.ServiceName)
                    ? "no backend"
                    : path.ServicePort is int servicePort
                        ? $"{path.ServiceName}:{servicePort}"
                        : path.ServiceName;
                highlights.Add($"Route · {host}{path.Path} -> {backend}");
            }
        }

        return highlights;
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
}

public sealed record AksResourceFactItemViewModel(string Label, string Value);

sealed record ResourceScopeLoadResult<T>(IReadOnlyList<T> Items, IReadOnlyList<string> FailedNamespaces);

public sealed class AksResourceBrowseItemViewModel
{
    public AksResourceBrowseItemViewModel(
        string Kind,
        string ApiKind,
        string Name,
        string Namespace,
        string StatusLabel,
        string NamespaceLabel,
        string SummaryLine,
        string SecondaryLine,
        string DetailSubtitle,
        string ActionLabel,
        string SearchText,
        IReadOnlyList<AksResourceFactItemViewModel> DetailFacts,
        IReadOnlyList<string> Highlights,
        bool CanEditYaml = false,
        bool CanAnalyzeIngress = false,
        bool CanAnalyzeNetworkPolicies = false,
        string? NetworkPolicyKind = null,
        bool CanRestart = false,
        int? ScaleReplicaCount = null,
        bool CanTrigger = false,
        bool CanRerun = false,
        AksPodItemViewModel? PodItem = null)
    {
        this.Kind = Kind;
        this.ApiKind = ApiKind;
        this.Name = Name;
        this.Namespace = Namespace;
        this.StatusLabel = StatusLabel;
        this.NamespaceLabel = NamespaceLabel;
        this.SummaryLine = SummaryLine;
        this.SecondaryLine = SecondaryLine;
        this.DetailSubtitle = DetailSubtitle;
        this.ActionLabel = ActionLabel;
        this.SearchText = SearchText;
        this.DetailFacts = DetailFacts;
        this.Highlights = Highlights;
        this.CanEditYaml = CanEditYaml;
        this.CanAnalyzeIngress = CanAnalyzeIngress;
        this.CanAnalyzeNetworkPolicies = CanAnalyzeNetworkPolicies;
        this.NetworkPolicyKind = NetworkPolicyKind;
        this.CanRestart = CanRestart;
        this.ScaleReplicaCount = ScaleReplicaCount;
        this.CanTrigger = CanTrigger;
        this.CanRerun = CanRerun;
        this.PodItem = PodItem;
    }

    public string Kind { get; }

    public string ApiKind { get; }

    public string Name { get; }

    public string Namespace { get; }

    public string StatusLabel { get; }

    public string NamespaceLabel { get; }

    public string SummaryLine { get; }

    public string SecondaryLine { get; }

    public string DetailSubtitle { get; }

    public string ActionLabel { get; }

    public string SearchText { get; }

    public IReadOnlyList<AksResourceFactItemViewModel> DetailFacts { get; }

    public IReadOnlyList<string> Highlights { get; }

    public bool CanEditYaml { get; }

    public bool CanAnalyzeIngress { get; }

    public bool CanAnalyzeNetworkPolicies { get; }

    public string? NetworkPolicyKind { get; }

    public bool CanRestart { get; }

    public int? ScaleReplicaCount { get; }

    public bool CanScale => ScaleReplicaCount is not null;

    public bool CanTrigger { get; }

    public bool CanRerun { get; }

    public AksPodItemViewModel? PodItem { get; }

    public bool Matches(AksResourceBrowseItemViewModel? other)
        => other is not null
           && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
           && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal);
}