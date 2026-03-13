# AKS

## What Is Supported

- Connect to Kubernetes using default or configured kubeconfig/context.
- Context switching and namespace filtering (single and all namespaces).
- Browse deployments, pods, ingresses, and Helm releases.
- View Kubernetes events with warning highlighting.
- Stream pod logs with filtering.
- View resource YAML.
- Port-forward sessions.
- Pod shell launch.
- Deployment restart, scale operations, and pod delete.
- Helm history, values, and rollback.
- Pod metrics retrieval where available.

## Core Runtime Flow

1. AKS page initializes client from selected environment AKS config.
2. UI loads context list, namespaces, and selected resource collection.
3. Table actions call `IAksClient` operations for mutations and diagnostics.
4. Long-running and side-panel operations keep the main grid responsive.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`

## Important Notes

- `KubernetesAksClient` includes Azure token fallback logic when kubeconfig exec auth is not enough.
- Helm operations are implemented through secret introspection and shelling out to `helm` for some commands.
- Port-forward lifecycle is tracked with process registry helpers and must be cleaned up on stop.

## Validation Pointers

- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
