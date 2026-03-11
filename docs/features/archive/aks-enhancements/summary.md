# Archive Summary - AKS Enhancements

---

title: "Archive Summary - AKS Enhancements"
owner: ""
completed_date: "2026-03-11"
pr: ""
commit: ""

---

## Goal

Complete the AKS resource browsing experience with kubeconfig context discovery, namespace-scoped workload views, read-only YAML inspection, Helm release listing, and settings simplification.

## Delivered

- **Context discovery**: `GetContextsAsync()` reads kubeconfig and returns all contexts with current-context marking. AksPage context dropdown populated from this, reconnects client on context switch.
- **Namespace-scoped resource views**: Deployments, Pods, Ingresses, and Helm tabs — all loading in parallel per namespace with FluentDataGrid and type-appropriate columns.
- **Read-only YAML viewer**: Slide-out panel with loading/error states. Row-level YAML buttons on Deployments, Pods, and Ingresses. Supports deployment, pod, ingress, and service kinds.
- **Helm release listing**: `GetHelmReleasesAsync()` reads Helm release secrets (label `owner=helm`), extracts name/revision/status/chart. `TryParseChartVersion()` extracts semver from chart labels.
- **AKS settings simplification**: Removed `ExplicitClusterUrl`, `UseAzureCredentialFallback`, and `CredentialRef` from `AksConfig`. Form reduced to three optional fields with inline save feedback and current-config summary. Azure credential fallback is now always automatic.
- **UI/UX overhaul**: Complete rewrite of AksPage and PodLogView with scoped CSS, connection status indicator, collapsible events panel, ready badges, live-tailing pulse, and better monospace font stack.
- **Navigation fixes**: AKS nav icon changed to Kubernetes wheel; Settings nav layout fixed with flexbox.

## Key decisions

- Kubeconfig context discovery via `LoadKubeConfig` — aligns with kubectl behavior, no custom parsing needed.
- Azure credential fallback always automatic — `ShouldUseAzureCredentialFallback` gates on AKS host detection, no user toggle required.
- Settings simplification — removed unused fields (ExplicitClusterUrl, CredentialRef) and the Azure fallback checkbox.
- Helm releases via Secret labels — avoids decoding compressed protobuf; chart version parsed from label string.

## Validation performed

- 24 unit tests passing (`SwebKit.Kubernetes.Tests`): auth helpers, chart version parsing, client configuration, constructor behavior.
- Build succeeds for all projects.

## Lessons learned

- Helm chart labels encode version as `chart-name-X.Y.Z` — regex-free parsing (find last hyphen before digit) is reliable.
- Settings forms need explicit save feedback — Blazor `EventCallback` chain doesn't always trigger visible re-renders at the right time.
- Most "planned" items were already implemented but docs lagged behind — keep docs in sync during implementation.

## Follow-up

- Resizable log/YAML panels for better usability.
- Context menu (right-click) actions instead of inline buttons.
- Mutative operations: pod restart, pod kill, Helm rollback.
- Integration tests with mocked Kubernetes API.

## Archive metadata

- Source: `docs/features/active/aks-enhancements/`
- Predecessor: `docs/features/archive/aks/` (connectivity foundation)
- Follow-up feature: `docs/features/active/aks-enhancements-v2/`
