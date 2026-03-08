---
title: "Technical Plan â€” AKS: Backend"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan â€” AKS: Backend

## Status

- Current: Pending

## Architecture

```
SwebKit.Kubernetes
  AksClient/
    KubernetesAksClient   â€” IAksClient implementation

IAksClient
  GetDeployments, GetPods
  StreamLogs              â€” IAsyncEnumerable<string>
  PortForward             â€” returns active tunnel handle
  OpenShell               â€” WebSocket-backed interactive session
  WatchPods               â€” IAsyncEnumerable<PodWatchEvent>
```

## Implementation Sequence

1. Implement buffered log streaming pipeline in `KubernetesAksClient`.
2. Implement port-forward session lifecycle (start / stop / query).
3. Add Kubernetes watch adapter with reconnect policy.
4. Implement pod shell WebSocket adapter.
5. Add cross-link contract: AKS â†’ Observability navigation params.

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

- `docs/features/active/aks/index.md`
- `docs/features/active/aks/technical-plan-ui.md`
- `docs/features/active/aks/test-plan.md`

