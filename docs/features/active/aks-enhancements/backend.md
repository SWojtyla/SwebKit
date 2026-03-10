# Backend Plan - AKS Enhancements

---

title: "Backend Plan - AKS Enhancements"
owner: ""
status: "Planned"

---

## Goal

Expand AKS backend capabilities from connectivity-only to full namespace-scoped resource and YAML retrieval workflows.

## Impacted areas

- `src/SwebKit.Kubernetes/`
- `src/SwebKit.Core/Abstractions/`
- `src/SwebKit.Core/Domain/`
- `tests/SwebKit.Kubernetes.Tests/`

## Design

- Discover contexts from kubeconfig path and surface them to UI.
- Provide namespace and resource list methods for pods, deployments, helm releases, and ingresses.
- Provide read-only YAML retrieval per supported resource kind.
- Preserve existing kubeconfig-first + Azure fallback auth behavior.

## API / Contracts

Planned additions or updates:

- `GetContextsAsync(string? kubeconfigPath)`
- `GetNamespacesAsync(...)`
- Resource list calls for pods, deployments, helm releases, ingresses
- `GetResourceYamlAsync(...)`

## Tasks

- [ ] Add context discovery methods using kubeconfig load helpers
- [ ] Add namespace listing and mapping models
- [ ] Add pods/deployments/helm/ingress list wrappers
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
