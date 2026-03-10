# Backend Plan - AKS Enhancements

---

title: "Backend Plan - AKS Enhancements"
owner: ""
status: "In Progress"

---

## Goal

Expand AKS backend capabilities from connectivity-only to full namespace-scoped resource and YAML retrieval workflows.

## Impacted areas

- `src/SwebKit.Kubernetes/`
- `src/SwebKit.Core/Abstractions/`
- `src/SwebKit.Core/Models/`
- `src/SwebKit.Core/Services/`
- `tests/SwebKit.Kubernetes.Tests/`

## Design

- Discover contexts from kubeconfig path and surface them to UI.
- Provide namespace and resource list methods for pods, deployments, helm releases, and ingresses.
- Provide read-only YAML retrieval per supported resource kind.
- Preserve existing kubeconfig-first + Azure fallback auth behavior.

## API / Contracts

### Delivered

- `GetNamespacesAsync(CancellationToken)` — Lists all namespaces from the cluster, returns `IReadOnlyList<string>` sorted alphabetically. Implemented in `KubernetesAksClient` via `CoreV1.ListNamespaceAsync` and in `DemoAksClient` with 7 demo namespaces.
- `GetIngressesAsync(string ns, CancellationToken)` — Lists ingresses in a namespace, returns `IReadOnlyList<IngressInfo>`. Implemented in `KubernetesAksClient` via `NetworkingV1.ListNamespacedIngressAsync` and in `DemoAksClient` with 3 demo ingresses.

### New models (in `AksModels.cs`)

- `IngressInfo` — Name, Namespace, IngressClass, Rules, Addresses, Labels
- `IngressRule` — Host, Paths
- `IngressPath` — Path, PathType, ServiceName, ServicePort

### Planned

- `GetContextsAsync(string? kubeconfigPath)` — Discover kubeconfig contexts
- `GetResourceYamlAsync(...)` — Read-only YAML retrieval per resource kind
- Helm release listing

## Tasks

- [x] Add namespace listing and mapping models
- [x] Add ingress list wrappers and models
- [x] Add demo data for namespaces and ingresses
- [ ] Add context discovery methods using kubeconfig load helpers
- [ ] Add helm release listing
- [ ] Add read-only YAML retrieval for supported kinds
- [ ] Add structured errors for auth, RBAC, and invalid context/namespace
- [ ] Add tests for context parsing and resource calls

## Concern-specific risks

- Helm release metadata shape variation across clusters
- Partial RBAC access by resource type

## Validation

- Unit tests: Planned
- Integration tests: Planned
- Manual checks: Planned (see `test-plan.md`)
