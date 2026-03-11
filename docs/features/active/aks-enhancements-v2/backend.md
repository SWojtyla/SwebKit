# Backend Plan - AKS Enhancements v2

---

title: "Backend Plan - AKS Enhancements v2"
owner: ""
status: "Planned"

---

## Goal

Extend `IAksClient` with mutative operations, Helm release inspection, pod metrics, and multi-namespace support, while keeping safety guards for production environments.

## Impacted areas

- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `tests/SwebKit.Kubernetes.Tests/`

## Design

- Add mutative methods to `IAksClient` that mirror `kubectl` semantics.
- All mutative operations accept a `CancellationToken` and throw on failure.
- The UI layer (not the client) is responsible for production guard confirmations.

## API / Contracts

### New methods on `IAksClient`

- `RestartDeploymentAsync(string ns, string deploymentName, CancellationToken)` — Triggers a rollout restart by patching the deployment's pod template annotation with a timestamp (same as `kubectl rollout restart`).
- `ScaleDeploymentAsync(string ns, string deploymentName, int replicas, CancellationToken)` — Patches deployment replica count.
- `DeletePodAsync(string ns, string podName, CancellationToken)` — Deletes a pod (graceful termination). Used for "kill pod" action.
- `GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken)` — Returns revision history for a Helm release by querying all secrets for that release name. Returns `IReadOnlyList<HelmRevisionInfo>`.
- `GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken)` — Decodes the Helm release secret data to extract computed values YAML. Returns `string` (YAML).
- `RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken)` — Invokes `helm rollback` via CLI subprocess (Helm SDK doesn't expose rollback as a library call).
- `GetPodMetricsAsync(string ns, CancellationToken)` — Queries the Kubernetes Metrics API (`metrics.k8s.io/v1beta1`) for CPU and memory usage per pod. Returns `IReadOnlyList<PodMetrics>`. Fails gracefully (returns empty list) if Metrics API is not installed on the cluster.
- `GetPodsAsync` and `GetDeploymentsAsync` overloads accepting `IReadOnlyList<string> namespaces` — Multi-namespace variants that execute parallel calls per namespace and merge results with namespace column. Falls back to single-namespace signature for backward compatibility.

### New models

- `HelmRevisionInfo` — Revision number, Status, Chart, AppVersion, Updated timestamp, Description (install/upgrade/rollback note).
- `PodMetrics` — PodName, Namespace, Containers (list of `ContainerMetrics`: Name, CpuCores, MemoryBytes).

### DemoAksClient extensions

- All new methods return realistic demo data or simulate delays.
- `DeletePodAsync` removes the pod from the in-memory list.
- `RestartDeploymentAsync` resets pod ages.
- `RollbackHelmReleaseAsync` updates the release status to the target revision.

## Tasks

- [x] Add `DeletePodAsync` to `IAksClient` and implement in both clients
- [x] Add `RestartDeploymentAsync` to `IAksClient` and implement (patch pod template annotation)
- [ ] Add `ScaleDeploymentAsync` to `IAksClient` and implement
- [ ] Add `GetHelmReleaseHistoryAsync` to `IAksClient` and implement (query all secrets per release)
- [ ] Add `GetHelmReleaseValuesAsync` to `IAksClient` and implement (decode release secret data)
- [ ] Add `RollbackHelmReleaseAsync` to `IAksClient` and implement (CLI subprocess)
- [ ] Add `HelmRevisionInfo` model
- [ ] Add `GetPodMetricsAsync` to `IAksClient` and implement (Metrics API with graceful fallback)
- [ ] Add `PodMetrics` and `ContainerMetrics` models
- [ ] Add multi-namespace overloads for `GetPodsAsync` and `GetDeploymentsAsync`
- [ ] Update `DemoAksClient` with demo data for all new methods
- [ ] Add unit tests for new methods and edge cases

## Concern-specific risks

- Helm rollback via CLI subprocess — requires `helm` binary on PATH; fail gracefully with clear error if missing
- Pod delete is irreversible — UI must confirm before calling
- Deployment restart annotation patch must not conflict with GitOps controllers
- Metrics API may not be installed — `GetPodMetricsAsync` must return empty list, not throw
- Multi-namespace queries can be slow — parallel execution with per-namespace timeout

## Validation

- Unit tests: Planned
- Integration tests: Planned (requires cluster or mocked API)
- Manual checks: Planned (see `test-plan.md`)
