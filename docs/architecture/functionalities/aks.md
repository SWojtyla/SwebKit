# AKS

## What Is Supported

- Connect to Kubernetes using default or configured kubeconfig/context.
- Keep the page shell and toolbar interactive while AKS client, context, and namespace bootstrap runs in the background.
- Context switching and namespace filtering (single and all namespaces).
- Monitor namespace selector now supports case-insensitive text filtering for long namespace lists, with an explicit no-match empty state.
- Browse deployments, pods, ingresses, Helm releases, Jobs, and CronJobs.
- Jobs are a first-class AKS resource tab with browse, filter, row selection, and namespace-click behavior.
- Jobs grid shows status, source provenance, progress, and recent timing metadata; namespace is shown in all-namespaces mode.
- CronJobs remain visible in all-namespaces mode, and batch loads include default namespace entries.
- CronJob context menu supports `Run now`, which creates a new Job from the selected CronJob.
- Job context menu supports `View YAML` and `Rerun job`, which creates a new sibling Job from the selected Job.
- Batch actions always execute against the selected row namespace, not the namespace selector.
- User-cancelled batch trigger actions exit quietly without surfacing failure notifications.
- Success notifications for batch actions include the created Job name and trigger a background Jobs refresh.
- View Kubernetes events with warning highlighting.
- Stream pod logs with filtering.
- Multi-pod log aggregation — stream logs from all pods of a deployment simultaneously; lines are prefixed with pod name and color-coded per pod.
- Pod and multi-pod log viewers include a range selector (`Last 5m`, `Last 10m`, `Last 1h`, `All`, `Previous container`). `Live` maps directly to follow mode; selecting `Previous container` forces `Live` off and disables it until another range is selected.
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
- Windows tray continuity for monitoring — Minimize and Close hide the app to tray, monitoring continues in the existing `PodHealthMonitorService`, and hidden pod alerts increment tray unread state.

## Core Runtime Flow

1. `AksPage` calls `IAksClientBootstrapper` to resolve the correct client source (override, demo, or live), normalize the active context and namespace, and load the context and namespace lists without blocking the initial render.
2. After bootstrap completes, the page loads the selected resource collection, including Jobs and CronJobs in both single-namespace and all-namespaces mode.
3. Resource YAML for Jobs and CronJobs flows through the same `GetResourceYamlAsync` detail-panel path as other AKS resources.
4. Table and context-menu actions call `IAksClient` operations for mutations and diagnostics; `Run now` and `Rerun job` use the selected row namespace.
5. Successful batch create actions surface the created Job name and queue a background Jobs refresh so the new execution becomes discoverable without changing tabs.
6. Long-running and side-panel operations keep the main grid responsive.
7. Auto-refresh pauses whenever any side panel (logs, YAML, container details, HPA, etc.) is open or the Events section is expanded, and resumes on panel close.
8. On Windows, tray lifecycle service subscribes to `PodHealthMonitorService.PodHealthDetected` and updates unread tray indicator only while app is hidden.

## Key Design Notes

- **Batch workload contract.** `IAksClient` now exposes additive Jobs and trigger methods: `GetJobsAsync`, `TriggerCronJobAsync`, and `RerunJobAsync`. Default multi-namespace overloads for `GetJobsAsync` and `GetCronJobsAsync` let the AKS page keep both resource types visible in all-namespaces mode without special client wrappers.
- **Bootstrap seam.** `IAksClientBootstrapper` now owns AKS client creation, context discovery, namespace discovery, and current-selection normalization. `AksPage` keeps a small signature guard so repeated parent re-renders do not restart the same bootstrap or reconnect path.
- **Batch browse model.** `JobInfo` carries status, active/succeeded/failed counts, desired completions, timestamps, source provenance, and labels so the Jobs grid can render operationally useful rows without a second read.
- **Batch YAML parity.** `GetResourceYamlAsync` explicitly supports `job` and `cronjob`. `DemoAksClient` emits batch/v1 YAML for both resource kinds, matching the live-client viewer flow.
- **Trigger provenance and sanitization.** `KubernetesAksClient` clones CronJob job templates or Job specs, strips controller-owned metadata and selectors, and annotates created Jobs with `swebkit.io/source-kind` and `swebkit.io/source-name`. Source mapping prefers owner references first, then these annotations.
- **Row-scoped batch actions.** In all-namespaces mode, `AksPage.razor` resolves Job and CronJob actions from the selected row object, not `CurrentNamespace`, which prevents accidental cross-namespace execution.
- **Unified side-panel column.** All side panels (YAML, Helm history/values, scale, logs, container details, ConfigMap/Secret detail, HPA) are rendered inside a single `aks-panels-col` flex container. Events sit at the bottom of this column as a collapsible inset (`aks-events-inset`), so multiple open panels never overflow the grid. When nothing is open the column is hidden and a thin vertical `aks-events-collapsed-tab` appears instead.
- **YAML search** is implemented entirely in `yamlHighlight.js` (`searchInPre`, `clearSearch`). Blazor calls JSInterop on each input change; match count is displayed in the search bar.
- **Multi-pod log fan-out** uses `System.Threading.Channels.Channel<AggregatedLogLine>` (unbounded). Each per-pod task writes into the channel; a linked `CancellationTokenSource` ensures the outer consumer cancellation propagates to all per-pod readers. No ordering guarantees — lines arrive as produced.
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
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs`
- `src/SwebKit.App/Services/TrayLifecycleState.cs`

## Validation Pointers

- `tests/SwebKit.App.Tests/AksConnectionBarTests.cs`
- `tests/SwebKit.App.Tests/AksPageBatchTests.cs`
- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
