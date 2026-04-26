# AKS

## What Is Supported

- `SwebKit.WinUI` now includes a native AKS route with cluster bootstrap, context and namespace selection, shared-card metric summaries, a native explorer for Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, ConfigMaps, Secrets, Ingresses, GatewayClasses, Gateways, and HTTPRoutes, a shared detail pane for the current workload, batch, Helm, and edge selection, selected-pod log diagnostics, native workload-level aggregated logs for Deployments and StatefulSets, bounded native port-forward session management, selected-pod shell launch, selected-resource YAML load across the expanded explorer surface, selected-resource YAML edit/apply for the currently supported Deployment, StatefulSet, and Ingress kinds, ingress analysis, network-policy analysis, namespace quota inspection, pod disruption budget inspection, workload probe-failure and placement evidence, Helm history/values, Helm upgrade preview, Helm rollback preview, Helm rollback actions, recent namespace events, pod metrics and HPA context in the native detail surface, selected-resource URL open/copy actions for Ingress and HTTPRoute resources, workload restart/scale, native pod delete, Job/CronJob rerun or trigger actions, a native pod-health monitoring manager with monitored-namespace selection plus recent alert history, and a context-sensitive keyboard shortcut layer with visible hint chips for the same core AKS actions that the MAUI page exposes.
- Incident Timeline backend uses `IAksClientBootstrapper` plus selector-label workload resolution to gather workload-scoped pod lifecycle and event evidence for `Deployment`, `StatefulSet`, and `Pod` scopes.
- Connect to Kubernetes using default or configured kubeconfig/context.
- Keep the page shell and toolbar interactive while AKS client, context, and namespace bootstrap runs in the background.
- Context switching and namespace filtering (single and all namespaces).
- Monitor namespace selector now supports case-insensitive text filtering for long namespace lists, with an explicit no-match empty state.
- Browse deployments, pods, Services, ingresses, Helm releases, Jobs, and CronJobs.
- Network-oriented AKS resources are grouped behind an expandable `Network` menu so Services, Ingresses, and Gateway API resources stay available without flattening the main toolbar.
- Inspect ingress backend evidence and workload-scoped network-policy evidence from the existing AKS side-panel rail.
- Deployment, StatefulSet, Pod, and Ingress rows expose diagnostics entry points through row buttons, context menus, and keyboard shortcuts (`n` for workload network analysis, `i` for ingress inspection).
- Browse Gateway API resources separately from classic Ingresses: GatewayClasses, Gateways, and HTTPRoutes render in their own AKS tabs.
- Jobs are a first-class AKS resource tab with browse, filter, row selection, and namespace-click behavior.
- Jobs grid shows status, source provenance, progress, and recent timing metadata; namespace is shown in all-namespaces mode.
- CronJobs remain visible in all-namespaces mode, and batch loads include default namespace entries.
- Gateway and HTTPRoute tabs support all-namespaces mode, namespace-aware row selection/restore, and YAML viewing against the row namespace.
- GatewayClass tab is cluster-scoped, supports filter and row selection, and opens YAML without depending on the namespace selector.
- CronJob context menu supports `Run now`, which creates a new Job from the selected CronJob.
- Job context menu supports `View YAML` and `Rerun job`, which creates a new sibling Job from the selected Job.
- Batch actions always execute against the selected row namespace, not the namespace selector.
- User-cancelled batch trigger actions exit quietly without surfacing failure notifications.
- Success notifications for batch actions include the created Job name and trigger a background Jobs refresh.
- View Kubernetes events with warning highlighting.
- Stream pod logs with filtering.
- `SwebKit.WinUI` exposes a native pod diagnostics surface that can start and stop tracked port-forward sessions for the selected pod through the shared `IPortForwardSessionService`; pinned presets and shell-level port-forward badges remain on the MAUI side for now.
- `SwebKit.WinUI` can launch an external pod shell for the selected running pod through `IAksClient.OpenShellAsync`, using the first non-sidecar container when one is available.
- Pod and multi-pod log viewers include a range selector (`Last 5m`, `Last 10m`, `Last 1h`, `All`, `Previous container`). `Live` maps directly to follow mode; selecting `Previous container` forces `Live` off and disables it until another range is selected.
- Pod logs expose the actual container list for multi-container pods so operators can switch tails without leaving the panel.
- Log viewers render a buffered history window with `Older`, `Newer`, and `Latest` navigation instead of trimming the UI to the last 500 rendered lines.
- While an operator pauses or browses history, incoming lines keep buffering without shifting the currently visible window; `Copy visible` preserves the current investigation slice while `Export all` downloads the full underlying stream.
- `All` log range now loads progressively from a bounded tail request; operators can request more history on demand with `Load older` without forcing the viewer to flood the UI with the full backlog immediately.
- Multi-pod log aggregation is always presented as one timestamp-merged stream; each pod keeps a stable color and legend entry so cross-pod correlation stays readable, and the legend doubles as a focus control for isolating one pod inside the merged view.
- Pod log export downloads the full underlying pod log stream instead of exporting only the currently visible window.
- View resource YAML.
- **Port-forward sessions panel** — tracked, observable sessions with `Starting / Active / Stopping / Stopped / Error` lifecycle; dialog to configure local port; sticky sessions panel; status bar count badge; all sessions cancelled on app exit.
- Pod shell launch (externally via `wt.exe` or `cmd.exe` with `kubectl exec`).
- Deployment restart, scale operations, and pod delete.
- StatefulSet visibility — browse, restart, and scale StatefulSets; degraded sets are highlighted.
- ConfigMap viewer — filterable key/value table; YAML view and edit.
- Secret viewer — key-names-only list by default; individual values revealed on demand (never bulk-loaded).
- Container image and environment details — image tag with copy, resource requests/limits, env vars with ConfigMapRef resolution and SecretRef reveal.
- HPA inline status — HPA badge on Deployment and StatefulSet rows showing current/max replicas and CPU%; detail panel with all metrics and conditions.
- Helm history, values, and rollback.
- Pod metrics retrieval where available — CPU and Memory columns always visible in the Pods grid; show "—" when metrics are unavailable.
- YAML viewer includes inline search (highlight + scroll to match).
- Ingress host cells are clickable — single click opens the URL in the default browser; right-click context menu offers "Open URL in browser" and "Copy URL" options.
- CronJob rows show schedule, active count, last schedule time, last success time, and suspended state badge.
- Publish current AKS context, namespace, resource type, filter state, and selected resource into the shared shell workspace model so favorites, recents, and named favorites can reopen AKS context.
- Windows tray continuity for monitoring — Minimize and Close hide the app to tray, monitoring continues in the existing `PodHealthMonitorService`, hidden pod alerts increment tray unread state, and the WinUI AKS page can now manage the monitored namespace set directly instead of relying on the dashboard as the only native management surface.

## Core Runtime Flow

1. `AksPage` calls `IAksClientBootstrapper` to resolve the correct client source (override, demo, or live), normalize the active context and namespace, and load the context and namespace lists without blocking the initial render.
2. After bootstrap completes, the WinUI page loads Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, ConfigMaps, Secrets, Ingresses, GatewayClasses, Gateways, and HTTPRoutes into a native resource-explorer cache for the current namespace scope, refreshes the native monitor namespace picker from the discovered namespaces, and when a specific namespace is selected it also loads recent events, pod metrics, and HPA data for native detail enrichment.
3. The explorer pivots between resource kinds without reconnecting, and the shared detail pane derives its facts, highlights, YAML state, diagnostics state, and supported actions from the selected explorer row.
4. Native workload-log actions route the selected Deployment or StatefulSet through `IAksClient.StreamDeploymentLogsAsync`, reuse the same buffered log-window pattern as pod diagnostics, and keep aggregated all-pod output in the WinUI AKS route instead of bouncing operators back to the MAUI detail panels.
5. Gateway API resources are loaded through `gateway.networking.k8s.io` custom-resource queries (`GatewayClass`, `Gateway`, `HTTPRoute`) and are intentionally separate from `Ingress`.
6. Resource YAML for workloads, batch resources, Services, Ingresses, GatewayClasses, Gateways, and HTTPRoutes flows through the same `GetResourceYamlAsync` native detail-pane path; the current editable subset remains Deployments, StatefulSets, and Ingresses.
7. Native detail-pane actions call `IAksClient` operations for ingress analysis, network-policy analysis, namespace quotas, pod disruption budgets, probe failures, placement analysis, Helm values/history, Helm upgrade preview, Helm rollback preview/rollback, restart, scale, pod delete, `Run now`, and `Rerun job`; selected Ingress and HTTPRoute rows also expose open/copy URL actions derived from the surfaced host data. Batch and Helm mutations always use the selected row namespace, and the selected-resource YAML, diagnostics, Helm, plus mutation flows are bound to the WinUI page lifetime so navigating away or switching the selected resource cancels in-flight calls.
8. The WinUI page-level key handler routes MAUI-style shortcut keys into the selected-resource and selected-pod action paths, while deliberately ignoring active text-entry controls so filters and editors keep normal typing behavior.
9. Ingress and network-policy analysis load on demand and do not join the main browse-data cache or periodic refresh loop.
10. Successful batch create actions surface the created Job name and refresh the native resource scope so the new execution becomes discoverable without leaving the page.
11. The native monitoring panel manages watched namespaces through `IPodHealthMonitorService`, persists those choices through the existing live or demo configuration path, and surfaces the latest pod-health alerts inside the AKS route so demo mode can exercise the same operator workflow without a live cluster.
12. Long-running and side-panel operations keep the main grid responsive.
13. HTTPRoute rows render in a non-virtualized grid path so variable-height route chips do not hide later rows when several routes are present.
14. Auto-refresh starts enabled at 10 seconds, pauses whenever any side panel (logs, YAML, container details, HPA, ingress analysis, network analysis, etc.) is open or the Events section is expanded, and resumes on panel close.
15. On Windows, tray lifecycle service subscribes to `PodHealthMonitorService.PodHealthDetected` and updates unread tray indicator only while app is hidden.

## Key Design Notes

- **Incident timeline anchor.** `AksTimelineSignalSource` is the anchor evidence adapter for the incident cockpit. It bootstraps the current `IAksClient`, resolves workload-owned pods from deployment/statefulset selector labels, and returns only workload-scoped pod lifecycle changes and events inside the requested UTC window. `DaemonSet` scopes are not yet supported by this adapter.
- **Batch workload contract.** `IAksClient` now exposes additive Jobs and trigger methods: `GetJobsAsync`, `TriggerCronJobAsync`, and `RerunJobAsync`. Default multi-namespace overloads for `GetJobsAsync` and `GetCronJobsAsync` let the AKS page keep both resource types visible in all-namespaces mode without special client wrappers.
- **Service inventory contract.** `IAksClient` now exposes `GetServicesAsync`. The AKS page treats Services as a first-class network resource with all-namespaces browse and row-namespace-aware YAML requests.
- **Wave 2 diagnostics contract.** `IAksClient` now exposes `AnalyzeIngressAsync` and `AnalyzeNetworkPoliciesAsync`. These return typed evidence summaries plus explicit limitation text instead of pushing raw object interpretation into Razor components.
- **Gateway API contract.** `IAksClient` exposes `GetGatewayClassesAsync`, `GetGatewaysAsync`, and `GetHttpRoutesAsync`. `KubernetesAksClient` queries Gateway API CRDs through the custom-objects client with `v1`/`v1beta1`/`v1alpha2` fallback so Envoy Gateway migrations remain visible even when classic `Ingress` is empty.
- **Bootstrap seam.** `IAksClientBootstrapper` now owns AKS client creation, context discovery, namespace discovery, and current-selection normalization. `AksPage` keeps a small signature guard so repeated parent re-renders do not restart the same bootstrap or reconnect path.
- **WinUI explorer slice.** `AksPageViewModel` loads Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, Ingresses, GatewayClasses, Gateways, and HTTPRoutes together, projects them into one native explorer model, and keeps selected-pod diagnostics available even after the operator pivots the explorer to a non-pod resource kind.
- **WinUI action slice.** The same selected-resource model now drives YAML, ingress analysis, network-policy analysis, namespace quota inspection, pod disruption budget inspection, workload probe-failure and placement evidence, Helm history/values, Helm upgrade preview, Helm rollback preview/rollback, restart, scale, and batch trigger commands from the native detail pane, so the native parity layer no longer depends on the MAUI side-panel components for those surfaces.
- **WinUI workload-log slice.** The WinUI AKS page now reuses the shared `IAksClient.StreamDeploymentLogsAsync` seam for Deployment and StatefulSet all-pod logs, so operators can keep aggregated workload diagnostics inside the native route instead of falling back to the MAUI side panel.
- **WinUI monitoring slice.** `AksPageViewModel` now also mirrors the MAUI pod-health monitor flow: it exposes a native monitor panel, lets operators queue or remove namespaces, starts or stops monitoring through `IPodHealthMonitorService`, projects recent alert history inside the AKS route, and listens for shared monitor-state broadcasts so the AKS page and dashboard stay in sync when another surface changes the watched namespace set.
- **WinUI shortcut slice.** `AksPage.xaml` now owns a page-level keyboard handler that routes context-sensitive AKS shortcuts through `AksPageViewModel`, while ignoring active text-entry controls so slash-to-filter and YAML editing do not fight each other.
- **Selected-resource action lifetime.** The WinUI selected-resource action rail shares a page-lifetime cancellation source for YAML, diagnostics, Helm, and mutation commands. Disposal cancels these calls and blocks follow-on notifications or refresh work, which keeps navigation-away behavior from leaving the AKS page in a half-busy state.
- **Batch browse model.** `JobInfo` carries status, active/succeeded/failed counts, desired completions, timestamps, source provenance, and labels so the Jobs grid can render operationally useful rows without a second read.
- **Batch YAML parity.** `GetResourceYamlAsync` explicitly supports `job` and `cronjob`. `DemoAksClient` emits batch/v1 YAML for both resource kinds, matching the live-client viewer flow.
- **Trigger provenance and sanitization.** `KubernetesAksClient` clones CronJob job templates or Job specs, strips controller-owned metadata and selectors, and annotates created Jobs with `swebkit.io/source-kind` and `swebkit.io/source-name`. Source mapping prefers owner references first, then these annotations.
- **Row-scoped batch actions.** In all-namespaces mode, `AksPage.razor` resolves Job and CronJob actions from the selected row object, not `CurrentNamespace`, which prevents accidental cross-namespace execution.
- **GatewayClass scope.** GatewayClasses are cluster-scoped resources. `AksPage` loads them independently of the namespace filter, restores them by name, and routes YAML requests through the shared viewer without a namespace dependency.
- **Gateway API identity.** Gateway and HTTPRoute selection, keyboard navigation, and workspace restore use `namespace/name` identity, matching the existing ingress namespace fix and avoiding collisions in all-namespaces mode.
- **Workspace integration.** `AksPage` registers a restore handler with `OperatorWorkspaceService`, publishes semantic snapshots for context, namespace, active resource tab, filters, panel flags, and current selection, and suppresses duplicate recent-resource writes while replaying a restore.
- **Unified side-panel column.** All side panels (YAML, Helm history/values, scale, logs, container details, ConfigMap/Secret detail, HPA) are rendered inside a single `aks-panels-col` flex container. Events sit at the bottom of this column as a collapsible inset (`aks-events-inset`), so multiple open panels never overflow the grid. When nothing is open the column is hidden and a thin vertical `aks-events-collapsed-tab` appears instead.
- **On-demand diagnostics panels.** `IngressAnalysisPanel` and `NetworkPolicyAnalysisPanel` are self-loading panel components. They fetch point-in-time evidence on open or refresh and deliberately stay outside the main browse-data refresh loop.
- **YAML search** is implemented entirely in `yamlHighlight.js` (`searchInPre`, `clearSearch`). Blazor calls JSInterop on each input change; match count is displayed in the search bar.
- **Multi-pod log fan-out** uses `System.Threading.Channels.Channel<AggregatedLogLine>` (unbounded). Each per-pod task writes into the channel; a linked `CancellationTokenSource` ensures the outer consumer cancellation propagates to all per-pod readers. Aggregated lines carry parsed timestamps so the UI can keep a single merged chronological view without reparsing every line on each refresh.
- **Log viewer buffering** is intentionally decoupled from the rendered window. The UI keeps a larger bounded buffer, pages through it in fixed-size windows, and only auto-scrolls while the operator is on the latest window. This keeps live tails readable while preserving older context.
- **Progressive `All` history** avoids requesting the full container backlog up front. The viewer starts from a bounded tail and lets the operator pull older chunks explicitly, which keeps the hybrid UI responsive when pods emit large log volumes.
- **Secret values are never eagerly loaded.** `SecretInfo` holds only key names. Values are fetched on demand via `GetSecretValuesAsync` and cached for the panel lifetime.
- **HPA API versioning.** `GetHpasAsync` targets `autoscaling/v2` (K8s 1.23+) and falls back to `v1` silently on 404.
- **Container detail env resolution** batches ConfigMap lookups by name — one API call per unique ConfigMap. `envFrom` bulk-import rows are shown as synthetic flag entries.
- `KubernetesAksClient` includes Azure token fallback logic when kubeconfig exec auth is not enough.
- Helm operations are implemented through secret introspection and shelling out to `helm` for some commands.
- **Port-forward session management** is handled by `IPortForwardSessionService` (singleton). It holds a list of `PortForwardSession` objects, each with a `Status` enum (`Starting, Active, Stopping, Stopped, Error`) and an `OnStatusChanged` callback wired by the service. `KubernetesAksClient` sets `EnableRaisingEvents = true` and fires the callback on stdout/stderr/process-exit events. `StopAllAsync` is called from `AppDomain.CurrentDomain.ProcessExit` in `App.xaml.cs`. The MAUI host renders these sessions as a sticky-bottom strip with a status-bar badge, while the WinUI host currently projects the same service into the selected-pod diagnostics card with an inline start form and tracked session list.

## Main Code Locations

- `src/SwebKit.WinUI/Views/Aks/AksPage.xaml`
- `src/SwebKit.WinUI/Views/Aks/AksPage.xaml.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.PortForwards.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.PodShell.cs`
- `src/SwebKit.WinUI/Services/AksClientBootstrapper.cs`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Services/AksClientBootstrapper.cs`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Aks/AksConnectionBar.razor`
- `src/SwebKit.App/Components/Aks/NamespaceMonitorSelector.razor`
- `src/SwebKit.App/Components/Aks/CronJobGrid.razor`
- `src/SwebKit.App/Components/Aks/JobGrid.razor`
- `src/SwebKit.App/Components/Aks/GatewayClassGrid.razor`
- `src/SwebKit.App/Components/Aks/GatewayGrid.razor`
- `src/SwebKit.App/Components/Aks/HttpRouteGrid.razor`
- `src/SwebKit.App/Components/Aks/ServiceGrid.razor`
- `src/SwebKit.App/Components/Aks/IngressAnalysisPanel.razor`
- `src/SwebKit.App/Components/Aks/NetworkPolicyAnalysisPanel.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/Aks/ConfigMapDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/SecretDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/ContainerDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor`
- `src/SwebKit.App/Components/Aks/PortForwardStartDialog.razor`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Abstractions/IAksClientBootstrapper.cs`
- `src/SwebKit.Core/Constants/AksBatchAnnotations.cs`
- `src/SwebKit.Core/Abstractions/IPortForwardSessionService.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- `src/SwebKit.Core/Services/PortForwardSessionService.cs`
- `src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs`
- `src/SwebKit.App/Services/TrayLifecycleState.cs`

## Validation Pointers

- `tests/SwebKit.App.Tests/AksConnectionBarTests.cs`
- `tests/SwebKit.App.Tests/AksDetailPanelsTests.cs`
- `tests/SwebKit.App.Tests/AksPageBatchTests.cs`
- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
