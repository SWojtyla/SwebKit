# Archive Summary — AKS New Capabilities (v3)

---

title: "Archive Summary - AKS New Capabilities (v3)"
owner: ""
completed_date: "2026-03-17"
pr: ""
commit: ""

---

## Goal

Add six new capabilities to the AKS page covering common gaps in day-to-day Kubernetes debugging: aggregated pod logs, StatefulSet visibility, ConfigMap and Secret inspection, container image and environment detail, HPA status at a glance, and a direct pod shell.

## Delivered

- **Multi-pod log aggregation** — `MultiPodLogView.razor` + "Logs for all pods" context menu action on Deployments and StatefulSets. Lines prefixed with pod name, color-coded per pod. Fan-out uses `Channel<AggregatedLogLine>` with a linked `CancellationTokenSource` for clean teardown of all per-pod streams.
- **StatefulSets tab** — `GetStatefulSetsAsync`, a dedicated resource tab alongside Deployments, with ready/desired replica display, degraded-state highlighting, and Restart/Scale context menu actions.
- **ConfigMap viewer** — `GetConfigMapsAsync`, filterable key/value table, YAML view and edit.
- **Secret viewer** — `SecretDetailPanel.razor` + `GetSecretsAsync` / `GetSecretValuesAsync`. Only key names loaded initially; values fetched on demand per reveal action and cached for panel lifetime.
- **Container image and env vars quick-view** — `ContainerDetailPanel.razor` + `GetContainerDetailsAsync`. Shows image tag (with copy), resource requests/limits, and env vars with ConfigMapRef resolution (batched by ConfigMap name) and SecretRef reveal.
- **HPA inline status** — `GetHpasAsync` with `autoscaling/v2` → `v1` fallback. HPA badge columns on Deployment and StatefulSet rows; HPA detail panel with all metrics and conditions.
- **Pod shell** — `OnCtxOpenPodShell` wired to existing `OpenShellAsync`, accessible from the Pod context menu.
- **9 new `IAksClient` methods** + multi-namespace overload for StatefulSets.
- **10 new model types** in `AksModels.cs`.
- Full `DemoAksClient` and `KubernetesAksClient` implementations, both build-clean.
- 12 new unit tests in `DemoAksClientTests` (18 total on that suite; 44 across `SwebKit.Core.Tests`).

## Key decisions preserved

- **Channel-based log fan-out** (Decision 001): `Channel<AggregatedLogLine>` chosen over Rx.NET to avoid a new dependency. Outer cancellation propagates via linked `CancellationTokenSource`.
- **Secrets never eagerly loaded** (Decision 002): `SecretInfo.Keys` holds only key names. `GetSecretValuesAsync` is called on demand.
- **HPA v2 → v1 fallback**: `GetHpasAsync` catches HTTP 404 on `autoscaling/v2` and retries with `autoscaling/v1` to support clusters older than K8s 1.23.
- **ConfigMap env var batching**: container detail resolution groups all ConfigMapRef values by ConfigMap name, making one API call per unique ConfigMap.

## Validation

- Unit tests: 44/44 passing (`SwebKit.Core.Tests`).
- Build: `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` all clean.
- Architecture doc: `docs/architecture/functionalities/aks.md` updated.

## Related

- Prior round: `docs/features/archive/aks-enhancements-v2/` (context menus, confirmation, mutations, Helm, panels)
- Architecture: `docs/architecture/functionalities/aks.md`
