# Status - AKS Enhancements

---

title: "Status - AKS Enhancements"
owner: ""
state: "Complete"
branch: "sw/main/aks"
started: "2026-03-10"
last_updated: "2026-03-11"

---

## Quick summary

Follow-up feature after archiving AKS connectivity foundation. Delivers full namespace-scoped resource browsing, YAML inspection, context discovery, Helm release listing, and settings simplification.

**Current focus:** Ready for review.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation — namespace listing, ingress listing, context discovery, Helm releases, YAML retrieval, chart version parsing
- [x] Frontend implementation — resource tabs, namespace dropdown, collapsible events, UI redesign, context selector, YAML viewer, Helm tab
- [x] Context discovery from kubeconfig (dropdown instead of free text)
- [x] Read-only YAML viewer for resources
- [x] Helm release listing
- [x] AKS settings simplification (removed unused fields, automatic Azure auth, save feedback)
- [x] Tests (unit tests expanded to 24 passing tests)
- [x] Docs aligned
- [x] Ready for review

## Completed

- Created follow-up feature scope and module docs.
- Linked this feature to archived AKS connectivity foundation docs.
- **Namespace listing**: Added `GetNamespacesAsync()` to `IAksClient`, implemented in `KubernetesAksClient` (via `ListNamespaceAsync`) and `DemoAksClient` (7 demo namespaces).
- **Ingress listing**: Added `IngressInfo`, `IngressRule`, `IngressPath` models to `AksModels.cs`. Added `GetIngressesAsync()` to `IAksClient`, implemented in both clients (3 demo ingresses with realistic host/path/service routing).
- **Resource type tabs**: AksPage toolbar now has Deployments / Pods / Ingresses / Helm tab buttons. Each shows a dedicated FluentDataGrid with type-appropriate columns. All resource types load in parallel on refresh.
- **Namespace dropdown**: Toolbar includes a `<select>` populated from `GetNamespacesAsync()`. Changing namespace triggers a full reload of all resource types and events.
- **Collapsible events panel**: Events panel has a close button that collapses it to a thin vertical strip showing warning count. Click to re-expand. CSS grid columns adapt automatically.
- **AKS config moved to Settings only**: Removed inline connection config from AksPage. Connection details are now exclusively managed on the Settings page via `AksConfigForm`. AksPage shows a "Go to Settings" link when unconfigured.
- **UI/UX overhaul**: Complete rewrite of AksPage and PodLogView with scoped CSS (no inline styles), proper visual hierarchy, connection status indicator, empty states, ready badges, live-tailing pulse indicator, and better monospace font stack for logs.
- **Nav icon fix**: AKS nav icon changed from gear (same as Settings) to Kubernetes wheel.
- **Settings nav fix**: Removed `position: absolute` from Settings nav item; left-nav now uses flexbox column layout.
- **AKS settings simplification**: Removed `ExplicitClusterUrl`, `UseAzureCredentialFallback`, and `CredentialRef` from `AksConfig`. Settings form now has three optional fields (Kubeconfig Path, Default Context, Default Namespace) with inline save feedback and current-config summary. Azure credential fallback is always applied automatically based on kubeconfig content. `KubernetesAksClient` constructor simplified to two parameters.
- **Context discovery**: `GetContextsAsync()` reads kubeconfig and returns all contexts with current-context marking. AksPage context dropdown populated from this, reconnects on change.
- **YAML viewer**: Read-only YAML slide-out panel with loading/error states. Row-level YAML buttons on Deployments, Pods, and Ingresses. `GetResourceYamlAsync()` supports deployment, pod, ingress, and service kinds.
- **Helm release listing**: `GetHelmReleasesAsync()` reads Helm release secrets (label `owner=helm`), extracts name/version/status/chart. `TryParseChartVersion()` extracts semver from chart label. Helm tab shows release name, chart, app version, revision, status, and age.
- **Tests expanded**: 24 passing unit tests covering auth helpers (server-id parsing, scope construction, fallback gating), chart version parsing, client configuration, and edge cases.

## Remaining

- None. Feature is complete.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Unit tests passing (24/24). Manual validation pending.

## Notes

- Keep all AKS operations read-only in this phase.
- All resource data loads in parallel per namespace for better performance.
- Demo mode provides realistic data for all resource types.
