# Feature Overview — AKS New Capabilities

---

title: "AKS New Capabilities"
owner: ""
status: "Planned"
created: "2026-03-15"
updated: "2026-03-15"

---

## Goal

Add six new capabilities to the AKS page that cover common gaps in day-to-day Kubernetes debugging: aggregated pod logs, StatefulSet visibility, ConfigMap and Secret inspection, container image and environment detail, HPA status at a glance, and a direct pod shell.

## Value

Reduce context-switching to `kubectl` for the most frequent debugging actions not yet covered by the existing AKS page. A .NET developer troubleshooting a production issue should be able to inspect configuration, check scaling state, trace container images, and follow multi-pod logs without leaving the tool.

## Scope

### In scope

1. **Multi-pod log aggregation** — Stream logs from all pods of a deployment simultaneously. Lines prefixed with pod name, color-coded per pod. Accessible via "Logs for all pods" in the Deployment context menu.
2. **StatefulSets tab** — New resource tab alongside Deployments. Shows name, namespace, ready/desired replicas, current and update revision, labels. Context menu: View YAML, Edit YAML, View Pods, Logs for all pods, Restart, Scale.
3. **ConfigMap and Secret viewer** — Two new resource tabs. ConfigMaps show key/value pairs in a filterable table. Secrets show only key names by default; each key has a reveal toggle that fetches and decodes the value on demand. Both support YAML view and edit.
4. **Container image and env vars quick-view** — "Container Details" panel from Pod and Deployment context menus. Shows container name, image:tag (with copy button), resource requests/limits, and env vars — resolving ConfigMapKeyRef references and masking SecretKeyRef values with a reveal toggle.
5. **HPA status inline** — Fetch HPA resources and surface their status as a badge on Deployment and StatefulSet rows (e.g. "HPA 3/5 @ 68% CPU"). Clicking the badge opens a detail panel with all metrics and conditions.
6. **Open shell in pod** — "Open shell in pod" action on the Pod context menu. Launches an external terminal (`wt.exe` or `cmd.exe`) running `kubectl exec -it <pod> -n <ns> -c <container> -- /bin/sh`.

### Out of scope

- Embedded in-app xterm.js terminal (shell opens externally, consistent with existing behavior)
- Full `envFrom` resolution for bulk ConfigMap/Secret imports (flag row shown; full resolution deferred)
- Container picker dialog for multi-container pods (heuristic skip of sidecars for MVP)
- Node viewer, network policy viewer, resource quota viewer

## Dependencies

- Existing `IAksClient`, `KubernetesAksClient`, `DemoAksClient`, `AksPage.razor`
- `kubectl` on PATH (already required by existing features)
- `autoscaling/v2` API (K8s 1.23+); must fall back to v1 gracefully for older clusters
- `aks-improvements` feature must not conflict on `IAksClient` signature additions

## Risks

- Multi-pod log fan-out: cancellation must propagate to all per-pod streams via linked `CancellationTokenSource` — not the outer token directly.
- HPA `autoscaling/v2` absent on clusters older than K8s 1.23: catch 404 and fall back to v1 silently.
- Container detail env var resolution: each unique ConfigMap name is one extra API call — batch within the method.
- `LoadAsync` now fans out further; one failing tab must not block others — wrap each task individually.

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- AKS functionality deep-dive: `docs/architecture/functionalities/aks.md` (update after implementation)
- Pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md`
- Prior art: `docs/features/archive/aks-enhancements/` (previous enhancement round — context discovery, resource listing, YAML viewer)

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
- Decisions: `decisions.md`
