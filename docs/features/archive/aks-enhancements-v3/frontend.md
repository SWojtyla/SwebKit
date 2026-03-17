# Frontend Plan — AKS New Capabilities

---

title: "Frontend Plan - AKS New Capabilities"
owner: ""
status: "Planned"

---

## Goal

Extend the AKS page with six new user-facing capabilities while preserving the existing tab, panel, and context-menu patterns. Add four new Razor components for the more complex views; inline simpler panels directly in `AksPage.razor`.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor` + `.razor.css`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` + `.razor.css` (new)
- `src/SwebKit.App/Components/Aks/ConfigMapDetailPanel.razor` + `.razor.css` (new)
- `src/SwebKit.App/Components/Aks/SecretDetailPanel.razor` + `.razor.css` (new)
- `src/SwebKit.App/Components/Aks/ContainerDetailPanel.razor` + `.razor.css` (new)

## UX notes

- All new panels open in the existing `ResizablePanel` slide-out column — mutually exclusive with each other and with existing YAML/log panels.
- `AutoRefreshToggle` must pause when any panel is open. Extract a `HasOpenPanel` computed bool rather than extending the inline condition further.
- New resource tabs (StatefulSets, ConfigMaps, Secrets) follow the existing tab button + `FluentDataGrid` pattern exactly.
- Secret values are never rendered until the user explicitly reveals them — mask with `•••••••` and an eye-toggle button.
- Container details and HPA panels do not need separate component files — they are simple enough to inline in `AksPage.razor` as `ResizablePanel` content blocks.

## `ResourceTypes` array after all changes

```csharp
private static readonly string[] ResourceTypes =
    ["Deployments", "StatefulSets", "Pods", "ConfigMaps", "Secrets", "Ingresses", "Helm"];
```

## New components

### `MultiPodLogView.razor`

Parameters: `IAksClient? Client`, `string? Namespace`, `string? DeploymentName`

- Mirrors `PodLogView` streaming loop: calls `Client.StreamDeploymentLogsAsync(...)` inside `Task.Run`.
- Maintains `Dictionary<string, int> _podColorIndex` mapping pod name → colour index (0–7) on first sight.
- Legend bar at the top: one colored dot + truncated pod name per unique pod seen.
- Each line: `<div class="log-line log-pod-@(_podColorIndex[line.PodName])"><span class="log-pod-tag">@TruncatePodName(line.PodName)</span> @line.Line</div>`
- CSS: add `.log-pod-0` through `.log-pod-7` with distinct muted foreground colors (not background — must stay readable).
- Same 10 000-line cap, clear button, line count, and filter input as `PodLogView`.
- `TruncatePodName`: extract the last two hash segments from a standard pod name, e.g. `order-api-7d9f-xk2jp` → `7d9f-xk2jp`.

### `ConfigMapDetailPanel.razor`

Parameters: `ConfigMapInfo? ConfigMap`

- Two-column table: Key / Value.
- `ResourceFilter` input for live key filtering.
- Values shown as plain text (no masking — ConfigMap data is not secret by Kubernetes convention).
- No async calls needed — data is already in the model.

### `SecretDetailPanel.razor`

Parameters: `IAksClient? Client`, `string? Namespace`, `SecretInfo? Secret`

- Table of key names from `Secret.Keys`.
- Each row has an eye-toggle (`FluentIcon` or a `<button>` with aria-label).
- `Dictionary<string, string?> _revealedValues` holds decoded values after reveal.
- On first reveal: call `Client.GetSecretValuesAsync(Namespace, Secret.Name)` once, cache the full map. Subsequent reveals use the cache without another API call.
- On hide: remove from dictionary (value cleared from DOM).
- Loading spinner while fetching on first reveal.
- Add a "Reveal all / Hide all" toggle in the panel header for convenience.

### `ContainerDetailPanel.razor`

Parameters: `IAksClient? Client`, `string? Namespace`, `string? PodName`, `string? SourceLabel`

- On `OnParametersSetAsync`: call `Client.GetContainerDetailsAsync(Namespace, PodName)`.
- Container selector: `<select>` if multiple containers; plain header if only one.
- Per container:
  - Image badge: `acr.io/name:tag` with a copy-to-clipboard button.
  - Resource table: four rows (CPU request/limit, memory request/limit); show `—` for unset values.
  - Env vars table: Name | Source | Value.
    - `Plain`: show value directly.
    - `ConfigMapRef`: show `cm:{SourceName}/{SourceKey}` in muted text; show resolved value if `IsResolved`.
    - `SecretRef`: show `secret:{SourceName}/{SourceKey}`; value is `•••••••  [reveal]`. Reveal calls `GetSecretValuesAsync`.
    - `FieldRef`: show field path in muted text.
    - Synthetic `envFrom` row: show as `<all keys from configmap: {name}>` in italic muted text.
- Loading spinner and error state.
- `SourceLabel` shown in the panel header: e.g. "Deployment: order-api" or "Pod: order-api-7d9f-xk2jp".

## `AksPage.razor` changes

### Feature 1 — Multi-pod log aggregation

- Add `private string? LogDeploymentName;`.
- Deployment context menu: add "Logs for all pods" item. Handler sets `LogDeploymentName = CtxDeployment?.Name`, clears `LogPodName`.
- New `ResizablePanel` slide-out: condition `LogDeploymentName is not null && LogPodName is null && YamlTarget is null`. Content: `<MultiPodLogView>`.
- `CloseAllMenus` (or equivalent): add `LogDeploymentName = null`.
- Opening single-pod logs must clear `LogDeploymentName`; opening all-pods logs must clear `LogPodName`.

### Feature 2 — StatefulSets tab

- Add `"StatefulSets"` to `ResourceTypes` (after `"Deployments"`).
- State: `List<StatefulSetInfo> StatefulSets = []`, `string StatefulSetFilter = string.Empty`, `StatefulSetInfo? CtxStatefulSet`, `ContextMenu StatefulSetMenu = default!`.
- `IQueryable<StatefulSetInfo> FilteredStatefulSets` computed property.
- `LoadAsync` (single-namespace branch): add `GetStatefulSetsAsync` call in the parallel task set.
- Grid columns: Name, Ready (`ReadyReplicas/Replicas` pill badge — green if equal, orange if less), CurrentRevision, Labels count.
- Context menu: View YAML, Edit YAML, View Pods, Logs for all pods, Restart, Scale.
- `OnCtxViewStatefulSetPods`: set `ActiveResourceType = "Pods"` and apply label filter.
- `OnCtxScaleStatefulSet`: set `_scaleIsStatefulSet = true`; `OnScaleConfirm` routes to `ScaleStatefulSetAsync` vs `ScaleDeploymentAsync` based on this flag.
- `YamlIsEditable`: add `"StatefulSet"`.
- `CloseAllMenus`: close `StatefulSetMenu`.

### Feature 3 — ConfigMap and Secret viewer

- Add `"ConfigMaps"` and `"Secrets"` to `ResourceTypes`.
- State: `List<ConfigMapInfo> ConfigMaps = []`, `List<SecretInfo> Secrets = []`, filters, `ContextMenu ConfigMapMenu`, `ContextMenu SecretMenu`, `ConfigMapInfo? ConfigMapDetailTarget`, `SecretInfo? SecretDetailTarget`.
- `LoadAsync` (single-namespace branch): add `GetConfigMapsAsync` and `GetSecretsAsync` in the parallel task set.
- ConfigMaps grid: Name, Keys count, Labels count. Context menu: View YAML, Edit YAML, View Keys (opens `ConfigMapDetailPanel`).
- Secrets grid: Name, Type, Keys count. Context menu: View YAML, Edit YAML, View Keys (opens `SecretDetailPanel`).
- `ResizablePanel` slide-out for `ConfigMapDetailTarget is not null`: `<ConfigMapDetailPanel ConfigMap="ConfigMapDetailTarget" />`.
- `ResizablePanel` slide-out for `SecretDetailTarget is not null`: `<SecretDetailPanel Client="Client" Namespace="Namespace" Secret="SecretDetailTarget" />`.
- `YamlIsEditable`: add `"ConfigMap"` and `"Secret"`.
- `HasAnyData`: include `ConfigMaps.Count > 0 || Secrets.Count > 0`.

### Feature 4 — Container image and env vars quick-view

- Add `private string? ContainerDetailPodName;`, `private string? ContainerDetailLabel;`.
- Pod context menu: add "Container Details". Handler: `ContainerDetailPodName = CtxPod?.Name`, `ContainerDetailLabel = $"Pod: {CtxPod?.Name}"`.
- Deployment context menu: add "Container Details". Handler: resolve first ready pod for the deployment (`Pods.FirstOrDefault(p => p.Name.StartsWith(CtxDeployment!.Name) && p.Ready)`), set `ContainerDetailPodName` and label.
- `ResizablePanel` slide-out for `ContainerDetailPodName is not null`: `<ContainerDetailPanel>` with all parameters.
- `CloseAllMenus`: clear `ContainerDetailPodName`.

### Feature 5 — HPA inline status

- `LoadAsync`: add `GetHpasAsync` to the parallel task set; store in `private List<HpaInfo> Hpas = []`.
- Deployments grid: add `TemplateColumn Title="HPA"`. Cell: look up `Hpas.FirstOrDefault(h => h.TargetName == context.Name && h.TargetKind == "Deployment")`. Render `<button class="aks-hpa-badge" @onclick="() => OpenHpaDetail(hpa)">HPA @current/@max @ @cpu%</button>` if found, else nothing.
- Same column on StatefulSets grid (TargetKind == "StatefulSet").
- Add `private HpaInfo? HpaDetailTarget;`.
- `ResizablePanel` inline panel for `HpaDetailTarget is not null`: shows target, min/max/current/desired replicas, metrics table (`HpaMetricStatus` list), conditions list (`HpaCondition` list with colored status dots).
- `OpenHpaDetail`: set `HpaDetailTarget = hpa`, close other panels.
- `AutoRefreshToggle` Paused: factor into `HasOpenPanel` computed bool (see below).

### Feature 6 — Open shell in pod

- Pod context menu: add "Open shell in pod" below a `<div class="ctx-separator">`.
- Handler `OnCtxOpenPodShell`:
  ```csharp
  var container = pod.Containers
      .FirstOrDefault(c => c != "istio-proxy" && c != "linkerd-proxy")
      ?? pod.Containers.FirstOrDefault()
      ?? string.Empty;
  await Client.OpenShellAsync(pod.Namespace, pod.Name, container);
  ```
- No new state, panels, or interface methods required.

### `HasOpenPanel` refactor

Extract the `AutoRefreshToggle` Paused condition to a computed property:

```csharp
private bool HasOpenPanel =>
    LogPodName is not null ||
    LogDeploymentName is not null ||
    YamlTarget is not null ||
    ScaleTarget is not null ||
    HelmHistoryTarget is not null ||
    HelmValuesTarget is not null ||
    ConfigMapDetailTarget is not null ||
    SecretDetailTarget is not null ||
    ContainerDetailPodName is not null ||
    HpaDetailTarget is not null;
```

Pass `Paused="HasOpenPanel"` to `AutoRefreshToggle`.

## Tasks

- [ ] Add `"StatefulSets"`, `"ConfigMaps"`, `"Secrets"` to `ResourceTypes`
- [ ] Feature 6: Add "Open shell in pod" to Pod context menu (no new component)
- [ ] Feature 2: StatefulSets grid, context menu, scale flag routing
- [ ] Feature 3: ConfigMaps and Secrets grids, context menus, `YamlIsEditable` update
- [ ] Feature 5: HPA badge column on Deployments and StatefulSets, inline detail panel
- [ ] Feature 1: "Logs for all pods" in Deployment menu, `MultiPodLogView` component
- [ ] Feature 4: "Container Details" in Pod + Deployment menus, `ContainerDetailPanel` component
- [ ] New components: `MultiPodLogView`, `ConfigMapDetailPanel`, `SecretDetailPanel`, `ContainerDetailPanel`
- [ ] `HasOpenPanel` refactor in `AksPage.razor`
- [ ] Scoped CSS for all new components

## Blazor patterns and pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../../pitfalls/blazor-maui.md) for the full reference. Most relevant here:

- **BL-2** (`InvokeAsync`): all log stream callbacks must call `InvokeAsync(StateHasChanged)` — they run on a background thread.
- **BL-4** (`@if` destroy/recreate): `MultiPodLogView` and `ContainerDetailPanel` will be destroyed and re-created each time their panel opens/closes. The streaming loop must cancel cleanly in `DisposeAsync`. Initialize fresh on `OnParametersSetAsync`.
- **BL-6** (JS interop timing): not directly applicable here, but note that any copy-to-clipboard call in `ContainerDetailPanel` must be guarded by `_isRendered`.

## Implementation sequence

1. `HasOpenPanel` refactor (low risk, unblocks cleaner integration of all new panels).
2. Feature 6 — Pod shell (trivial, one menu item, no component).
3. Feature 2 — StatefulSets tab (directly mirrors Deployments; lowest UI risk).
4. Feature 3 — ConfigMaps and Secrets tabs + panel components.
5. Feature 5 — HPA badge column and inline panel.
6. Feature 4 — Container detail panel (medium risk — async mount, multiple containers).
7. Feature 1 — Multi-pod log component (highest risk — streaming + cancellation).

## Acceptance checks

- [ ] "Logs for all pods" streams from all replica pods simultaneously with colored pod-name prefixes.
- [ ] StatefulSets tab lists resources, Ready badge reflects degraded state visually, Restart and Scale work.
- [ ] ConfigMaps tab shows key/value pairs; filtering works.
- [ ] Secrets tab shows only key names; reveal toggle decodes and displays the value; second reveal uses cache.
- [ ] Container details panel shows image, requests/limits, and env vars with source annotations.
- [ ] SecretRef env vars are masked by default and reveal on demand.
- [ ] HPA badge visible on deployment and StatefulSet rows when an HPA exists; detail panel shows all metrics.
- [ ] "Open shell in pod" opens a terminal with `kubectl exec` against the correct pod and container.
- [ ] `AutoRefreshToggle` pauses correctly when any new panel is open.
- [ ] All panels are mutually exclusive and close cleanly when another panel opens.
