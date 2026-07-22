using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.App.Components.Pages;

/// <summary>
/// Lifecycle methods, bootstrap, and data loading for AksPage.
/// Extracted from AksPage.razor for readability.
/// </summary>
public partial class AksPage
{
    private AksNamespaceScope CurrentNamespaceScope => AksNamespaceScope.FromSelection(CurrentNamespace, Namespaces);

    private bool IsMultiNamespace => CurrentNamespaceScope.IsMulti;

    private string CurrentPrimaryNamespace => CurrentNamespaceScope.Primary;

    private IReadOnlyList<string> GetScopedNamespaces() => CurrentNamespaceScope.Namespaces;

    private void StoreSnapshot(string? cacheKey = null)
    {
        cacheKey ??= CurrentCacheKey;
        if (cacheKey is null)
            return;

        Cache.Set(cacheKey, new AksPageSnapshot(
            Deployments, StatefulSets, Pods, Services, Ingresses, GatewayClasses, Gateways, HttpRoutes, HelmReleases,
            ConfigMaps, Secrets, Hpas, Events, Jobs, CronJobs, PodMetricsList));
    }

    private async Task RefreshJobsAsync()
    {
        if (Client is null)
            return;

        var requestedContext = ActiveContext;
        var requestedNamespace = CurrentNamespace;

        try
        {
            var scope = CurrentNamespaceScope;
            IReadOnlyList<JobInfo> refreshedJobs = scope.IsMulti
                ? await Client.GetJobsAsync(scope.Namespaces)
                : await Client.GetJobsAsync(scope.Primary);

            if (requestedContext != ActiveContext || requestedNamespace != CurrentNamespace)
                return;

            Jobs = refreshedJobs.ToList();
            StoreSnapshot();
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to refresh AKS jobs for {Namespace}", requestedNamespace);
        }
    }

    private async Task LoadAsync()
    {
        if (Client is null) return;

        if (CurrentNamespaceScope.Namespaces.Count == 0)
        {
            ClearSelection();
            _detailPanels?.ResetAllPanels();
            Deployments = [];
            StatefulSets = [];
            Pods = [];
            Services = [];
            Ingresses = [];
            GatewayClasses = [];
            Gateways = [];
            HttpRoutes = [];
            HelmReleases = [];
            ConfigMaps = [];
            Secrets = [];
            Hpas = [];
            Events = [];
            _eventWarningCount = 0;
            Jobs = [];
            CronJobs = [];
            PodMetricsList = [];
            IsLoading = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        // PERF-18: Restore cached data for instant back-navigation rendering
        var cacheKey = CurrentCacheKey;
        var cached = cacheKey is not null ? Cache.Get<AksPageSnapshot>(cacheKey) : null;
        if (cached is not null)
        {
            Deployments = cached.Deployments;
            StatefulSets = cached.StatefulSets;
            Pods = cached.Pods;
            Services = cached.Services;
            Ingresses = cached.Ingresses;
            GatewayClasses = cached.GatewayClasses;
            Gateways = cached.Gateways;
            HttpRoutes = cached.HttpRoutes;
            HelmReleases = cached.HelmReleases;
            ConfigMaps = cached.ConfigMaps;
            Secrets = cached.Secrets;
            Hpas = cached.Hpas;
            Events = cached.Events;
            _eventWarningCount = Events.Count(e => e.Type == "Warning");
            Jobs = cached.Jobs;
            CronJobs = cached.CronJobs;
            PodMetricsList = cached.PodMetricsList;
            IsLoading = false;
            StateHasChanged();
            // Continue to background-refresh (stale-while-revalidate)
        }

        // PERF-16: Cancel any in-flight load before starting a new one
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _cts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = newCts.Token;

        // Always show the loading state while fetching fresh data, even when we render a stale
        // cache snapshot immediately. This gives the user a clear, consistent indicator that
        // background work is in progress.
        IsLoading = true; ErrorMessage = null; StateHasChanged();
        try
        {
            // PERF-7: Incremental rendering — each dataset renders as it completes
            // PERF2-8: Batch StateHasChanged via dirty flag + debounced flush loop
            bool datasetDirty = false;

            // Multi-namespace fan-out (IAksClient.FanOutNamespacesAsync) already isolates
            // per-namespace RBAC denials so one forbidden namespace doesn't discard data from
            // namespaces the operator does have access to; it records each denial here. This
            // scope also catches AksAccessDeniedException raised directly by LoadDataset below,
            // which only happens for calls that bypass fan-out (cluster-scoped GatewayClasses, or
            // any call in single-namespace mode).
            using var accessScope = new AksAccessDeniedScope();

            async Task LoadDataset<T>(Func<Task<IReadOnlyList<T>>> fetch, Action<List<T>> assign)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await fetch();
                    ct.ThrowIfCancellationRequested();
                    assign(result.ToList());
                    datasetDirty = true;
                }
                catch (OperationCanceledException) { throw; } // CS-2
                catch (AksAccessDeniedException)
                {
                    var kind = typeof(T).Name.EndsWith("Info", StringComparison.Ordinal) ? typeof(T).Name[..^4] : typeof(T).Name;
                    AksAccessDeniedScope.Record(kind, CurrentNamespaceScope.IsMulti ? "(cluster)" : CurrentNamespace);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Dataset load failed for namespace {Namespace}", CurrentNamespace);
                }
            }

            var scope = CurrentNamespaceScope;
            if (scope.IsMulti)
            {
                var scopedNamespaces = scope.Namespaces;

                // Clear non-loaded lists synchronously
                HelmReleases = [];
                ConfigMaps = [];
                Secrets = [];
                Hpas = [];
                Events = [];
                _eventWarningCount = 0;
                PodMetricsList = [];

                var tasks = new List<Task>
                {
                    LoadDataset(() => Client.GetDeploymentsAsync(scopedNamespaces), r => Deployments = r),
                    LoadDataset(() => Client.GetPodsAsync(scopedNamespaces), r => Pods = r),
                    LoadDataset(() => Client.GetStatefulSetsAsync(scopedNamespaces), r => StatefulSets = r),
                    LoadDataset(() => Client.GetServicesAsync(scopedNamespaces), r => Services = r),
                    LoadDataset(() => Client.GetIngressesAsync(scopedNamespaces), r => Ingresses = r),
                    LoadDataset(() => Client.GetGatewayClassesAsync(ct), r => GatewayClasses = r),
                    LoadDataset(() => Client.GetGatewaysAsync(scopedNamespaces), r => Gateways = r),
                    LoadDataset(() => Client.GetHttpRoutesAsync(scopedNamespaces), r => HttpRoutes = r),
                    LoadDataset(() => Client.GetHpasAsync(scopedNamespaces), r => Hpas = r),
                    LoadDataset(() => Client.GetJobsAsync(scopedNamespaces), r => Jobs = r),
                    LoadDataset(() => Client.GetCronJobsAsync(scopedNamespaces), r => CronJobs = r),
                };

                // PERF2-8: Render flush loop — batch renders at ~150ms intervals
                var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var flushTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!flushCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(150, flushCts.Token);
                            if (datasetDirty)
                            {
                                datasetDirty = false;
                                await InvokeAsync(StateHasChanged);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                });

                await Task.WhenAll(tasks);
                flushCts.Cancel();
                flushCts.Dispose();

                // Final flush — render any remaining dirty state
                datasetDirty = false;
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                var ns = scope.Primary;

                var tasks = new List<Task>
                {
                    LoadDataset(() => Client.GetDeploymentsAsync(ns), r => Deployments = r),
                    LoadDataset(() => Client.GetStatefulSetsAsync(ns), r => StatefulSets = r),
                    LoadDataset(() => Client.GetPodsAsync(ns), r => Pods = r),
                    LoadDataset(() => Client.GetServicesAsync(ns), r => Services = r),
                    LoadDataset(() => Client.GetIngressesAsync(ns), r => Ingresses = r),
                    LoadDataset(() => Client.GetGatewayClassesAsync(ct), r => GatewayClasses = r),
                    LoadDataset(() => Client.GetGatewaysAsync(ns), r => Gateways = r),
                    LoadDataset(() => Client.GetHttpRoutesAsync(ns), r => HttpRoutes = r),
                    LoadDataset(() => Client.GetHelmReleasesAsync(ns), r => HelmReleases = r),
                    LoadDataset(async () => (IReadOnlyList<KubernetesEvent>)(await Client.GetEventsAsync(ns)).Take(50).ToList(), r => {
Events = r; _eventWarningCount = r.Count(e => e.Type == "Warning"); }),
                    LoadDataset(() => Client.GetPodMetricsAsync(ns), r => PodMetricsList = r),
                    LoadDataset(() => Client.GetConfigMapsAsync(ns), r => ConfigMaps = r),
                    LoadDataset(() => Client.GetSecretsAsync(ns), r => Secrets = r),
                    LoadDataset(() => Client.GetHpasAsync(ns), r => Hpas = r),
                    LoadDataset(() => Client.GetJobsAsync(ns), r => Jobs = r),
                    LoadDataset(() => Client.GetCronJobsAsync(ns), r => CronJobs = r),
                };

                // PERF2-8: Render flush loop — batch renders at ~150ms intervals
                var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var flushTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!flushCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(150, flushCts.Token);
                            if (datasetDirty)
                            {
                                datasetDirty = false;
                                await InvokeAsync(StateHasChanged);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                });

                await Task.WhenAll(tasks);
                flushCts.Cancel();
                flushCts.Dispose();

                // Final flush — render any remaining dirty state
                datasetDirty = false;
                await InvokeAsync(StateHasChanged);
            }

            var resourceWarning = BuildPermissionWarning(accessScope.Denials);
            PermissionWarning = _namespaceListWarning is null
                ? resourceWarning
                : resourceWarning is null
                    ? _namespaceListWarning
                    : $"{_namespaceListWarning} {resourceWarning}";
        }
        catch (OperationCanceledException) { return; } // CS-2: cancelled — new load takes over
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                // PERF-18: Save fresh data to cache
                if (cacheKey is not null)
                {
                    StoreSnapshot(cacheKey);
                }

                await PublishWorkspaceSnapshotAsync(recordRecent: false);
                IsLoading = false;
                await InvokeAsync(StateHasChanged); // BL-2
                CheckAllPodsGreen();
            }
        }
    }

    private static string? BuildPermissionWarning(IReadOnlyList<(string ResourceKind, string Namespace)> denials)
    {
        if (denials.Count == 0) return null;

        // Optional Gateway API resources (gateway.networking.k8s.io) are advanced networking, not baseline
        // cluster access — a 403 on them must not raise the core "limited permissions" warning. Filter them
        // out before grouping so their view simply hides/degrades. Core denials (pods, deployments, …) remain.
        var filtered = denials
            .Where(d => !SwebKit.Kubernetes.AksClient.KubernetesAksClient.GatewayApiDenialKinds.Contains(d.ResourceKind))
            .ToList();

        if (filtered.Count == 0) return null;

        var byKind = filtered
            .GroupBy(d => d.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(d => d.Namespace))}");

        return "No permission to list some resources — showing everything else. " + string.Join(" · ", byKind);
    }

    private static bool CanReuseWarmBootstrapResult(
        AksClientBootstrapResult warm,
        string requestedContext,
        string requestedNamespace)
    {
        if (!string.IsNullOrWhiteSpace(requestedContext)
            && !string.Equals(warm.ActiveContext, requestedContext, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(warm.CurrentNamespace, requestedNamespace, StringComparison.Ordinal);
    }

    private async Task HandleContextChangedAsync(string newContext)
    {
        if (newContext == ActiveContext) return;
        Cache.InvalidateByPrefix("aks:"); // PERF-18: context switch — invalidate stale cache
        ActiveContext = newContext;
        CurrentNamespace = string.Empty;
        _detailPanels?.ResetAllPanels();
        ClearSelection();
        // Clear stale data immediately so the loading spinner is shown instead of
        // old pods/deployments from the previous context bleeding through.
        Deployments = []; StatefulSets = []; Pods = []; Services = [];
        Ingresses = []; GatewayClasses = []; Gateways = []; HttpRoutes = [];
        HelmReleases = []; ConfigMaps = []; Secrets = []; Hpas = [];
        Events = []; Jobs = []; CronJobs = []; PodMetricsList = [];
        _eventWarningCount = 0;
        await BootstrapAndLoadAsync(newContext, string.Empty);
        await PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private async Task HandleNamespaceChangedAsync(string ns)
    {
        CurrentNamespace = ns;
        // PERF-18: namespace switch does NOT invalidate — each ns has its own cache key
        await LoadAsync();
        await PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private Task HandleNamespaceJumpAsync(string ns)
    {
        CurrentNamespace = ns;
        return LoadAsync();
    }
}
