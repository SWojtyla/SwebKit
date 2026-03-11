# Backend Plan - AKS Enhancements

---

title: "Backend Plan - AKS Enhancements"
owner: ""
status: "Complete"

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
- Preserve existing kubeconfig-first + Azure fallback auth behavior (always automatic, no user toggle).

## API / Contracts

### Delivered

- `GetNamespacesAsync(CancellationToken)` — Lists all namespaces from the cluster, returns `IReadOnlyList<string>` sorted alphabetically. Implemented in `KubernetesAksClient` via `CoreV1.ListNamespaceAsync` and in `DemoAksClient` with 7 demo namespaces.
- `GetIngressesAsync(string ns, CancellationToken)` — Lists ingresses in a namespace, returns `IReadOnlyList<IngressInfo>`. Implemented in `KubernetesAksClient` via `NetworkingV1.ListNamespacedIngressAsync` and in `DemoAksClient` with 3 demo ingresses.

### Models (in `AksModels.cs`)

- `IngressInfo` — Name, Namespace, IngressClass, Rules, Addresses, Labels
- `IngressRule` — Host, Paths
- `IngressPath` — Path, PathType, ServiceName, ServicePort
- `HelmReleaseInfo` — Name, Namespace, Chart, AppVersion, ChartVersion, Status, Revision, Updated
- `KubeContextInfo` — Name, Cluster, User, Namespace, IsCurrent

- `GetContextsAsync(CancellationToken)` — Reads kubeconfig and returns all contexts with current-context marking. Returns `IReadOnlyList<KubeContextInfo>`. Implemented in `KubernetesAksClient` (via `KubernetesClientConfiguration.LoadKubeConfig`) and `DemoAksClient` (5 demo contexts).
- `GetHelmReleasesAsync(string ns, CancellationToken)` — Lists Helm releases by querying Secrets with label `owner=helm`. Extracts name, revision, status, chart, and chart version. Returns `IReadOnlyList<HelmReleaseInfo>`. `DemoAksClient` returns 8 demo releases.
- `GetResourceYamlAsync(string ns, string kind, string name, CancellationToken)` — Read-only YAML retrieval for deployment, pod, ingress, and service kinds. Returns serialized YAML string via `KubernetesYaml.Serialize`.
- `TryParseChartVersion(string? chart)` — Internal helper that extracts semver from Helm chart label (e.g. `ingress-nginx-4.9.1` → `4.9.1`).

## Tasks

- [x] Add namespace listing and mapping models
- [x] Add ingress list wrappers and models
- [x] Add demo data for namespaces and ingresses
- [x] Simplify `AksConfig` (removed `ExplicitClusterUrl`, `UseAzureCredentialFallback`, `CredentialRef`; Azure fallback is now always automatic)
- [x] Simplify `KubernetesAksClient` constructor (two params: context, path; fallback always applied)
- [x] Add context discovery via `GetContextsAsync()` using `LoadKubeConfig`
- [x] Add helm release listing via `GetHelmReleasesAsync()` with `TryParseChartVersion()`
- [x] Add read-only YAML retrieval for deployment, pod, ingress, service kinds
- [x] Add tests for auth helpers, chart version parsing, client configuration (24 passing)

## Concern-specific risks

- Helm release metadata shape variation across clusters
- Partial RBAC access by resource type

## Validation

- Unit tests: 24 passing (`dotnet test tests/SwebKit.Kubernetes.Tests/`)
- Integration tests: Deferred (requires live cluster)
- Manual checks: Pending (see `test-plan.md`)
