# Backend Plan - aks-runtime-diagnostics-depth

---

title: "Backend Plan - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "Planned"

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
- Planned additive contracts:
- `NamespaceQuotaInfo`, `LimitRangeInfo`, `PodDisruptionBudgetInfo`, `ProbeFailureSummary`, `PlacementAnalysis`, `NetworkPolicyAnalysis`, and `HelmDiffPreview` in `AksModels.cs`
- Planned additive client methods:
- `GetResourceQuotasAsync`
- `GetLimitRangesAsync`
- `GetPodDisruptionBudgetsAsync`
- `GetProbeFailureSummaryAsync` or equivalent bounded summary retrieval
- `GetPlacementAnalysisAsync`
- `GetNetworkPoliciesAsync` and `AnalyzeIngressAsync`
- `PreviewHelmUpgradeAsync` or `GetHelmDiffAsync`

## Design

The backend should return typed evidence summaries instead of leaving the UI to infer diagnostics from raw objects:

1. Namespace diagnostics should aggregate `ResourceQuota` and `LimitRange` objects for the current namespace.
2. Workload diagnostics should combine workload spec, pod status, and recent Kubernetes events into a bounded probe or placement summary.
3. Network and ingress diagnostics should inspect Kubernetes objects that are already available through the API server and explain what they imply, while remaining explicit about what they cannot prove.
4. Helm preview should remain read-only. If the environment cannot produce a full diff, the backend should return an explicit capability or fallback state instead of failing opaquely.

## API / Contracts

- Additive model changes in `AksModels.cs` should prefer small, UI-friendly summaries plus optional raw supporting items.
- `IAksClient` should stay additive and read-oriented. No policy mutation APIs are planned.
- Probe and placement diagnostics may need helper models that preserve both summary text and supporting event records.
- Helm preview should distinguish full diff, degraded preview, and unsupported states explicitly so the UI can render them safely.
- Backward compatibility:
- Existing AKS browsing, log streaming, YAML, HPA, Jobs, and Helm history contracts remain intact.
- New diagnostics should not force current callers to know about the new models until they opt in.

## Tasks

### Wave 1 - namespace and workload diagnostics [dotnet-expert]

- [ ] Extend `AksModels.cs` with quota, limit, PDB, probe, and placement types.
- [ ] Extend `IAksClient` and `KubernetesAksClient` to retrieve those models.
- [ ] Decide what should be summarized server-side versus left as supporting detail.

### Wave 2 - network and ingress diagnostics [dotnet-expert]

- [ ] Add network policy and ingress analysis contracts.
- [ ] Implement bounded object reads and summary logic in `KubernetesAksClient`.
- [ ] Ensure unsupported resource types or missing permissions degrade clearly.

### Wave 3 - Helm preview [dotnet-expert]

- [ ] Add preview capability detection and typed preview output.
- [ ] Decide whether full diff requires external plugin support and how fallback preview behaves.
- [ ] Add deterministic tests for supported and unsupported paths.

## Migration and runtime changes

- No persistent-data migration is required.
- Helm preview may add a runtime dependency on diff support or a fallback shell path; the implementation must report that explicitly.
- Demo mode should either provide representative fixtures or clearly mark diagnostics as unavailable.

## Validation

- Unit tests: Not started. Add summary or helper tests in `tests/SwebKit.Core.Tests` if shared analysis logic is introduced.
- Integration tests: Not started. Extend `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs` heavily for the new client surface.
- Manual checks: verify that unsupported Helm preview capability and missing cluster objects degrade clearly.

## Notes

- `dotnet-csharp.md` cancellation guidance applies to any new list or watch-like calls. Do not swallow cancellation under broad catch blocks.
- Because the page already pauses auto-refresh when panels are open, the backend can favor deterministic point-in-time snapshots over live streams for these diagnostics.
