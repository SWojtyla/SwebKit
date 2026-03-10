# Feature Overview - AKS Enhancements

---

title: "Feature Overview - AKS Enhancements"
owner: ""
status: "Planned"
created: "2026-03-10"
updated: "2026-03-10"

---

## Goal

Complete the AKS resource browsing experience after the connectivity foundation phase by adding kubeconfig context discovery, namespace-scoped workload/resource views, and read-only YAML inspection.

## Value

Developers can inspect AKS workloads end-to-end from one page using discoverable kubeconfig contexts and namespace-scoped resource views, without needing to switch to external tools for routine troubleshooting.

## Scope

- In scope:
  - Discover kubeconfig contexts from file and present a selector
  - Namespace list and selection sourced from cluster
  - Namespace-scoped views for pods, deployments, helm releases, and ingresses
  - Read-only YAML viewer for supported resource kinds
  - Error states for auth/RBAC/not-found and large payload loading states
- Out of scope:
  - Mutations (apply/delete/edit)
  - Live terminal/shell improvements beyond existing behavior
  - AKS management-plane operations (scale/upgrade/nodepool management)

## Dependencies

- Archived foundation feature: `docs/features/archive/aks/`
- `KubernetesClient` runtime behavior and cluster RBAC permissions
- Existing app shell/state infrastructure in `SwebKit.App` and `SwebKit.Core`

## Risks & mitigations

- Risk: large namespaces cause slow loads — Mitigation: lazy loading and per-tab refresh boundaries
- Risk: partial RBAC permissions — Mitigation: per-tab errors with non-blocking fallback for other tabs
- Risk: yaml rendering with large manifests — Mitigation: on-demand fetch and virtualized/read-only viewer behavior

## Related documents

- Archive summary: `docs/features/archive/aks/summary.md`
- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`

## Suggested modules

- `backend.md` — context discovery, namespace/resource contracts, yaml retrieval
- `frontend.md` — selectors, tabbed resource UX, yaml viewer states
- `decisions.md` — enhancement-phase tradeoffs
- `test-plan.md` — automated/manual validation expansion
- `status.md` — implementation progress

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Tests: `test-plan.md`
- Decisions: `decisions.md`
