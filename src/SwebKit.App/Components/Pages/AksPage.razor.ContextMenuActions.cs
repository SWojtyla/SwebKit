using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SwebKit.Core.Models;

namespace SwebKit.App.Components.Pages;

/// <summary>
/// Context menu show helpers and action handlers for AksPage.
/// Extracted from AksPage.razor for readability.
/// </summary>
public partial class AksPage
{
    // ── Context menu show helpers ──

    private static void OnTableContextMenu(MouseEventArgs e)
    {
        // Suppress browser context menu on the table area (handled per-row below)
    }

    private void ShowDeploymentMenu(MouseEventArgs e, DeploymentInfo d)
    {
        CtxDeployment = d;
        DeploymentMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowPodMenu(MouseEventArgs e, PodInfo p)
    {
        CtxPod = p;
        PodMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowServiceMenu(MouseEventArgs e, ServiceInfo service)
    {
        CtxService = service;
        ServiceMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowIngressMenu(MouseEventArgs e, IngressInfo i)
    {
        CtxIngress = i;
        IngressMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowGatewayClassMenu(MouseEventArgs e, GatewayClassInfo gatewayClass)
    {
        CtxGatewayClass = gatewayClass;
        GatewayClassMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowGatewayMenu(MouseEventArgs e, GatewayInfo gateway)
    {
        CtxGateway = gateway;
        GatewayMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowHttpRouteMenu(MouseEventArgs e, HttpRouteInfo httpRoute)
    {
        CtxHttpRoute = httpRoute;
        HttpRouteMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowHelmMenu(MouseEventArgs e, HelmReleaseInfo h)
    {
        CtxHelm = h;
        HelmMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowStatefulSetMenu(MouseEventArgs e, StatefulSetInfo s)
    {
        CtxStatefulSet = s;
        StatefulSetMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowConfigMapMenu(MouseEventArgs e, ConfigMapInfo cm)
    {
        CtxConfigMap = cm;
        ConfigMapMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowSecretMenu(MouseEventArgs e, SecretInfo s)
    {
        CtxSecret = s;
        SecretMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowJobMenu(MouseEventArgs e, JobInfo job)
    {
        CtxJob = job;
        JobMenu.Show(e.ClientX, e.ClientY);
    }

    private void ShowCronJobMenu(MouseEventArgs e, CronJobInfo cj)
    {
        CtxCronJob = cj;
        CronJobMenu.Show(e.ClientX, e.ClientY);
    }

    // ── Context menu actions ──

    private async Task OnCtxViewYaml(char source)
    {
        var (kind, name) = source switch
        {
            'D' => ("Deployment", CtxDeployment?.Name),
            'P' => ("Pod", CtxPod?.Name),
            'I' => ("Ingress", CtxIngress?.Name),
            'H' => ("Helm", CtxHelm?.Name),
            'S' => ("StatefulSet", CtxStatefulSet?.Name),
            'C' => ("ConfigMap", CtxConfigMap?.Name),
            'E' => ("Secret", CtxSecret?.Name),
            'J' => ("CronJob", CtxCronJob?.Name),
            _ => ((string?)null, (string?)null)
        };
        CloseAllMenus();
        if (kind is not null && name is not null)
            await _detailPanels.OpenYamlAsync(kind, name, targetNamespace: GetContextTargetNamespace(source));
    }

    private string? GetContextTargetNamespace(char source) => source switch
    {
        'D' => CtxDeployment?.Namespace,
        'P' => CtxPod?.Namespace,
        'I' => CtxIngress?.Namespace,
        'H' => CtxHelm?.Namespace,
        'S' => CtxStatefulSet?.Namespace,
        'C' => CtxConfigMap?.Namespace,
        'E' => CtxSecret?.Namespace,
        'J' => CtxCronJob?.Namespace,
        _ => null
    };

    private void OnCtxViewDeploymentLogs()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        _detailPanels?.CloseYaml();
        var pod = Pods.FirstOrDefault(p => p.Namespace == deployment.Namespace && p.Name.StartsWith(deployment.Name, StringComparison.Ordinal));
        if (pod is not null) _detailPanels?.ShowPodLogs(pod.Name, pod.Containers, pod.Namespace);
    }

    private void OnCtxViewPodLogs()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null) return;
        _detailPanels?.CloseYaml();
        _detailPanels?.ShowPodLogs(pod.Name, pod.Containers, pod.Namespace);
    }

    private async Task OnCtxRestartDeployment()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null || Client is null) return;

        var confirmed = await Confirm.ShowAsync(
        $"Restart deployment \"{deployment.Name}\"?",
        deployment.Name,
        requireTypedName: IsProduction);
        if (!confirmed) return;

        try
        {
            await Client.RestartDeploymentAsync(deployment.Namespace, deployment.Name);
            await LoadAsync();
            Notifications.ShowSuccess("Deployment restarted", deployment.Name);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Notifications.ShowError("Failed to restart deployment", ex: ex); }
    }

    private async Task OnCtxKillPod()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null || Client is null) return;

        var confirmed = await Confirm.ShowAsync(
        $"Delete pod \"{pod.Name}\"? This is irreversible.",
        pod.Name,
        requireTypedName: IsProduction);
        if (!confirmed) return;

        try
        {
            await Client.DeletePodAsync(pod.Namespace, pod.Name);
            _detailPanels?.CloseLogs();
            await LoadAsync();
            Notifications.ShowSuccess("Pod deleted", pod.Name);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Notifications.ShowError("Failed to delete pod", ex: ex); }
    }

    private async Task OnCtxCopyHostUrl()
    {
        var ingress = CtxIngress;
        CloseAllMenus();
        if (ingress is null) return;
        var host = ingress.Rules.FirstOrDefault(r => r.Host is not null)?.Host;
        if (host is null) return;
        var url = BuildIngressUrl(host);
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", url);
            Notifications.ShowSuccess("URL copied", url);
        }
        catch (Exception ex) { Logger.LogDebug(ex, "JS clipboard write failed"); }
    }

    private async Task OnCtxOpenIngressUrl()
    {
        var ingress = CtxIngress;
        CloseAllMenus();
        if (ingress is null) return;
        var host = ingress.Rules.FirstOrDefault(r => r.Host is not null)?.Host;
        if (host is null) return;
        await OpenUrlAsync(BuildIngressUrl(host));
    }

    private void OnCtxAnalyzeDeploymentNetwork()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        OpenDeploymentNetworkAnalysis(deployment);
    }

    private void OnCtxProbeFailuresDeployment()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        _detailPanels.ShowProbeFailures("Deployment", deployment.Name, deployment.Namespace);
    }

    private void OnCtxPlacementDeployment()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        _detailPanels.ShowPlacementAnalysis("Deployment", deployment.Name, deployment.Namespace);
    }

    private void OnCtxAnalyzeStatefulSetNetwork()
    {
        var statefulSet = CtxStatefulSet;
        CloseAllMenus();
        if (statefulSet is null) return;
        OpenStatefulSetNetworkAnalysis(statefulSet);
    }

    private void OnCtxProbeFailuresStatefulSet()
    {
        var statefulSet = CtxStatefulSet;
        CloseAllMenus();
        if (statefulSet is null) return;
        _detailPanels.ShowProbeFailures("StatefulSet", statefulSet.Name, statefulSet.Namespace);
    }

    private void OnCtxPlacementStatefulSet()
    {
        var statefulSet = CtxStatefulSet;
        CloseAllMenus();
        if (statefulSet is null) return;
        _detailPanels.ShowPlacementAnalysis("StatefulSet", statefulSet.Name, statefulSet.Namespace);
    }

    private void OnCtxAnalyzePodNetwork()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null) return;
        OpenPodNetworkAnalysis(pod);
    }

    private void OnCtxAnalyzeIngress()
    {
        var ingress = CtxIngress;
        CloseAllMenus();
        if (ingress is null) return;
        OpenIngressAnalysis(ingress);
    }

    private async Task OnCtxDeleteIngress()
    {
        var ingress = CtxIngress;
        CloseAllMenus();
        if (ingress is null || Client is null) return;

        var confirmed = await Confirm.ShowAsync(
            $"Delete ingress \"{ingress.Name}\"? This is irreversible.",
            ingress.Name,
            requireTypedName: IsProduction);
        if (!confirmed) return;

        try
        {
            await Client.DeleteIngressAsync(ingress.Namespace, ingress.Name);
            await LoadAsync();
            Notifications.ShowSuccess("Ingress deleted", ingress.Name);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Notifications.ShowError("Failed to delete ingress", ex: ex); }
    }

    private async Task OnCtxDeleteHttpRoute()
    {
        var httpRoute = CtxHttpRoute;
        CloseAllMenus();
        if (httpRoute is null || Client is null) return;

        var confirmed = await Confirm.ShowAsync(
            $"Delete HTTPRoute \"{httpRoute.Name}\"? This is irreversible.",
            httpRoute.Name,
            requireTypedName: IsProduction);
        if (!confirmed) return;

        try
        {
            await Client.DeleteHttpRouteAsync(httpRoute.Namespace, httpRoute.Name);
            await LoadAsync();
            Notifications.ShowSuccess("HTTPRoute deleted", httpRoute.Name);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Notifications.ShowError("Failed to delete HTTPRoute", ex: ex); }
    }

    private static string BuildIngressUrl(string host)
    {
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return host;
        if (System.Net.IPAddress.TryParse(host, out _))
            return $"http://{host}";
        return $"https://{host}";
    }

    private static string GetServiceIdentity(ServiceInfo service) => $"{service.Namespace}/{service.Name}";

    private static string GetNamespaceScopedIdentity(string ns, string name) => $"{ns}/{name}";

    private static string GetIngressIdentity(IngressInfo ingress) => $"{ingress.Namespace}/{ingress.Name}";

    private static string GetGatewayClassIdentity(GatewayClassInfo gatewayClass) => gatewayClass.Name;

    private static string GetGatewayIdentity(GatewayInfo gateway) => $"{gateway.Namespace}/{gateway.Name}";

    private static string GetHttpRouteIdentity(HttpRouteInfo httpRoute) => $"{httpRoute.Namespace}/{httpRoute.Name}";

    private static Task OpenUrlAsync(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Shell launch can be unavailable in tests or restricted environments.
        }

        return Task.CompletedTask;
    }

    private async Task OnCtxViewYamlGatewayClass()
    {
        var gatewayClass = CtxGatewayClass;
        CloseAllMenus();
        if (gatewayClass is null) return;
        await _detailPanels.OpenYamlAsync("GatewayClass", gatewayClass.Name, targetNamespace: string.Empty);
    }

    private async Task OnCtxViewYamlService()
    {
        var service = CtxService;
        CloseAllMenus();
        if (service is null) return;
        await _detailPanels.OpenYamlAsync("Service", service.Name, targetNamespace: service.Namespace);
    }

    private async Task OnCtxViewYamlGateway()
    {
        var gateway = CtxGateway;
        CloseAllMenus();
        if (gateway is null) return;
        await _detailPanels.OpenYamlAsync("Gateway", gateway.Name, targetNamespace: gateway.Namespace);
    }

    private async Task OnCtxViewYamlHttpRoute()
    {
        var httpRoute = CtxHttpRoute;
        CloseAllMenus();
        if (httpRoute is null) return;
        await _detailPanels.OpenYamlAsync("HTTPRoute", httpRoute.Name, targetNamespace: httpRoute.Namespace);
    }

    private async Task OnCtxEditYamlHttpRoute()
    {
        var httpRoute = CtxHttpRoute;
        CloseAllMenus();
        if (httpRoute is null) return;
        await _detailPanels.OpenYamlAsync("HTTPRoute", httpRoute.Name, editMode: true, targetNamespace: httpRoute.Namespace);
    }

    private async Task OnCtxViewYamlCronJob()
    {
        var cj = CtxCronJob;
        CloseAllMenus();
        if (cj is null) return;
        await _detailPanels.OpenYamlAsync("CronJob", cj.Name, targetNamespace: cj.Namespace);
    }

    private async Task OnCtxViewYamlJob()
    {
        var job = CtxJob;
        CloseAllMenus();
        if (job is null) return;
        await _detailPanels.OpenYamlAsync("Job", job.Name, targetNamespace: job.Namespace);
    }

    private async Task OnCtxRunCronJob()
    {
        var cronJob = CtxCronJob;
        CloseAllMenus();
        if (cronJob is null || Client is null) return;

        try
        {
            var createdJobName = await Client.TriggerCronJobAsync(cronJob.Namespace, cronJob.Name);
            Notifications.ShowSuccess("CronJob triggered", createdJobName);
            _ = RefreshJobsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            Notifications.ShowError("Failed to trigger CronJob", ex: ex);
        }
    }

    private async Task OnCtxRerunJob()
    {
        var job = CtxJob;
        CloseAllMenus();
        if (job is null || Client is null) return;

        try
        {
            var createdJobName = await Client.RerunJobAsync(job.Namespace, job.Name);
            Notifications.ShowSuccess("Job rerun started", createdJobName);
            _ = RefreshJobsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            Notifications.ShowError("Failed to rerun job", ex: ex);
        }
    }

    private async Task OnCtxToggleJobPause()
    {
        var job = CtxJob;
        CloseAllMenus();
        if (job is null || Client is null) return;

        var pausing = job.Parallelism != 0;
        var newParallelism = pausing ? 0 : 1;
        var verb = pausing ? "pause" : "resume";

        try
        {
            await Client.SetJobParallelismAsync(job.Namespace, job.Name, newParallelism);
            Notifications.ShowSuccess($"Job {verb}d", job.Name);
            _ = RefreshJobsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            Notifications.ShowError($"Failed to {verb} job", ex: ex);
        }
    }

    private async Task OnCtxToggleCronJobSuspend()
    {
        var cj = CtxCronJob;
        CloseAllMenus();
        if (cj is null || Client is null) return;

        var suspending = !cj.Suspend;
        var verb = suspending ? "suspend" : "resume";

        try
        {
            await Client.SuspendCronJobAsync(cj.Namespace, cj.Name, suspending);
            Notifications.ShowSuccess($"CronJob {verb}d", cj.Name);
            _ = RefreshJobsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            Notifications.ShowError($"Failed to {verb} CronJob", ex: ex);
        }
    }

    private void OnCtxScaleDeployment()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        _detailPanels.ShowScale(deployment.Name, deployment.Replicas, false, deployment.Namespace);
    }

    private void OnCtxAllPodsLogs()
    {
        var deployment = CtxDeployment;
        CloseAllMenus();
        if (deployment is null) return;
        _detailPanels?.CloseYaml();
        _detailPanels?.ShowDeploymentLogs(deployment.Name, deployment.Namespace);
    }

    private void OnCtxAllPodsLogsStatefulSet()
    {
        var ss = CtxStatefulSet;
        CloseAllMenus();
        if (ss is null) return;
        _detailPanels?.CloseYaml();
        _detailPanels?.ShowDeploymentLogs(ss.Name, ss.Namespace);
    }

    // Feature 2: StatefulSet actions
    private async Task OnCtxRestartStatefulSet()
    {
        var ss = CtxStatefulSet;
        CloseAllMenus();
        if (ss is null || Client is null) return;

        var confirmed = await Confirm.ShowAsync(
        $"Restart stateful set \"{ss.Name}\"?",
        ss.Name,
        requireTypedName: IsProduction);
        if (!confirmed) return;

        try
        {
            await Client.RestartStatefulSetAsync(ss.Namespace, ss.Name);
            await LoadAsync();
            Notifications.ShowSuccess("Stateful set restarted", ss.Name);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Notifications.ShowError("Failed to restart stateful set", ex: ex); }
    }

    private void OnCtxScaleStatefulSet()
    {
        var ss = CtxStatefulSet;
        CloseAllMenus();
        if (ss is null) return;
        _detailPanels.ShowScale(ss.Name, ss.Replicas, true, ss.Namespace);
    }

    // Feature 3: ConfigMap / Secret viewer
    private void OnCtxViewConfigMapKeys()
    {
        var cm = CtxConfigMap;
        CloseAllMenus();
        if (cm is null) return;
        _detailPanels.ShowConfigMapDetail(cm);
    }

    private void OnCtxViewSecretKeys()
    {
        var secret = CtxSecret;
        CloseAllMenus();
        if (secret is null) return;
        _detailPanels.ShowSecretDetail(secret);
    }

    // Feature 4: Container details
    private void OnCtxContainerDetailsPod()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null) return;
        _detailPanels.ShowContainerDetails(pod.Name, pod.Namespace);
    }

    private void OnCtxContainerDetailsDeployment()
    {
        var d = CtxDeployment;
        CloseAllMenus();
        if (d is null) return;
        var pod = Pods.FirstOrDefault(p => p.Namespace == d.Namespace && p.Name.StartsWith(d.Name, StringComparison.Ordinal) && p.Ready)
        ?? Pods.FirstOrDefault(p => p.Namespace == d.Namespace && p.Name.StartsWith(d.Name, StringComparison.Ordinal));
        if (pod is null) return;
        _detailPanels.ShowContainerDetails(pod.Name, pod.Namespace);
    }

    // Feature 5: HPA detail
    private void OpenHpaDetail(HpaInfo hpa)
    {
        _detailPanels.ShowHpaDetail(hpa);
    }

    // Feature 6: Open shell in pod
    private async Task OnCtxOpenPodShell()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null || Client is null) return;
        var container = pod.Containers
        .FirstOrDefault(c => c != "istio-proxy" && c != "linkerd-proxy")
        ?? pod.Containers.FirstOrDefault()
        ?? string.Empty;
        await Client.OpenShellAsync(pod.Namespace, pod.Name, container);
    }

    // Port-forward actions
    private void OnCtxPortForward()
    {
        var pod = CtxPod;
        CloseAllMenus();
        if (pod is null) return;

        _pfDialogPod = pod;
        _pfDialogRemotePort = 80; // default; user can change
        ShowPortForwardDialog = true;
        StateHasChanged();
    }

    private async Task OnStartPortForward(int localPort)
    {
        ShowPortForwardDialog = false;
        if (_pfDialogPod is null || Client is null) return;

        ShowPortForwardSessions = true;
        try
        {
            await SessionService.StartAsync(Client, _pfDialogPod.Namespace, _pfDialogPod.Name, localPort, _pfDialogRemotePort);
        }
        catch (Exception ex)
        {
            Notifications.ShowError("Port-forward failed", ex.Message, ex: ex);
        }
        StateHasChanged();
    }

    private async Task OnStopPortForwardSession(PortForwardSession session)
    {
        try { await SessionService.StopAsync(session); }
        catch (Exception ex) { Notifications.ShowError("Failed to stop session", ex.Message, ex: ex); }
    }

    private void HandleToggleEventsOff()
    {
        ShowEvents = false;
        StateHasChanged();
    }

    private static string TruncatePodName(string name)
    => name.Length > 36 ? name[..18] + "…" + name[^14..] : name;

    private void CloseAllMenus()
    {
        DeploymentMenu.Close();
        PodMenu.Close();
        ServiceMenu.Close();
        IngressMenu.Close();
        GatewayClassMenu.Close();
        GatewayMenu.Close();
        HttpRouteMenu.Close();
        HelmMenu.Close();
        StatefulSetMenu.Close();
        ConfigMapMenu.Close();
        SecretMenu.Close();
        JobMenu.Close();
        CronJobMenu.Close();
    }

    // ── Side panel helpers ──
    // (CloseLogs, CloseDeploymentLogs, CloseContainerDetail, CloseConfigMapDetail,
    // CloseSecretDetail moved to AksDetailPanels)

    private async Task OnCtxEditYaml(char source)
    {
        var (kind, name) = source switch
        {
            'D' => ("Deployment", CtxDeployment?.Name),
            'I' => ("Ingress", CtxIngress?.Name),
            'S' => ("StatefulSet", CtxStatefulSet?.Name),
            'C' => ("ConfigMap", CtxConfigMap?.Name),
            'E' => ("Secret", CtxSecret?.Name),
            _ => ((string?)null, (string?)null)
        };
        CloseAllMenus();
        if (kind is not null && name is not null)
            await _detailPanels.OpenYamlAsync(kind, name, editMode: true, targetNamespace: GetContextTargetNamespace(source));
    }

    private async Task CopyToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        CloseAllMenus();
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
        Notifications.ShowSuccess("Copied!");
    }

    private async Task OnCtxViewHelmValues()
    {
        var helm = CtxHelm;
        CloseAllMenus();
        if (helm is null) return;
        await _detailPanels.ShowHelmValuesAsync(helm.Name);
    }

    private async Task OnCtxViewHelmHistory()
    {
        var helm = CtxHelm;
        CloseAllMenus();
        if (helm is null) return;
        await _detailPanels.ShowHelmHistoryAsync(helm.Name);
    }

    private async Task OnCtxRollbackHelm()
    {
        var helm = CtxHelm;
        CloseAllMenus();
        if (helm is null) return;
        _detailPanels.CloseYaml();
        await _detailPanels.ShowHelmRollbackAsync(helm.Name);
    }

    private void OnCtxHelmDiffPreview()
    {
        var helm = CtxHelm;
        CloseAllMenus();
        if (helm is null) return;
        _detailPanels.ShowHelmDiffPreview(helm.Name, helm.Namespace);
    }
}
