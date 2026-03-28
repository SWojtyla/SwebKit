# Archive Summary — AKS Feature History

**Consolidated from:** aks, aks-enhancements, aks-enhancements-v2, aks-enhancements-v3, aks-enhancements-v4
**Span:** 2026-03-08 → 2026-03-18

---

## What was built (chronological)

**Phase 1 — Connectivity foundation (2026-03-08 to -10)**

- Kubeconfig-first AKS client; explicit path + context support; Azure Identity fallback for missing tokens
- AKS settings: kubeconfig path, context, namespace, apply/save reconnect
- Unit tests for AKS auth helper (server-id parsing, scope construction, fallback gating)

**Phase 2 — Resource browser (2026-03-11)**

- Context discovery: `GetContextsAsync()` reads all kubeconfig contexts; dropdown with reconnect
- Namespace-scoped resource tabs: Deployments, Pods, Ingresses, Helm (parallel load per namespace)
- Read-only YAML viewer; Helm release details, history, values; settings simplified to 3 fields

**Phase 3 — UX & ops (2026-03-12 to -13)**

- Custom `ContextMenu.razor` (cursor-positioned, keyboard dismiss, backdrop)
- `AksConfirmBar.razor` — inline confirm with typed-name guard for production
- Helm rollback via `helm` CLI subprocess; scale deployment/statefulset; pod deletion; pod shell (`kubectl exec`)
- Resizable right-side panels via `ResizablePanel.razor`

**Phase 4 — New capabilities (2026-03-15 to -17)**

- Multi-pod log aggregation (`MultiPodLogView.razor`) using `Channel<AggregatedLogLine>` fan-out
- StatefulSets tab with degraded-state highlighting and Scale/Restart actions
- ConfigMap detail panel; Secret detail panel (key names only, values revealed on demand)
- Container image + environment details panel (with `envFrom` heuristic)
- HPA status panel (v2/v1 fallback)
- Pod shell (`OpenShellAsync` reused, sidecar-skip heuristic for container selection)

**Phase 5 — UX polish (2026-03-18)**

- Correct panel stacking; improved events UX; YAML search; Ingress URL open in browser
- Accurate pod resource requests/limits display; correct Helm history ordering (by revision desc)
- CronJob tab with last schedule, next schedule, and active job count

---

## Key decisions (reuse value)

- **Kubeconfig as source of truth** — aligns with `kubectl`; Azure Identity fallback is additive only
- **Azure credential fallback is automatic** — `ShouldUseAzureCredentialFallback` gates activation; no user toggle needed
- **Namespace-first browsing** — all resource lists are namespace-scoped; reduces noise in large clusters
- **Custom HTML context menus** — MAUI BlazorWebView has inconsistent native context menu behavior; build custom
- **Production guard pattern** — destructive ops show inline confirm bar; production additionally requires typed resource name
- **Helm rollback via CLI subprocess** — `helm rollback` via `Process`; Kubernetes library cannot perform rollback natively
- **Multi-pod log fan-out via `Channel<T>`** — one channel per merge; each per-pod Task.Run writes into it; linked CTS for clean teardown
- **Secrets: key names only in list model** — values fetched on demand via separate call, cached in panel scope; minimal accidental exposure
- **HPA v2/v1 silent fallback** — try `autoscaling/v2`; catch 404; fall back to `autoscaling/v1` without surfacing an error
- **Container selection heuristic** — skip well-known sidecar names (`istio-proxy`, `linkerd-proxy`); use first remaining container; fall back to index 0
