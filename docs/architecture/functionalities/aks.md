# AKS

## What Is Supported

- Incident Timeline backend uses `IAksClientBootstrapper` plus selector-label workload resolution to gather workload-scoped pod lifecycle and event evidence for `Deployment`, `StatefulSet`, and `Pod` scopes.
- Connect to Kubernetes using default or configured kubeconfig/context.
- Keep the page shell and toolbar interactive while AKS client, context, and namespace bootstrap runs in the background.
- Context switching and namespace filtering (single, selected multiple, and all namespaces).
- Namespace picker supports explicit multi-selection so operators can compare a scoped set of namespaces without switching to full-cluster all-namespaces mode.
- Monitor namespace selector now supports case-insensitive text filtering for long namespace lists, with an explicit no-match empty state.
- Browse deployments, pods, Services, ingresses, Helm releases, Jobs, and CronJobs.
- Pods hide terminal `Completed` / `Succeeded` rows by default so completed Job pods do not crowd active troubleshooting; a pod-list checkbox reveals them on demand.
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
- Pod and multi-pod log viewers include a range selector (`Last 5m`, `Last 10m`, `Last 1h`, `All`, `Previous container`). `Live` maps directly to follow mode; selecting `Previous container` forces `Live` off and disables it until another range is selected.
- Pod logs expose the actual container list for multi-container pods so operators can switch tails without leaving the panel.
- Log viewers render a buffered history window with `Older`, `Newer`, and `Latest` navigation instead of trimming the UI to the last 500 rendered lines.
- While an operator pauses or browses history, incoming lines keep buffering without shifting the currently visible window; `Copy visible` preserves the current investigation slice while `Export all` downloads the full underlying stream.
- `All` log range now loads progressively from a bounded tail request; operators can request more history on demand with `Load older` without forcing the viewer to flood the UI with the full backlog immediately.
- Multi-pod log aggregation is always presented as one timestamp-merged stream; each pod keeps a stable color and legend entry so cross-pod correlation stays readable, and the legend doubles as a focus control for isolating one pod inside the merged view.
- Pod log export downloads the full underlying pod log stream instead of exporting only the currently visible window.
- View resource YAML.
- YAML edit mode preserves blank lines and inserts an indented newline on `Enter` so the highlighted overlay stays aligned with the editable textarea.
- **Port-forward sessions panel** — tracked, observable sessions with `Starting / Active / Stopping / Stopped / Error` lifecycle; dialog to configure local port; sticky sessions panel; status bar count badge; all sessions cancelled on app exit.
- Pod shell launch (externally via `wt.exe` or `cmd.exe` with `kubectl exec`).
- Deployment restart, scale operations, and pod delete.
- StatefulSet visibility — browse, restart, and scale StatefulSets; degraded sets are highlighted.
- ConfigMap viewer — filterable key/value table; YAML view and edit.
- Secret viewer — key-names-only list by default; individual values revealed on demand (never bulk-loaded).
- Container image and environment details — image tag with copy, resource requests/limits, env vars with ConfigMapRef resolution and SecretRef reveal.
- HPA inline status — HPA badge on Deployment and StatefulSet rows showing current/max replicas and CPU%; detail panel summarizes autoscaler state, replica movement, metrics, and conditions.
- HPA detail panel actions for `View YAML` and `Edit YAML`, reusing the shared AKS YAML viewer/apply workflow instead of a separate HPA-specific editor.
- Helm history, values, and rollback.
- Pod metrics retrieval where available — CPU and Memory columns always visible in the Pods grid; show "—" when metrics are unavailable.
- YAML viewer includes inline search (highlight + scroll to match).
- Ingress host cells are clickable — single click opens the URL in the default browser; right-click context menu offers "Open URL in browser" and "Copy URL" options.
- CronJob rows show schedule, active count, last schedule time, last success time, and suspended state badge.
- Publish current AKS context, namespace, resource type, filter state, and selected resource into the shared shell workspace model so favorites, recents, named favorites, and dashboard AKS namespace tiles can reopen AKS context.
- Windows tray continuity for monitoring — Minimize and Close hide the app to tray, monitoring continues in the existing `PodHealthMonitorService`, and hidden pod alerts increment tray unread state.

## Core Runtime Flow

1. `AksPage` calls `IAksClientBootstrapper` to resolve the correct client source (override, demo, or live), normalize the active context and namespace, and load the context and namespace lists without blocking the initial render.
2. After bootstrap completes, the page loads the selected resource collection, including Jobs and CronJobs in single-namespace, explicit multi-namespace, and all-namespaces mode.
3. Services are loaded alongside the other namespace-scoped resources and support all-namespaces browse, selection, and YAML viewing against the selected row namespace.
4. Gateway API resources are loaded through `gateway.networking.k8s.io` custom-resource queries (`GatewayClass`, `Gateway`, `HTTPRoute`) and are intentionally separate from `Ingress`.
5. Resource YAML for Services, Jobs, CronJobs, GatewayClasses, Gateways, and HTTPRoutes flows through the same `GetResourceYamlAsync` detail-panel path as other AKS resources.
6. Table, context-menu, and keyboard actions call `IAksClient` operations for mutations and diagnostics; `Run now` and `Rerun job` use the selected row namespace.
7. Ingress and network-policy analysis load on demand from the side-panel components and do not join the main browse-data cache or periodic refresh loop.
8. Successful batch create actions surface the created Job name and queue a background Jobs refresh so the new execution becomes discoverable without changing tabs.
9. Long-running and side-panel operations keep the main grid responsive.
10. HTTPRoute rows render in a non-virtualized grid path so variable-height route chips do not hide later rows when several routes are present.
11. HPA detail-panel YAML actions route through `AksDetailPanels.OpenYamlAsync("HPA", ...)`, and `AksYamlViewer` now treats `HPA` / `HorizontalPodAutoscaler` as editable resource kinds so operators can apply YAML changes through the same guarded flow used by Deployment, StatefulSet, ConfigMap, Secret, and Ingress edits.
12. Auto-refresh starts enabled at 10 seconds, pauses whenever any side panel (logs, YAML, container details, HPA, ingress analysis, network analysis, etc.) is open or the Events section is expanded, and resumes on panel close.
13. On Windows, tray lifecycle service subscribes to `PodHealthMonitorService.PodHealthDetected` and updates unread tray indicator only while app is hidden.

## Key Design Notes

- **Incident timeline anchor.** `AksTimelineSignalSource` is the anchor evidence adapter for the incident cockpit. It bootstraps the current `IAksClient`, resolves workload-owned pods from deployment/statefulset selector labels, and returns only workload-scoped pod lifecycle changes and events inside the requested UTC window. `DaemonSet` scopes are not yet supported by this adapter.
- **Batch workload contract.** `IAksClient` now exposes additive Jobs and trigger methods: `GetJobsAsync`, `TriggerCronJobAsync`, and `RerunJobAsync`. Default multi-namespace overloads for `GetJobsAsync` and `GetCronJobsAsync` let the AKS page keep both resource types visible in all-namespaces mode without special client wrappers.
- **Service inventory contract.** `IAksClient` now exposes `GetServicesAsync`. The AKS page treats Services as a first-class network resource with all-namespaces browse and row-namespace-aware YAML requests.
- **Wave 2 diagnostics contract.** `IAksClient` now exposes `AnalyzeIngressAsync` and `AnalyzeNetworkPoliciesAsync`. These return typed evidence summaries plus explicit limitation text instead of pushing raw object interpretation into Razor components.
- **Gateway API contract.** `IAksClient` exposes `GetGatewayClassesAsync`, `GetGatewaysAsync`, and `GetHttpRoutesAsync`. `KubernetesAksClient` queries Gateway API CRDs through the custom-objects client with `v1`/`v1beta1`/`v1alpha2` fallback so Envoy Gateway migrations remain visible even when classic `Ingress` is empty.
- **Bootstrap seam.** `IAksClientBootstrapper` now owns AKS client creation, context discovery, namespace discovery, and current-selection normalization. `AksPage` keeps a small signature guard so repeated parent re-renders do not restart the same bootstrap or reconnect path.
- **Batch browse model.** `JobInfo` carries status, active/succeeded/failed counts, desired completions, timestamps, source provenance, and labels so the Jobs grid can render operationally useful rows without a second read.
- **Batch YAML parity.** `GetResourceYamlAsync` explicitly supports `job` and `cronjob`. `DemoAksClient` emits batch/v1 YAML for both resource kinds, matching the live-client viewer flow.
- **Trigger provenance and sanitization.** `KubernetesAksClient` clones CronJob job templates or Job specs, strips controller-owned metadata and selectors, and annotates created Jobs with `swebkit.io/source-kind` and `swebkit.io/source-name`. Source mapping prefers owner references first, then these annotations.
- **Row-scoped batch actions.** In all-namespaces mode, `AksPage.razor` resolves Job and CronJob actions from the selected row object, not `CurrentNamespace`, which prevents accidental cross-namespace execution.
- **GatewayClass scope.** GatewayClasses are cluster-scoped resources. `AksPage` loads them independently of the namespace filter, restores them by name, and routes YAML requests through the shared viewer without a namespace dependency.
- **Gateway API identity.** Gateway and HTTPRoute selection, keyboard navigation, and workspace restore use `namespace/name` identity, matching the existing ingress namespace fix and avoiding collisions in all-namespaces mode.
- **Workspace integration.** `AksPage` registers a restore handler with `OperatorWorkspaceService`, publishes semantic snapshots for context, namespace, active resource tab, filters, panel flags, and current selection, and suppresses duplicate recent-resource writes while replaying a restore. Dashboard AKS namespace watch tiles persist an optional `context` alongside `namespace`; refresh and drill-through use that context when present and otherwise fall back to the configured/current context.
- **Unified side-panel column.** All side panels (YAML, Helm history/values, scale, logs, container details, ConfigMap/Secret detail, HPA) are rendered inside a single `aks-panels-col` flex container. Events sit at the bottom of this column as a collapsible inset (`aks-events-inset`), so multiple open panels never overflow the grid. When nothing is open the column is hidden and a thin vertical `aks-events-collapsed-tab` appears instead.
- **On-demand diagnostics panels.** `IngressAnalysisPanel` and `NetworkPolicyAnalysisPanel` are self-loading panel components. They fetch point-in-time evidence on open or refresh and deliberately stay outside the main browse-data refresh loop.
- **YAML search** is implemented entirely in `yamlHighlight.js` (`searchInPre`, `clearSearch`). Blazor calls JSInterop on each input change; match count is displayed in the search bar.
- YAML edit highlighting also uses `yamlHighlight.js`; editor mode deliberately preserves blank lines while the read-only viewer keeps its compact blank-line suppression.
- **Multi-pod log fan-out** uses `System.Threading.Channels.Channel<AggregatedLogLine>` (unbounded). Each per-pod task writes into the channel; a linked `CancellationTokenSource` ensures the outer consumer cancellation propagates to all per-pod readers. Aggregated lines carry parsed timestamps so the UI can keep a single merged chronological view without reparsing every line on each refresh.
- **Log viewer buffering** is intentionally decoupled from the rendered window. The UI keeps a larger bounded buffer, pages through it in fixed-size windows, and only auto-scrolls while the operator is on the latest window. This keeps live tails readable while preserving older context.
- **Progressive `All` history** avoids requesting the full container backlog up front. The viewer starts from a bounded tail and lets the operator pull older chunks explicitly, which keeps the hybrid UI responsive when pods emit large log volumes.
- **Secret values are never eagerly loaded.** `SecretInfo` holds only key names. Values are fetched on demand via `GetSecretValuesAsync` and cached for the panel lifetime.
- **HPA API versioning.** `GetHpasAsync` targets `autoscaling/v2` (K8s 1.23+) and falls back to `v1` silently on 404.
- **Container detail env resolution** batches ConfigMap lookups by name — one API call per unique ConfigMap. `envFrom` bulk-import rows are shown as synthetic flag entries.
- `KubernetesAksClient` includes Azure token fallback logic when kubeconfig exec auth is not enough.
- Helm operations are implemented through secret introspection and shelling out to `helm` for some commands.
- **Port-forward session management** is handled by `IPortForwardSessionService` (singleton). It holds a list of `PortForwardSession` objects, each with a `Status` enum (`Starting, Active, Stopping, Stopped, Error`) and an `OnStatusChanged` callback wired by the service. `KubernetesAksClient` sets `EnableRaisingEvents = true` and fires the callback on stdout/stderr/process-exit events. `StopAllAsync` is called from `AppDomain.CurrentDomain.ProcessExit` in `App.xaml.cs`. Sessions panel is rendered as a sticky-bottom strip in `AksPage.razor`; the status bar shows an active count button that navigates to AKS and opens the panel via `OpenPortForwardPanelEvent`.

## Main Code Locations

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
