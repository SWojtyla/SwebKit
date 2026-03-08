# Technical Plan — AKS: Backend

## Status

- Current: Pending

## Architecture

```
SwebKit.Kubernetes
  AksClient/
    KubernetesAksClient   — IAksClient implementation

IAksClient
  GetDeployments, GetPods
  StreamLogs              — IAsyncEnumerable<string>
  PortForward             — returns active tunnel handle
  OpenShell               — WebSocket-backed interactive session
  WatchPods               — IAsyncEnumerable<PodWatchEvent>
```

## Implementation Sequence

1. Implement buffered log streaming pipeline in `KubernetesAksClient`.
2. Implement port-forward session lifecycle (start / stop / query).
3. Add Kubernetes watch adapter with reconnect policy.
4. Implement pod shell WebSocket adapter.
5. Add cross-link contract: AKS → Observability navigation params.

## Detailed Tasks

- [ ] Add buffered `IAsyncEnumerable<string>` log streaming with cancellation.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Add pod restart detection and automatic reconnect logic.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Add port-forward abstraction and session handle type.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Implement watch adapter returning `IAsyncEnumerable<PodWatchEvent>`.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Define cross-link navigation parameter contract.
  - Files: `src/SwebKit.Core/Abstractions/IAksClient.cs`

## Acceptance Checks

- [ ] Log stream emits lines continuously and stops cleanly on cancellation.
- [ ] Port-forward sessions can be started, queried, and stopped.
- [ ] Watch stream updates caller when pod state changes.
- [ ] Shell session forwards stdin/stdout over WebSocket.

## Traceability Backlinks

- `docs/features/aks/index.md`
- `docs/features/aks/technical-plan-ui.md`
- `docs/features/aks/test-plan.md`
