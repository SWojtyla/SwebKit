# Backend Plan - aks

---

title: "Backend Plan - aks"
owner: ""
status: "Planned"

---

## Goal

Deliver a reliable AKS/Kubernetes read-only data layer that loads cluster context from kubeconfig files and serves namespace-scoped views for pods, deployments, Helm releases, and ingresses, including raw YAML retrieval for each resource.

## Impacted areas

- Projects / services: `src/SwebKit.Kubernetes/`, `src/SwebKit.Core/Abstractions/`, `src/SwebKit.Core/Domain/`
- External dependencies: `KubernetesClient` package and kubeconfig file loading
- Optional storage: user-selected kubeconfig path in app state/ui state

## Design

Use `IAksClient` as the boundary for AKS operations and keep all Kubernetes API details in `SwebKit.Kubernetes`. The client should:

- Load kubeconfig from default path or user-selected file.
- Resolve current context and available namespaces.
- Query namespace-scoped resources (pods, deployments, ingresses).
- List Helm releases by reading Helm-managed Kubernetes secrets/configmaps.
- Fetch raw YAML for selected resources on demand.

## API / Contracts

- `LoadKubeConfigAsync(string? filePath)` -> loads kubeconfig and available contexts
- `GetNamespacesAsync(string contextName)` -> returns namespace list
- `GetPodsAsync(string contextName, string namespaceName)`
- `GetDeploymentsAsync(string contextName, string namespaceName)`
- `GetHelmReleasesAsync(string contextName, string namespaceName)`
- `GetIngressesAsync(string contextName, string namespaceName)`
- `GetResourceYamlAsync(string contextName, string namespaceName, string kind, string name)`

Backward compatibility notes:

- Existing AKS client methods for logs/terminal remain available but are not in scope for this feature plan.

## Tasks

- [ ] Define/update contracts for kubeconfig/context/namespace/resource listing and YAML retrieval
- [ ] Implement kubeconfig load + context switching in `KubernetesAksClient`
- [ ] Implement namespace list and active namespace query methods
- [ ] Implement pods/deployments/helm releases/ingresses list methods
- [ ] Implement YAML retrieval method for each supported resource kind
- [ ] Add/update error handling for invalid kubeconfig, auth failures, and RBAC-denied resources
- [ ] Add/update logging and telemetry around cluster calls and failures
- [ ] Add/update unit and integration tests in `tests/SwebKit.Kubernetes.Tests/`

## Migration and runtime changes

- No database migration required
- Runtime config: support explicit kubeconfig file path selection and default kubeconfig fallback
- Operational notes: clearly surface context, namespace, and RBAC errors in user-facing messages

## Validation

- Unit tests: Planned
- Integration tests: Planned
- Manual checks:
  - Load kubeconfig from default and custom file path
  - Switch context and verify namespace list updates
  - List pods/deployments/helm releases/ingresses for selected namespace
  - Open YAML view for each resource kind

## Notes

- Helm release listing should gracefully handle clusters that do not use Helm secrets/configmaps in the expected format.
- Keep all operations read-only in this phase; no mutate/delete/apply in backend contracts.
