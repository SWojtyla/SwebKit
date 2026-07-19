using SwebKit.Core.Models;

namespace SwebKit.App.Components.Pages;

/// <summary>
/// Filter state and filtered collection properties for AksPage.
/// Extracted from AksPage.razor for readability.
/// </summary>
public partial class AksPage
{
    // Filter state (per-tab)
    private string DeploymentFilter = string.Empty;
    private string StatefulSetFilter = string.Empty;
    private string PodFilter = string.Empty;
    private string ServiceFilter = string.Empty;
    private string IngressFilter = string.Empty;
    private string GatewayClassFilter = string.Empty;
    private string GatewayFilter = string.Empty;
    private string HttpRouteFilter = string.Empty;
    private string HelmFilter = string.Empty;
    private string ConfigMapFilter = string.Empty;
    private string SecretFilter = string.Empty;
    private string JobFilter = string.Empty;
    private string CronJobFilter = string.Empty;
    private bool ShowCompletedPods;

    // Cache for filtered collections to avoid recomputation on every render
    private IQueryable<DeploymentInfo>? _filteredDeploymentsCache;
    private IQueryable<StatefulSetInfo>? _filteredStatefulSetsCache;
    private IQueryable<PodInfo>? _filteredPodsCache;
    private string? _lastDeploymentFilter;
    private string? _lastStatefulSetFilter;
    private string? _lastPodFilter;
    private bool _lastShowCompletedPods;
    private List<DeploymentInfo>? _lastDeploymentsSource;
    private List<StatefulSetInfo>? _lastStatefulSetsSource;
    private List<PodInfo>? _lastPodsSource;

    private string ActiveFilter
    {
        get => ActiveResourceType switch
        {
            "Deployments" => DeploymentFilter,
            "StatefulSets" => StatefulSetFilter,
            "Pods" => PodFilter,
            "Services" => ServiceFilter,
            "Ingresses" => IngressFilter,
            "GatewayClasses" => GatewayClassFilter,
            "Gateways" => GatewayFilter,
            "HTTPRoutes" => HttpRouteFilter,
            "Helm" => HelmFilter,
            "ConfigMaps" => ConfigMapFilter,
            "Secrets" => SecretFilter,
            "Jobs" => JobFilter,
            "CronJobs" => CronJobFilter,
            _ => string.Empty
        };
        set
        {
            switch (ActiveResourceType)
            {
                case "Deployments": DeploymentFilter = value; break;
                case "StatefulSets": StatefulSetFilter = value; break;
                case "Pods": PodFilter = value; break;
                case "Services": ServiceFilter = value; break;
                case "Ingresses": IngressFilter = value; break;
                case "GatewayClasses": GatewayClassFilter = value; break;
                case "Gateways": GatewayFilter = value; break;
                case "HTTPRoutes": HttpRouteFilter = value; break;
                case "Helm": HelmFilter = value; break;
                case "ConfigMaps": ConfigMapFilter = value; break;
                case "Secrets": SecretFilter = value; break;
                case "Jobs": JobFilter = value; break;
                case "CronJobs": CronJobFilter = value; break;
            }
        }
    }

    private IQueryable<DeploymentInfo> FilteredDeployments
    {
        get
        {
            if (_filteredDeploymentsCache is null ||
                _lastDeploymentFilter != DeploymentFilter ||
                !ReferenceEquals(_lastDeploymentsSource, Deployments))
            {
                _lastDeploymentFilter = DeploymentFilter;
                _lastDeploymentsSource = Deployments;
                _filteredDeploymentsCache = string.IsNullOrWhiteSpace(DeploymentFilter)
                    ? Deployments.AsQueryable()
                    : Deployments.Where(d => d.Name.Contains(DeploymentFilter, StringComparison.OrdinalIgnoreCase)
                        || d.Status.Contains(DeploymentFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();
            }
            return _filteredDeploymentsCache;
        }
    }

    private IQueryable<StatefulSetInfo> FilteredStatefulSets
    {
        get
        {
            if (_filteredStatefulSetsCache is null ||
                _lastStatefulSetFilter != StatefulSetFilter ||
                !ReferenceEquals(_lastStatefulSetsSource, StatefulSets))
            {
                _lastStatefulSetFilter = StatefulSetFilter;
                _lastStatefulSetsSource = StatefulSets;
                _filteredStatefulSetsCache = string.IsNullOrWhiteSpace(StatefulSetFilter)
                    ? StatefulSets.AsQueryable()
                    : StatefulSets.Where(s => s.Name.Contains(StatefulSetFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();
            }
            return _filteredStatefulSetsCache;
        }
    }

    private IQueryable<PodInfo> FilteredPods
    {
        get
        {
            if (_filteredPodsCache is null ||
                _lastPodFilter != PodFilter ||
                _lastShowCompletedPods != ShowCompletedPods ||
                !ReferenceEquals(_lastPodsSource, Pods))
            {
                _lastPodFilter = PodFilter;
                _lastShowCompletedPods = ShowCompletedPods;
                _lastPodsSource = Pods;
                _filteredPodsCache = Pods
                    .Where(p => ShowCompletedPods || !IsCompletedPod(p))
                    .Where(p => string.IsNullOrWhiteSpace(PodFilter)
                        || p.Name.Contains(PodFilter, StringComparison.OrdinalIgnoreCase)
                        || p.Status.Contains(PodFilter, StringComparison.OrdinalIgnoreCase)
                        || p.Phase.Contains(PodFilter, StringComparison.OrdinalIgnoreCase)
                        || (p.NodeName?.Contains(PodFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                    .AsQueryable();
            }
            return _filteredPodsCache;
        }
    }

    private int HiddenCompletedPodCount => Pods.Count(IsCompletedPod);

    private static bool IsCompletedPod(PodInfo pod)
        => pod.Status is "Completed" or "Succeeded" || pod.Phase is "Succeeded";

    private IQueryable<ServiceInfo> FilteredServices => string.IsNullOrWhiteSpace(ServiceFilter)
    ? Services.AsQueryable()
    : Services.Where(service => service.Name.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase)
    || service.Type.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase)
    || service.ClusterIp.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase)
    || service.ExternalAddresses.Any(address => address.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase))
    || service.Ports.Any(port => port.Port.ToString().Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase)
        || (port.TargetPort?.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase) ?? false)
        || (port.Name?.Contains(ServiceFilter, StringComparison.OrdinalIgnoreCase) ?? false))).AsQueryable();

    private IQueryable<IngressInfo> FilteredIngresses => string.IsNullOrWhiteSpace(IngressFilter)
    ? Ingresses.AsQueryable()
    : Ingresses.Where(i => i.Name.Contains(IngressFilter, StringComparison.OrdinalIgnoreCase)
    || i.Rules.Any(r => r.Host?.Contains(IngressFilter, StringComparison.OrdinalIgnoreCase) ?? false)).AsQueryable();

    private IQueryable<GatewayClassInfo> FilteredGatewayClasses => string.IsNullOrWhiteSpace(GatewayClassFilter)
    ? GatewayClasses.AsQueryable()
    : GatewayClasses.Where(gatewayClass => gatewayClass.Name.Contains(GatewayClassFilter,
StringComparison.OrdinalIgnoreCase)
    || (gatewayClass.ControllerName?.Contains(GatewayClassFilter, StringComparison.OrdinalIgnoreCase) ?? false)
    || (gatewayClass.ParametersReference?.Contains(GatewayClassFilter, StringComparison.OrdinalIgnoreCase) ?? false)
    || (gatewayClass.Description?.Contains(GatewayClassFilter, StringComparison.OrdinalIgnoreCase) ?? false)
    || gatewayClass.Status.Contains(GatewayClassFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();

    private IQueryable<GatewayInfo> FilteredGateways => string.IsNullOrWhiteSpace(GatewayFilter)
    ? Gateways.AsQueryable()
    : Gateways.Where(gateway => gateway.Name.Contains(GatewayFilter, StringComparison.OrdinalIgnoreCase)
    || (gateway.GatewayClassName?.Contains(GatewayFilter, StringComparison.OrdinalIgnoreCase) ?? false)
    || gateway.Addresses.Any(address => address.Contains(GatewayFilter, StringComparison.OrdinalIgnoreCase))
    || gateway.Listeners.Any(listener => (listener.Hostname?.Contains(GatewayFilter, StringComparison.OrdinalIgnoreCase) ??
false)
        || (listener.Protocol?.Contains(GatewayFilter, StringComparison.OrdinalIgnoreCase) ?? false))).AsQueryable();

    private IQueryable<HttpRouteInfo> FilteredHttpRoutes => string.IsNullOrWhiteSpace(HttpRouteFilter)
    ? HttpRoutes.AsQueryable()
    : HttpRoutes.Where(route => route.Name.Contains(HttpRouteFilter, StringComparison.OrdinalIgnoreCase)
    || route.Hostnames.Any(host => host.Contains(HttpRouteFilter, StringComparison.OrdinalIgnoreCase))
    || route.ParentRefs.Any(parent => parent.Contains(HttpRouteFilter, StringComparison.OrdinalIgnoreCase))
    || route.BackendRefs.Any(backend => backend.Contains(HttpRouteFilter, StringComparison.OrdinalIgnoreCase))
    || route.Status.Contains(HttpRouteFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();

    private IQueryable<HelmReleaseInfo> FilteredHelmReleases => string.IsNullOrWhiteSpace(HelmFilter)
    ? HelmReleases.AsQueryable()
    : HelmReleases.Where(h => h.Name.Contains(HelmFilter, StringComparison.OrdinalIgnoreCase)
    || h.Status.Contains(HelmFilter, StringComparison.OrdinalIgnoreCase)
    || (h.Chart?.Contains(HelmFilter, StringComparison.OrdinalIgnoreCase) ?? false)).AsQueryable();

    private IQueryable<ConfigMapInfo> FilteredConfigMaps => string.IsNullOrWhiteSpace(ConfigMapFilter)
    ? ConfigMaps.AsQueryable()
    : ConfigMaps.Where(cm => cm.Name.Contains(ConfigMapFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();

    private IQueryable<SecretInfo> FilteredSecrets => string.IsNullOrWhiteSpace(SecretFilter)
    ? Secrets.AsQueryable()
    : Secrets.Where(s => s.Name.Contains(SecretFilter, StringComparison.OrdinalIgnoreCase)
    || s.Type.Contains(SecretFilter, StringComparison.OrdinalIgnoreCase)).AsQueryable();

    private IQueryable<JobInfo> FilteredJobs => string.IsNullOrWhiteSpace(JobFilter)
    ? Jobs.AsQueryable()
    : Jobs.Where(job => job.Name.Contains(JobFilter, StringComparison.OrdinalIgnoreCase)
    || job.Status.Contains(JobFilter, StringComparison.OrdinalIgnoreCase)
    || (job.SourceKind?.Contains(JobFilter, StringComparison.OrdinalIgnoreCase) ?? false)
    || (job.SourceName?.Contains(JobFilter, StringComparison.OrdinalIgnoreCase) ?? false)).AsQueryable();

    private IQueryable<CronJobInfo> FilteredCronJobs => string.IsNullOrWhiteSpace(CronJobFilter)
    ? CronJobs.AsQueryable()
    : CronJobs.Where(c => c.Name.Contains(CronJobFilter, StringComparison.OrdinalIgnoreCase)
    || (c.Schedule?.Contains(CronJobFilter, StringComparison.OrdinalIgnoreCase) ?? false)).AsQueryable();
}
