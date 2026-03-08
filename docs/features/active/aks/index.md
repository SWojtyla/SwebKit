# Feature Overview - AKS

---

title: "Feature Overview - AKS"
owner: ""
status: "Planned"
created: "2026-03-08"
updated: "2026-03-08"

---

## Goal

Provide a Kubernetes resource browser that reads cluster configuration from kubeconfig files and lets developers explore namespaces, workloads, networking, and releases — with full YAML inspection — without leaving the app.

## Value

Eliminates constant context-switching between the app and `kubectl` / Lens / Azure Portal during day-to-day cluster operations. Developers can quickly browse resources, check status, and inspect YAML definitions from one unified tool.

## Scope

- In scope:
  - Read and parse kubeconfig files (default path + custom file selection)
  - Context switching between clusters defined in kubeconfig
  - Namespace listing with selection (sets active namespace for all views)
  - View pods — name, status, restarts, age, node, containers
  - View deployments — name, ready replicas, strategy, age
  - View Helm releases — name, revision, chart, app version, status, namespace
  - View ingresses — name, class, hosts, paths, backends, TLS status
  - View raw YAML for any listed resource (read-only)
- Out of scope:
  - Live log tailing, port-forwarding, embedded terminal (future enhancement)
  - Resource editing or deletion
  - Helm install / upgrade / rollback operations
  - CRD browsing
  - Azure-specific AKS management plane operations (scale, upgrade cluster)

## Dependencies

- Depends on `docs/features/active/foundation-mvp/` (DI, navigation, app shell)
- `KubernetesClient` NuGet package (official .NET Kubernetes client)
- Helm release listing via Kubernetes API (reads Helm release secrets directly)

## Risks & mitigations

- Risk: Large clusters with many namespaces/resources cause slow load times — Mitigation: Paginate resource lists and lazy-load YAML on demand
- Risk: Kubeconfig with expired or invalid credentials — Mitigation: Surface clear error messages with guidance to refresh credentials
- Risk: Helm release listing requires Helm secrets access — Mitigation: Read Helm release secrets directly from the cluster via Kubernetes API (no Helm CLI dependency)

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Suggested modules

- `backend.md` — kubeconfig, namespace/resource contracts, YAML retrieval
- `frontend.md` — AKS page flows and resource/YAML browsing UX
- `decisions.md` — architecture and scope tradeoffs
- `test-plan.md` — validation strategy and scenarios
- `status.md` — execution progress and blockers

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Tests: `test-plan.md`
- Decisions: `decisions.md`
