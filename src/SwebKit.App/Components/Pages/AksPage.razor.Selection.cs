using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using SwebKit.App.Services;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Components.Pages;

/// <summary>
/// Selection, navigation, keyboard handlers, command registration,
/// and workspace snapshot logic for AksPage.
/// Extracted from AksPage.razor for readability.
/// </summary>
public partial class AksPage
{
    // ── Selection ──

    private void SelectDeployment(DeploymentInfo deployment)
    {
        SelectedDeployment = deployment;
        _detailPanels?.CloseContainerDetail();
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectStatefulSet(StatefulSetInfo statefulSet)
    {
        SelectedStatefulSet = statefulSet;
        _detailPanels?.CloseContainerDetail();
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectConfigMap(ConfigMapInfo configMap)
    {
        SelectedConfigMap = configMap;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectSecret(SecretInfo secret)
    {
        SelectedSecret = secret;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectPod(PodInfo pod)
    {
        SelectedPod = pod;
        _detailPanels?.SwitchOrCloseContainerDetail(pod.Name, pod.Namespace);
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectService(ServiceInfo service)
    {
        SelectedService = service;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectIngress(IngressInfo ingress)
    {
        SelectedIngress = ingress;
        PushAksSelection();
        StateHasChanged();
    }

    private void OpenDeploymentNetworkAnalysis(DeploymentInfo deployment)
    {
        SelectDeployment(deployment);
        _detailPanels.ShowNetworkPolicyAnalysis("Deployment", deployment.Name, deployment.Namespace);
    }

    private void OpenStatefulSetNetworkAnalysis(StatefulSetInfo statefulSet)
    {
        SelectStatefulSet(statefulSet);
        _detailPanels.ShowNetworkPolicyAnalysis("StatefulSet", statefulSet.Name, statefulSet.Namespace);
    }

    private void OpenPodNetworkAnalysis(PodInfo pod)
    {
        SelectPod(pod);
        _detailPanels.ShowNetworkPolicyAnalysis("Pod", pod.Name, pod.Namespace);
    }

    private void OpenIngressAnalysis(IngressInfo ingress)
    {
        SelectIngress(ingress);
        _detailPanels.ShowIngressAnalysis(ingress.Name, ingress.Namespace);
    }

    private void SelectGatewayClass(GatewayClassInfo gatewayClass)
    {
        SelectedGatewayClass = gatewayClass;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectGateway(GatewayInfo gateway)
    {
        SelectedGateway = gateway;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectHttpRoute(HttpRouteInfo httpRoute)
    {
        SelectedHttpRoute = httpRoute;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectHelmRelease(HelmReleaseInfo helmRelease)
    {
        SelectedHelmRelease = helmRelease;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectJob(JobInfo job)
    {
        SelectedJob = job;
        PushAksSelection();
        StateHasChanged();
    }

    private void SelectCronJob(CronJobInfo cronJob)
    {
        SelectedCronJob = cronJob;
        PushAksSelection();
        StateHasChanged();
    }

    // ── Workspace snapshot ──

    private async Task PublishWorkspaceSnapshotAsync(bool recordRecent)
    {
        var snapshot = BuildWorkspaceSnapshot();
        if (snapshot is null)
        {
            Workspaces.ClearCurrentSnapshot("aks");
            return;
        }

        await Workspaces.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private WorkspaceSnapshot? BuildWorkspaceSnapshot()
    {
        var resourceName = GetSelectedResourceName();
        var resourceKind = GetSelectedResourceKind();
        var displayName = resourceName ?? (string.IsNullOrWhiteSpace(ActiveContext) ? "AKS" : ActiveContext);
        var hasExplicitNamespace = resourceName?.Contains('/', StringComparison.Ordinal) == true;
        var displayPath = resourceName is null
            ? displayName
            : resourceKind == "gatewayclass" || hasExplicitNamespace
                ? resourceName
                : $"{CurrentNamespace}/{resourceName}";
        var key = resourceName is null
            ? $"aks:cluster:{ActiveContext}:{CurrentNamespace}"
            : resourceKind == "gatewayclass" || hasExplicitNamespace
                ? $"aks:{resourceKind}:{resourceName}"
                : $"aks:{resourceKind}:{CurrentNamespace}:{resourceName}";

        return new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = key,
                Area = "aks",
                Kind = resourceKind,
                DisplayName = displayName,
                DisplayPath = displayPath,
                Summary = string.IsNullOrWhiteSpace(ActiveContext)
                    ? ActiveResourceType
                    : $"{ActiveContext} | {ActiveResourceType}",
                Icon = "☸",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = ActiveContext,
                    ["namespace"] = CurrentNamespace,
                },
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["context"] = ActiveContext,
                ["namespace"] = CurrentNamespace,
                ["resourceType"] = ActiveResourceType,
                ["filter"] = ActiveFilter,
                ["showCompletedPods"] = ShowCompletedPods.ToString(),
                ["showEvents"] = ShowEvents.ToString(),
                ["showPortForwardSessions"] = ShowPortForwardSessions.ToString(),
            },
        };
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        if (snapshot.RestoreState.TryGetValue("context", out var restoredContext))
        {
            ActiveContext = restoredContext;
        }

        if (snapshot.RestoreState.TryGetValue("namespace", out var restoredNamespace))
        {
            CurrentNamespace = restoredNamespace;
        }

        await BootstrapAndLoadAsync(ActiveContext, CurrentNamespace);

        if (snapshot.RestoreState.TryGetValue("resourceType", out var restoredResourceType)
            && !string.IsNullOrWhiteSpace(restoredResourceType))
        {
            ActiveResourceType = restoredResourceType;
        }

        ActiveFilter = snapshot.RestoreState.TryGetValue("filter", out var restoredFilter)
            ? restoredFilter
            : string.Empty;

        ShowCompletedPods = snapshot.RestoreState.TryGetValue("showCompletedPods", out var showCompletedPodsText)
            && bool.TryParse(showCompletedPodsText, out var restoredShowCompletedPods)
            && restoredShowCompletedPods;

        if (snapshot.RestoreState.TryGetValue("showEvents", out var showEventsText)
            && bool.TryParse(showEventsText, out var restoredShowEvents))
        {
            ShowEvents = restoredShowEvents;
        }

        if (snapshot.RestoreState.TryGetValue("showPortForwardSessions", out var showPortForwardText)
            && bool.TryParse(showPortForwardText, out var restoredShowPortForwardSessions))
        {
            ShowPortForwardSessions = restoredShowPortForwardSessions;
        }

        if (snapshot.Resource.Kind != "cluster")
        {
            _suppressWorkspaceRecent = true;
            ApplySelectedResource(snapshot.Resource.Kind, snapshot.Resource.DisplayName);
            _suppressWorkspaceRecent = false;
        }

        await PublishWorkspaceSnapshotAsync(recordRecent: false);
        await InvokeAsync(StateHasChanged);
    }

    private string? GetSelectedResourceName() => ActiveResourceType switch
    {
        "Deployments" => SelectedDeployment is null ? null : GetNamespaceScopedIdentity(SelectedDeployment.Namespace,
SelectedDeployment.Name),
        "Pods" => SelectedPod is null ? null : GetNamespaceScopedIdentity(SelectedPod.Namespace, SelectedPod.Name),
        "StatefulSets" => SelectedStatefulSet is null ? null : GetNamespaceScopedIdentity(SelectedStatefulSet.Namespace,
SelectedStatefulSet.Name),
        "ConfigMaps" => SelectedConfigMap is null ? null : GetNamespaceScopedIdentity(SelectedConfigMap.Namespace,
SelectedConfigMap.Name),
        "Secrets" => SelectedSecret is null ? null : GetNamespaceScopedIdentity(SelectedSecret.Namespace, SelectedSecret.Name),
        "Services" => SelectedService is null ? null : GetServiceIdentity(SelectedService),
        "Ingresses" => SelectedIngress is null ? null : GetIngressIdentity(SelectedIngress),
        "GatewayClasses" => SelectedGatewayClass is null ? null : GetGatewayClassIdentity(SelectedGatewayClass),
        "Gateways" => SelectedGateway is null ? null : GetGatewayIdentity(SelectedGateway),
        "HTTPRoutes" => SelectedHttpRoute is null ? null : GetHttpRouteIdentity(SelectedHttpRoute),
        "Helm" => SelectedHelmRelease is null ? null : GetNamespaceScopedIdentity(SelectedHelmRelease.Namespace,
SelectedHelmRelease.Name),
        "Jobs" => SelectedJob is null ? null : GetNamespaceScopedIdentity(SelectedJob.Namespace, SelectedJob.Name),
        "CronJobs" => SelectedCronJob is null ? null : GetNamespaceScopedIdentity(SelectedCronJob.Namespace,
SelectedCronJob.Name),
        _ => null,
    };

    private string GetSelectedResourceKind() => ActiveResourceType switch
    {
        "Deployments" => "deployment",
        "Pods" => "pod",
        "StatefulSets" => "statefulset",
        "ConfigMaps" => "configmap",
        "Secrets" => "secret",
        "Services" => "service",
        "Ingresses" => "ingress",
        "GatewayClasses" => "gatewayclass",
        "Gateways" => "gateway",
        "HTTPRoutes" => "httproute",
        "Helm" => "helm",
        "Jobs" => "job",
        "CronJobs" => "cronjob",
        _ => "cluster",
    };

    private void ApplySelectedResource(string resourceKind, string resourceName)
    {
        switch (resourceKind)
        {
            case "deployment":
                SelectedDeployment = Deployments.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace,
item.Name), resourceName,
StringComparison.Ordinal))
                    ?? Deployments.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "pod":
                SelectedPod = Pods.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace, item.Name),
resourceName,
StringComparison.Ordinal))
                    ?? Pods.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "statefulset":
                SelectedStatefulSet = StatefulSets.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace,
item.Name), resourceName,
StringComparison.Ordinal))
                    ?? StatefulSets.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "configmap":
                SelectedConfigMap = ConfigMaps.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace,
item.Name), resourceName,
StringComparison.Ordinal))
                    ?? ConfigMaps.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "secret":
                SelectedSecret = Secrets.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace, item.Name),
resourceName,
StringComparison.Ordinal))
                    ?? Secrets.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "service":
                SelectedService = Services.FirstOrDefault(item => string.Equals(GetServiceIdentity(item), resourceName,
StringComparison.Ordinal))
                    ?? Services.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "ingress":
                SelectedIngress = Ingresses.FirstOrDefault(item => string.Equals(GetIngressIdentity(item), resourceName,
StringComparison.Ordinal))
                    ?? Ingresses.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "gatewayclass":
                SelectedGatewayClass = GatewayClasses.FirstOrDefault(item => string.Equals(GetGatewayClassIdentity(item), resourceName,
StringComparison.Ordinal))
                    ?? GatewayClasses.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "gateway":
                SelectedGateway = Gateways.FirstOrDefault(item => string.Equals(GetGatewayIdentity(item), resourceName,
StringComparison.Ordinal))
                    ?? Gateways.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "httproute":
                SelectedHttpRoute = HttpRoutes.FirstOrDefault(item => string.Equals(GetHttpRouteIdentity(item), resourceName,
StringComparison.Ordinal))
                    ?? HttpRoutes.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "helm":
                SelectedHelmRelease = HelmReleases.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace,
item.Name), resourceName,
StringComparison.Ordinal))
                    ?? HelmReleases.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "job":
                SelectedJob = Jobs.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace, item.Name),
resourceName,
StringComparison.Ordinal))
                    ?? Jobs.FirstOrDefault(item => item.Name == resourceName);
                break;
            case "cronjob":
                SelectedCronJob = CronJobs.FirstOrDefault(item => string.Equals(GetNamespaceScopedIdentity(item.Namespace, item.Name),
resourceName,
StringComparison.Ordinal))
                    ?? CronJobs.FirstOrDefault(item => item.Name == resourceName);
                break;
            default:
                ClearSelection();
                return;
        }

        PushAksSelection();
    }

    private void HandleToggleEvents(bool value)
    {
        ShowEvents = value;
        StateHasChanged();
    }

    private void HandleTogglePortForwardSessions(bool value)
    {
        ShowPortForwardSessions = value;
        StateHasChanged();
    }

    private void SwitchResourceType(string type)
    {
        ActiveResourceType = type;
        ClearSelection();
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private void HandleJumpToResource(KubernetesEvent evt)
    {
        var targetResourceType = evt.InvolvedObjectKind switch
        {
            "Deployment" => "Deployments",
            "StatefulSet" => "StatefulSets",
            "Pod" => "Pods",
            "CronJob" => "CronJobs",
            "Job" => "Jobs",
            _ => null
        };
        if (targetResourceType is null) return;
        SwitchResourceType(targetResourceType);
        ActiveFilter = evt.InvolvedObjectName ?? string.Empty;
    }

    private void OnFilterChanged(string value)
    {
        ActiveFilter = value;
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        StateHasChanged();
    }

    private void OnShowCompletedPodsChanged(ChangeEventArgs e)
    {
        ShowCompletedPods = e.Value is bool value && value;
        if (!ShowCompletedPods && SelectedPod is not null && IsCompletedPod(SelectedPod))
        {
            SelectedPod = null;
            Selection.SetSelection("aks", null);
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        StateHasChanged();
    }

    // ── Grid keyboard navigation ──

    private void HandleGridKeyDown(KeyboardEventArgs e)
    {
        var actionKeys = new[] { "l", "y", "r", "s", "p", "d", "h", "v", "n", "i", "Enter", "/" };
        _preventGridKey = e.Key is "ArrowUp" or "ArrowDown" or "Escape" || actionKeys.Contains(e.Key);

        switch (e.Key)
        {
            case "ArrowDown": SelectRelative(+1); return;
            case "ArrowUp": SelectRelative(-1); return;
            case "Escape": ClearSelection(); return;
            case "/": _ = FocusFilterAsync(); return;
        }

        // HandleLetterActionAsync (e.g. "y" → OpenYamlAsync) mutates state on child components
        // (AksDetailPanels) that no longer self-render. Because this handler is a fire-and-forget
        // task detached from Blazor's event dispatch, it must trigger its own render on completion —
        // otherwise the requested panel silently never appears.
        _ = HandleLetterActionAndRenderAsync(e.Key);
    }

    private async Task HandleLetterActionAndRenderAsync(string key)
    {
        await HandleLetterActionAsync(key);
        await InvokeAsync(StateHasChanged);
    }

    private static T? NavigateInList<T>(IEnumerable<T> items, T? current, int delta, Func<T, string> getName) where T :
class
    {
        var list = items.ToList();
        if (list.Count == 0) return current;
        var idx = current is null ? -1 : list.FindIndex(item => getName(item) == getName(current));
        return list[Math.Clamp(idx + delta, 0, list.Count - 1)];
    }

    private void SelectRelative(int delta)
    {
        switch (ActiveResourceType)
        {
            case "Deployments":
                SelectedDeployment = NavigateInList(FilteredDeployments, SelectedDeployment, delta, d =>
GetNamespaceScopedIdentity(d.Namespace, d.Name));
                break;
            case "Pods":
                SelectedPod = NavigateInList(FilteredPods, SelectedPod, delta, p => GetNamespaceScopedIdentity(p.Namespace,
p.Name)); break;
            case "StatefulSets":
                SelectedStatefulSet = NavigateInList(FilteredStatefulSets, SelectedStatefulSet, delta, s =>
GetNamespaceScopedIdentity(s.Namespace, s.Name)); break;
            case "ConfigMaps":
                SelectedConfigMap = NavigateInList(FilteredConfigMaps, SelectedConfigMap, delta, cm =>
GetNamespaceScopedIdentity(cm.Namespace, cm.Name));
                break;
            case "Secrets":
                SelectedSecret = NavigateInList(FilteredSecrets, SelectedSecret, delta, s =>
GetNamespaceScopedIdentity(s.Namespace, s.Name)); break;
            case "Services":
                SelectedService = NavigateInList(FilteredServices, SelectedService, delta, GetServiceIdentity);
                break;
            case "Ingresses":
                SelectedIngress = NavigateInList(FilteredIngresses, SelectedIngress, delta, GetIngressIdentity);
                break;
            case "GatewayClasses":
                SelectedGatewayClass = NavigateInList(FilteredGatewayClasses, SelectedGatewayClass, delta, GetGatewayClassIdentity);
                break;
            case "Gateways":
                SelectedGateway = NavigateInList(FilteredGateways, SelectedGateway, delta, GetGatewayIdentity);
                break;
            case "HTTPRoutes":
                SelectedHttpRoute = NavigateInList(FilteredHttpRoutes, SelectedHttpRoute, delta, GetHttpRouteIdentity);
                break;
            case "Helm":
                SelectedHelmRelease = NavigateInList(FilteredHelmReleases, SelectedHelmRelease, delta, h =>
GetNamespaceScopedIdentity(h.Namespace, h.Name)); break;
            case "Jobs":
                SelectedJob = NavigateInList(FilteredJobs, SelectedJob, delta, job => GetNamespaceScopedIdentity(job.Namespace,
job.Name));
                break;
            case "CronJobs":
                SelectedCronJob = NavigateInList(FilteredCronJobs, SelectedCronJob, delta, cj =>
GetNamespaceScopedIdentity(cj.Namespace, cj.Name)); break;
        }
        PushAksSelection();
        StateHasChanged();
    }

    private void PushAksSelection()
    {
        object? selected = ActiveResourceType switch
        {
            "Deployments" => SelectedDeployment,
            "Pods" => SelectedPod,
            "StatefulSets" => SelectedStatefulSet,
            "ConfigMaps" => SelectedConfigMap,
            "Secrets" => SelectedSecret,
            "Services" => SelectedService,
            "Ingresses" => SelectedIngress,
            "GatewayClasses" => SelectedGatewayClass,
            "Gateways" => SelectedGateway,
            "HTTPRoutes" => SelectedHttpRoute,
            "Helm" => SelectedHelmRelease,
            "Jobs" => SelectedJob,
            "CronJobs" => SelectedCronJob,
            _ => null
        };
        Selection.SetSelection("aks", selected);
        _ = PublishWorkspaceSnapshotAsync(recordRecent: !_suppressWorkspaceRecent && selected is not null);
    }

    private void ClearSelection()
    {
        SelectedDeployment = null;
        SelectedPod = null;
        SelectedStatefulSet = null;
        SelectedConfigMap = null;
        SelectedSecret = null;
        SelectedService = null;
        SelectedIngress = null;
        SelectedGatewayClass = null;
        SelectedGateway = null;
        SelectedHttpRoute = null;
        SelectedHelmRelease = null;
        SelectedJob = null;
        SelectedCronJob = null;
        Selection.SetSelection("aks", null);
        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
        StateHasChanged();
    }

    private async Task FocusFilterAsync()
    {
        await JS.InvokeVoidAsync("SwebKit.focusAksFilter");
    }

    private async Task HandleLetterActionAsync(string key)
    {
        switch (ActiveResourceType)
        {
            case "Deployments" when SelectedDeployment is not null:
                switch (key)
                {
                    case "l": CtxDeployment = SelectedDeployment; OnCtxViewDeploymentLogs(); break;
                    case "n": CtxDeployment = SelectedDeployment; OnCtxAnalyzeDeploymentNetwork(); break;
                    case "y":
                        await _detailPanels.OpenYamlAsync("Deployment", SelectedDeployment.Name, targetNamespace:
SelectedDeployment.Namespace); break;
                    case "r": CtxDeployment = SelectedDeployment; await OnCtxRestartDeployment(); break;
                    case "Enter": CtxDeployment = SelectedDeployment; OnCtxViewDeploymentLogs(); break;
                }
                break;

            case "Pods" when SelectedPod is not null:
                switch (key)
                {
                    case "l": CtxPod = SelectedPod; OnCtxViewPodLogs(); break;
                    case "n": CtxPod = SelectedPod; OnCtxAnalyzePodNetwork(); break;
                    case "y": await _detailPanels.OpenYamlAsync("Pod", SelectedPod.Name, targetNamespace: SelectedPod.Namespace); break;
                    case "s": CtxPod = SelectedPod; await OnCtxOpenPodShell(); break;
                    case "p": CtxPod = SelectedPod; OnCtxPortForward(); break;
                    case "d": CtxPod = SelectedPod; await OnCtxKillPod(); break;
                    case "Enter": CtxPod = SelectedPod; OnCtxViewPodLogs(); break;
                }
                break;

            case "StatefulSets" when SelectedStatefulSet is not null:
                switch (key)
                {
                    case "l": CtxStatefulSet = SelectedStatefulSet; OnCtxAllPodsLogsStatefulSet(); break;
                    case "n": CtxStatefulSet = SelectedStatefulSet; OnCtxAnalyzeStatefulSetNetwork(); break;
                    case "y":
                        await _detailPanels.OpenYamlAsync("StatefulSet", SelectedStatefulSet.Name, targetNamespace:
SelectedStatefulSet.Namespace); break;
                    case "r": CtxStatefulSet = SelectedStatefulSet; await OnCtxRestartStatefulSet(); break;
                    case "Enter": CtxStatefulSet = SelectedStatefulSet; OnCtxAllPodsLogsStatefulSet(); break;
                }
                break;

            case "ConfigMaps" when SelectedConfigMap is not null:
                switch (key)
                {
                    case "y":
                        await _detailPanels.OpenYamlAsync("ConfigMap", SelectedConfigMap.Name, targetNamespace:
SelectedConfigMap.Namespace); break;
                    case "Enter": CtxConfigMap = SelectedConfigMap; OnCtxViewConfigMapKeys(); break;
                }
                break;

            case "Secrets" when SelectedSecret is not null:
                switch (key)
                {
                    case "y":
                        await _detailPanels.OpenYamlAsync("Secret", SelectedSecret.Name, targetNamespace: SelectedSecret.Namespace);
                        break;
                    case "Enter": CtxSecret = SelectedSecret; OnCtxViewSecretKeys(); break;
                }
                break;

            case "Services" when SelectedService is not null:
                switch (key)
                {
                    case "y": CtxService = SelectedService; await OnCtxViewYamlService(); break;
                    case "Enter": CtxService = SelectedService; await OnCtxViewYamlService(); break;
                }
                break;

            case "Ingresses" when SelectedIngress is not null:
                switch (key)
                {
                    case "i": CtxIngress = SelectedIngress; OnCtxAnalyzeIngress(); break;
                    case "y":
                        await _detailPanels.OpenYamlAsync("Ingress", SelectedIngress.Name, targetNamespace:
SelectedIngress.Namespace); break;
                    case "Enter": CtxIngress = SelectedIngress; OnCtxOpenIngressUrl(); break;
                }
                break;

            case "GatewayClasses" when SelectedGatewayClass is not null:
                switch (key)
                {
                    case "y": CtxGatewayClass = SelectedGatewayClass; await OnCtxViewYamlGatewayClass(); break;
                    case "Enter": CtxGatewayClass = SelectedGatewayClass; await OnCtxViewYamlGatewayClass(); break;
                }
                break;

            case "Gateways" when SelectedGateway is not null:
                switch (key)
                {
                    case "y": CtxGateway = SelectedGateway; await OnCtxViewYamlGateway(); break;
                    case "Enter": CtxGateway = SelectedGateway; await OnCtxViewYamlGateway(); break;
                }
                break;

            case "HTTPRoutes" when SelectedHttpRoute is not null:
                switch (key)
                {
                    case "y": CtxHttpRoute = SelectedHttpRoute; await OnCtxViewYamlHttpRoute(); break;
                    case "Enter": CtxHttpRoute = SelectedHttpRoute; await OnCtxViewYamlHttpRoute(); break;
                }
                break;

            case "Helm" when SelectedHelmRelease is not null:
                switch (key)
                {
                    case "y": CtxHelm = SelectedHelmRelease; await OnCtxViewYaml('H'); break;
                    case "h": CtxHelm = SelectedHelmRelease; await OnCtxViewHelmHistory(); break;
                    case "v": CtxHelm = SelectedHelmRelease; await OnCtxViewHelmValues(); break;
                    case "Enter": CtxHelm = SelectedHelmRelease; await OnCtxViewHelmHistory(); break;
                }
                break;

            case "Jobs" when SelectedJob is not null:
                switch (key)
                {
                    case "y": CtxJob = SelectedJob; await OnCtxViewYamlJob(); break;
                    case "Enter": CtxJob = SelectedJob; await OnCtxViewYamlJob(); break;
                }
                break;

            case "CronJobs" when SelectedCronJob is not null:
                switch (key)
                {
                    case "y": CtxCronJob = SelectedCronJob; await OnCtxViewYamlCronJob(); break;
                    case "Enter": CtxCronJob = SelectedCronJob; await OnCtxViewYamlCronJob(); break;
                }
                break;
        }
    }
}
