# AKS

## What Is Supported

- Connect to Kubernetes using default or configured kubeconfig/context.
- Context switching and namespace filtering (single and all namespaces).
- Monitor namespace selector now supports case-insensitive text filtering for long namespace lists, with an explicit no-match empty state.
- Browse deployments, pods, ingresses, Helm releases, and CronJobs.
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
- CronJob visibility — schedule, active count, last schedule/success times, suspended state badge.
- Windows tray continuity for monitoring — Minimize and Close hide the app to tray, monitoring continues in the existing `PodHealthMonitorService`, and hidden pod alerts increment tray unread state.

## Core Runtime Flow

1. AKS page initializes client from selected environment AKS config.
2. UI loads context list, namespaces, and selected resource collection.
3. Table actions call `IAksClient` operations for mutations and diagnostics.
4. Long-running and side-panel operations keep the main grid responsive.
5. Auto-refresh pauses whenever any side panel (logs, YAML, container details, HPA, etc.) is open or the Events section is expanded, and resumes on panel close.
6. On Windows, tray lifecycle service subscribes to `PodHealthMonitorService.PodHealthDetected` and updates unread tray indicator only while app is hidden.

## Key Design Notes

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
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Aks/NamespaceMonitorSelector.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/Aks/ConfigMapDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/SecretDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/ContainerDetailPanel.razor`
- `src/SwebKit.App/Components/Aks/PortForwardSessionsPanel.razor`
- `src/SwebKit.App/Components/Aks/PortForwardStartDialog.razor`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Abstractions/IPortForwardSessionService.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- `src/SwebKit.Core/Services/PortForwardSessionService.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs`
- `src/SwebKit.App/Services/TrayLifecycleState.cs`

## Validation Pointers

- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
