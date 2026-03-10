# Status - AKS Enhancements

---

title: "Status - AKS Enhancements"
owner: ""
state: "In Progress"
branch: "sw/main/aks"
started: "2026-03-10"
last_updated: "2026-03-10"

---

## Quick summary

Follow-up feature opened after archiving AKS connectivity foundation. Focus is now full namespace-scoped resource browsing and YAML inspection.

**Current focus:** YAML viewer and context discovery from kubeconfig.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation — namespace listing, ingress listing
- [x] Frontend implementation — resource tabs, namespace dropdown, collapsible events, UI redesign
- [ ] Context discovery from kubeconfig (dropdown instead of free text)
- [ ] Read-only YAML viewer for resources
- [ ] Helm release listing
- [ ] Tests (unit/integration/e2e)
- [x] Docs aligned
- [ ] Ready for review

## Completed

- Created follow-up feature scope and module docs.
- Linked this feature to archived AKS connectivity foundation docs.
- **Namespace listing**: Added `GetNamespacesAsync()` to `IAksClient`, implemented in `KubernetesAksClient` (via `ListNamespaceAsync`) and `DemoAksClient` (7 demo namespaces).
- **Ingress listing**: Added `IngressInfo`, `IngressRule`, `IngressPath` models to `AksModels.cs`. Added `GetIngressesAsync()` to `IAksClient`, implemented in both clients (3 demo ingresses with realistic host/path/service routing).
- **Resource type tabs**: AksPage toolbar now has Deployments / Pods / Ingresses tab buttons. Each shows a dedicated FluentDataGrid with type-appropriate columns. All three resource types load in parallel on refresh.
- **Namespace dropdown**: Toolbar includes a `<select>` populated from `GetNamespacesAsync()`. Changing namespace triggers a full reload of all resource types and events.
- **Collapsible events panel**: Events panel has a close button that collapses it to a thin vertical strip showing warning count. Click to re-expand. CSS grid columns adapt automatically.
- **AKS config moved to Settings only**: Removed inline connection config from AksPage. Connection details are now exclusively managed on the Settings page via `AksConfigForm`. AksPage shows a "Go to Settings" link when unconfigured.
- **UI/UX overhaul**: Complete rewrite of AksPage and PodLogView with scoped CSS (no inline styles), proper visual hierarchy, connection status indicator, empty states, ready badges, live-tailing pulse indicator, and better monospace font stack for logs.
- **Nav icon fix**: AKS nav icon changed from gear (same as Settings) to Kubernetes wheel.
- **Settings nav fix**: Removed `position: absolute` from Settings nav item; left-nav now uses flexbox column layout.

## Remaining

- Implement kubeconfig context discovery and selector wiring.
- Implement read-only YAML viewer for supported kinds.
- Add Helm release listing.
- Expand tests and complete manual validation.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Keep all AKS operations read-only in this phase.
- All resource data loads in parallel per namespace for better performance.
- Demo mode provides realistic data for all resource types.
