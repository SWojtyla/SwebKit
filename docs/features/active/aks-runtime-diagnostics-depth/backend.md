# Backend Plan - aks-runtime-diagnostics-depth

---

title: "Backend Plan - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "In Progress"

---

## Goal

Extend the AKS client and models so the existing page can retrieve higher-signal runtime diagnostics and Helm preview data as typed, bounded, read-only summaries.

## Impacted areas

- Existing abstractions and models:
- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- Existing implementations:
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- Existing downstream consumer that may later benefit from clearer evidence:
- `src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs`
- Implemented Wave 2 additive contracts:
- `IngressAnalysis`
- `IngressBackendAnalysis`
- `NetworkPolicyAnalysis`
- `NetworkPolicyMatch`
- Planned later additive contracts:
- `NamespaceQuotaInfo`, `LimitRangeInfo`, `PodDisruptionBudgetInfo`, `ProbeFailureSummary`, `PlacementAnalysis`, and `HelmDiffPreview` in `AksModels.cs`
- Implemented Wave 2 additive client methods:
- `AnalyzeIngressAsync`
- `AnalyzeNetworkPoliciesAsync`
- Planned later additive client methods:
- `GetResourceQuotasAsync`
- `GetLimitRangesAsync`
- `GetPodDisruptionBudgetsAsync`
- `GetProbeFailureSummaryAsync` or equivalent bounded summary retrieval
- `GetPlacementAnalysisAsync`
- `PreviewHelmUpgradeAsync` or `GetHelmDiffAsync`

## Design

The backend should return typed evidence summaries instead of leaving the UI to infer diagnostics from raw objects:

1. Namespace diagnostics should aggregate `ResourceQuota` and `LimitRange` objects for the current namespace.
2. Workload diagnostics should combine workload spec, pod status, and recent Kubernetes events into a bounded probe or placement summary.
3. Network and ingress diagnostics should inspect Kubernetes objects that are already available through the API server and explain what they imply, while remaining explicit about what they cannot prove.
4. Helm preview should remain read-only. If the environment cannot produce a full diff, the backend should return an explicit capability or fallback state instead of failing opaquely.
5. Wave 2 diagnostics should remain point-in-time reads. They do not join the page-level refresh cache and are loaded only when a panel requests them.

## API / Contracts

- Additive model changes in `AksModels.cs` should prefer small, UI-friendly summaries plus optional raw supporting items.
- `IAksClient` should stay additive and read-oriented. No policy mutation APIs are planned.
- Wave 2 analysis contracts include limitation text so the UI can stay explicit about what the backend did and did not prove.
- Probe and placement diagnostics may need helper models that preserve both summary text and supporting event records.
- Helm preview should distinguish full diff, degraded preview, and unsupported states explicitly so the UI can render them safely.
- Backward compatibility:
- Existing AKS browsing, log streaming, YAML, HPA, Jobs, and Helm history contracts remain intact.
- New diagnostics should not force current callers to know about the new models until they opt in.

## Tasks

### Wave 1 - namespace and workload diagnostics [dotnet-expert]

- [x] Extend `AksModels.cs` with quota, limit, PDB, probe, and placement types.
- [x] Extend `IAksClient` and `KubernetesAksClient` to retrieve those models.
- [x] Decide what should be summarized server-side versus left as supporting detail.

### Wave 2 - network and ingress diagnostics [dotnet-expert]

- [x] Add network policy and ingress analysis contracts.
- [x] Implement bounded object reads and summary logic in `KubernetesAksClient`.
- [x] Ensure unsupported resource types or missing permissions degrade clearly.

### Wave 3 - Helm preview [dotnet-expert]

- [x] Add preview capability detection and typed preview output.
- [x] Decide whether full diff requires external plugin support and how fallback preview behaves.
- [x] Add deterministic tests for supported and unsupported paths.

## Migration and runtime changes

- No persistent-data migration is required.
- Helm preview may add a runtime dependency on diff support or a fallback shell path; the implementation must report that explicitly.
- Demo mode should either provide representative fixtures or clearly mark diagnostics as unavailable.

## Validation

- Unit tests: Focused demo-mode coverage added in `tests/SwebKit.Core.Tests/DemoAksClientTests.cs` for both new Wave 2 analysis methods.
- Integration tests: The live client build and `tests/SwebKit.Kubernetes.Tests/AksTimelineSignalSourceTests.cs` compatibility slice passed on 2026-04-15. Direct `KubernetesAksClient` analysis-edge tests are still outstanding.
- Manual checks: verify that unsupported Helm preview capability and missing cluster objects degrade clearly.

## Notes

- `dotnet-csharp.md` cancellation guidance applies to any new list or watch-like calls. Do not swallow cancellation under broad catch blocks.
- Because the page already pauses auto-refresh when panels are open, the backend can favor deterministic point-in-time snapshots over live streams for these diagnostics.
