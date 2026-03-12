# Archive Summary - AKS Enhancements v2

---

title: "Archive Summary - AKS Enhancements v2"
owner: ""
completed_date: "2026-03-12"
pr: ""
commit: ""

---

## Goal

Elevate the AKS page from a read-only resource browser to a practical daily-driver for Kubernetes troubleshooting and operations with improved UX, context menus, resizable panels, and targeted mutative actions.

## Delivered

- **Context menus**: Custom `ContextMenu.razor` component with backdrop, keyboard dismiss, cursor-positioned menu. Replaced all inline buttons on Deployments, Pods, Ingresses, and Helm tabs.
- **Confirmation bar**: `AksConfirmBar.razor` — inline confirmation with optional typed-name guard for production environments.
- **Mutative operations**: Restart deployment (rollout restart), kill pod (graceful delete), scale deployment (replica count patch), Helm rollback (CLI subprocess with revision picker).
- **Helm inspection**: Release history panel with revision table, computed values panel (decoded from Helm secrets), release manifest YAML via `helm get manifest`.
- **Resizable panels**: `ResizablePanel.razor` — draggable split panel with left-edge drag handle, min/max constraints, double-click to reset. Applied to YAML, History, Values, and Log panels.
- **Filtering**: `ResourceFilter.razor` — per-tab search/filter bar with real-time substring matching across name, status, and relevant fields.
- **Auto-refresh**: `AutoRefreshToggle.razor` — toolbar toggle with interval dropdown (10s/30s/60s), using `PeriodicTimer` with proper async loop and error resilience. Pauses when detail panels are open.
- **Pod metrics**: CPU and memory columns from Metrics API with color-coded indicators (green/orange/red). Graceful fallback when Metrics API unavailable.
- **Multi-namespace**: "All namespaces" option with parallel API calls per namespace. Clickable namespace column to drill into single namespace.
- **Bug fixes**: Fixed Helm YAML viewing (was missing "helm" resource kind), fixed auto-refresh (replaced `System.Threading.Timer` async void with `PeriodicTimer`), added revision picker for Helm rollback.

## Key decisions

- Custom HTML context menus instead of browser native — MAUI BlazorWebView compatibility.
- Production guard for destructive operations — typed-name confirmation on production clusters.
- Helm rollback via CLI subprocess — Go SDK not usable from .NET; `helm rollback` is reliable and standard.
- Helm manifest via `helm get manifest` CLI — Helm releases are not native K8s resources, Kubernetes API cannot retrieve manifests directly.

## Validation performed

- 18 unit tests for `DemoAksClient` covering all new methods (scale, helm history/values/rollback, pod metrics, multi-namespace, events, contexts).
- 13 unit tests for `KubernetesAksClient` CPU/memory parsing helpers (nanocores, millicores, Ki/Mi/Gi).
- All 31 tests passing. Build succeeds for all projects.

## Lessons learned

- `System.Threading.Timer` with async callbacks creates async void — use `PeriodicTimer` with a proper async loop for reliable periodic work in Blazor.
- Helm releases require CLI for operations (rollback, manifest retrieval) since the Go SDK is not accessible from .NET.
- Rollback UX should always present a revision picker rather than auto-selecting — users need visibility into what they're rolling back to.

## Follow-up

- Persist panel widths in UI state (minor polish).
- Label selector advanced filtering (minor polish).
- Integration tests with mocked Kubernetes API.

## Archive metadata

- Source: `docs/features/active/aks-enhancements-v2/`
- Predecessor: `docs/features/archive/aks-enhancements/`
- Related: `docs/features/archive/aks/` (connectivity foundation)
