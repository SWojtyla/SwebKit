# Archive Summary - AKS Connectivity Foundation

---

title: "Archive Summary - AKS Connectivity Foundation"
owner: ""
completed_date: "2026-03-10"
pr: ""
commit: ""

---

## Goal

Deliver a usable AKS foundation that reads kubeconfig, supports context and namespace configuration in-app, and remains compatible with Azure credentials when kubeconfig token auth is missing.

## Delivered

- Kubeconfig-first AKS client construction with explicit kubeconfig path and context support.
- Azure Identity fallback for AKS token acquisition when kubeconfig auth has no access token.
- AKS page controls for kubeconfig path, context, namespace, and session apply/save reconnect flows.
- AKS settings form fields for kubeconfig path and Azure fallback toggle.
- Unit tests for AKS auth helper behavior (server-id parsing, scope construction, fallback gating).

## Key decisions

- Kubeconfig is the AKS source of truth for cluster connectivity.
- Azure credential fallback is additive, not a replacement for kubeconfig/exec auth.
- Operations in this phase remain read-only.

## Validation performed

- `dotnet test tests/SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj -v minimal` (all tests passed).
- `dotnet build src/SwebKit.App/SwebKit.App.csproj -t:Build -p:Configuration=Debug -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None` (build succeeded).

## Lessons learned

- Kubeconfig `--server-id` parsing must handle YAML list-style args where the value is on the next line.
- AKS auth reliability improves materially when kubeconfig auth remains primary and Azure fallback is only used when needed.

## Follow-up

- Implement context discovery and selector from kubeconfig (dropdown instead of free text).
- Implement namespace/resource browser scope (pods, deployments, helm releases, ingresses) and YAML viewer.
- Expand AKS tests to cover follow-up resource-browser paths.

## Archive metadata

- Source archived from: `docs/features/active/aks/`
- Follow-up feature: `docs/features/active/aks-enhancements/`
