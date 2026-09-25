# AKS

## What Is Supported

The React AKS workspace supports live and demo Kubernetes operations through the .NET sidecar:

- kubeconfig/current-context discovery and context switching;
- single, multiple, or all-namespace scope;
- Deployments, StatefulSets, Pods, Services, Ingresses, GatewayClasses, Gateways, HTTPRoutes, Envoy resources, Helm releases, Jobs, CronJobs, HPAs/KEDA autoscaling, ConfigMaps, Secrets, and Events;
- row sorting/filtering, context menus, YAML viewing/editing, and destructive confirmations;
- restart/scale/delete workload actions;
- Job rerun and CronJob run-now/schedule editing;
- HPA detail, reversible autoscaling pause/resume, and owner-aware KEDA deletion;
- ingress and network-policy analysis;
- Helm history, values, and rollback;
- pod/container details, resolved environment references, and on-demand secret reveal;
- single- and multi-pod log streaming with range selection, filtering, history windows, pause/freeze, copy, and export;
- external pod shell launch and Tauri-owned port-forward sessions; and
- contextual agent entry points scoped to selected Kubernetes resources.

Terminal `Completed`/`Succeeded` pods are hidden by default. GatewayClasses remain cluster-scoped; namespace-aware actions always use the selected row's namespace rather than a toolbar fallback.

## Frontend Architecture

`AksPage` is composed around `AksWorkspaceProvider`. The provider is split into six churn-scoped contexts:

- `useAksCluster` — contexts, namespace discovery, profile/demo connection state;
- `useAksNav` — URL-backed active tab, namespace scope, filters, selections;
- `useAksQueries` — shared pod query data;
- `useAksOps` — refresh timing, fetching state, pause state;
- `useAksOverlays` — confirmation/context-menu state; and
- `useAksActions` — stable shared callbacks.

Most resource tabs subscribe only to stable actions plus their own feature query hook, so the ten-second auto-refresh does not rerender every tab. Resource tables use the shared `ResourceTable` abstraction.

Navigation and selection are URL-backed. Namespace tokens preserve explicit multi-select and `*` all-namespace scope; context changes clear incompatible namespace state. The page publishes bounded screen state for the agent through `useScreenStateProvider`.

## Sidecar and Client Flow

```text
/aks
  → AksWorkspaceProvider
      → hooks in web/src/lib/hooks/useAks.ts
      → /api/aks/* sidecar endpoints
      → IMonitoringConnectionPool resolves demo/live IAksClient
      → KubernetesAksClient or DemoAksClient
      → typed models returned to React Query
```

`AksEndpoints` remains thin: it resolves the current client, validates/binds route data, streams logs as SSE, and delegates resource operations to `IAksClient`. Live behavior is implemented by domain partials under `src/SwebKit.Kubernetes/AksClient/`; demo behavior is split across `DemoAksClient.*.cs` partials.

Multi-namespace client overloads use bounded concurrency. Gateway API queries use custom-object API-version fallback. Kubeconfig exec authentication is primary for Azure-backed clusters, with Azure credential fallback only for recognized broken/unavailable exec-credential failures.

## Refresh and Query Behavior

- Auto-refresh starts enabled at ten seconds and persists both enabled state and interval as view preferences.
- It pauses while side panels or event inspection are open and while the window is hidden.
- Group invalidation uses `web/src/lib/aks-query-keys.ts`; periodic refresh excludes cluster-scoped context/namespace/test queries.
- Explicit Refresh can invalidate the broader AKS query set.
- Expensive diagnostic panels load on demand rather than joining periodic browse refresh.
- Secrets expose key names in listings; values load only when explicitly revealed.

## Logs

Pod logs stream through `/api/aks/{ns}/pods/{pod}/logs/stream` as SSE. `useLogBuffer` maintains a bounded backing buffer while `useLogWindow` presents Older/Newer/Latest slices. Pausing or browsing history does not stop ingestion or shift the visible slice.

Single-pod logs support container selection, previous-container mode, live follow, and bounded progressive `All` history. Multi-pod logs merge per-pod streams chronologically and retain stable pod colors/focus controls. Syntax highlighting is shared through `LogLineText` and theme code tokens.

## YAML and Mutations

The shared `YamlViewer` handles resource YAML viewing, search, editing, validation, and apply. Mutations remain resource-aware and production-sensitive operations are confirmation-gated. HPA/KEDA handling is ownership-aware:

- KEDA-managed HPA pause patches the owning ScaledObject.
- Plain HPA pause freezes current bounds while storing original values for restoration.
- Deleting a KEDA-managed HPA deletes the owning ScaledObject so it is not immediately recreated.

Batch triggers sanitize controller metadata/selectors and annotate provenance on created Jobs.

## Native Desktop Operations

Pod shell and port-forward are desktop-native operations, not sidecar services:

- `web/src/lib/tauri-bridge.ts` invokes Tauri commands;
- `src-tauri/src/pod_shell.rs` launches the external shell;
- Tauri tracks port-forward child processes and exposes start/stop/list commands;
- `PortForwardPanel` reads and controls those sessions.

Browser development uses the bridge's non-Tauri fallbacks and cannot provide full native process behavior.

## Main Code Locations

- `web/src/components/aks/AksPage.tsx`
- `web/src/components/aks/shared/AksWorkspaceContext.tsx`
- `web/src/components/aks/shared/ResourceTable.tsx`
- `web/src/components/aks/*Tab.tsx` — resource surfaces
- `web/src/components/aks/PodDetailPanel.tsx`
- `web/src/components/aks/PodLogView.tsx` / `MultiPodLogView.tsx`
- `web/src/components/aks/shared/useLogBuffer.ts` / `useLogWindow.ts`
- `web/src/components/aks/YamlViewer.tsx`
- `web/src/components/aks/AnalysisPanel.tsx`
- `web/src/components/aks/PortForwardPanel.tsx` / `PodShellPanel.tsx`
- `web/src/lib/hooks/useAks.ts`
- `web/src/lib/aks-query-keys.ts`
- `src-sidecar/Endpoints/AksEndpoints.cs`
- `src-sidecar/Services/SidecarMonitoringConnectionPool.cs`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient*.cs`
- `src/SwebKit.Core/Services/DemoAksClient*.cs`
- `src-tauri/src/pod_shell.rs`

## Validation Pointers

- `web/e2e/aks.spec.ts`
- `web/e2e/aks-context-switch.spec.ts`
- `web/e2e/aks-deferred.spec.ts`
- `tests/SwebKit.Sidecar.Tests/AksEndpointsTests.cs`
- `tests/SwebKit.Sidecar.Tests/AksLogStreamEndpointTests.cs`
- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`
- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientSecretsHelmEventsTests.cs`
- `tests/SwebKit.Kubernetes.Tests/AksExecCredentialDiagnosticsTests.cs`
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
- `tests/SwebKit.Agents.Tests/AksToolsTests.cs`
