# Status - aks-runtime-diagnostics-depth

---

title: "Status - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-17"

---

## Quick summary

All three waves are now complete. Wave 1 adds namespace quota/limit-range, PDB, probe-failure, and placement-constraint diagnostics. Wave 2 adds ingress and network-policy analysis panels. Wave 3 adds a read-only Helm diff preview with capability detection.

Jira: not linked

Current focus: manual validation on a live cluster.

## Progress checklist

### Wave 1 - namespace and workload constraints

- [x] Define quota, limit-range, PDB, probe, and placement models in `AksModels.cs`
- [x] Extend `IAksClient` and `KubernetesAksClient` with additive read methods
- [x] Add `NamespaceQuotaPanel`, `PodDisruptionBudgetPanel`, `ProbeFailurePanel`, `PlacementConstraintsPanel` Razor components
- [x] Wire entry points from deployment/statefulset context menus

### Wave 2 - network and ingress diagnostics

- [x] Group network-oriented AKS resources behind an expandable `Network` menu
- [x] Add Services as a first-class AKS browse and YAML surface
- [x] Keep HTTPRoute browse stable when several route rows are present
- [x] Define network policy and ingress analysis models
- [x] Decide how far analysis should go without implying packet-level certainty
- [x] Add drill points from workloads and ingresses into the new panels

### Wave 3 - Helm preview

- [x] Define Helm diff capability detection and fallback behavior
- [x] Add `HelmDiffPreview` model and `PreviewHelmUpgradeAsync` method
- [x] Implement in `KubernetesAksClient` (Full / Degraded / Unsupported) and `DemoAksClient`
- [x] Add `HelmDiffPreviewPanel` Razor component with capability-aware rendering
- [x] Add Helm context menu entry in `AksPage.razor`

## Completed

- Confirmed the feature should deepen the current `/aks` route instead of creating a separate diagnostics experience.
- Identified namespace constraints, probe failures, network policy or ingress analysis, placement constraints, and Helm preview as the highest-value gaps.
- Scoped the feature toward evidence summaries and away from scheduler or network simulation.
- Grouped Services, Ingresses, and Gateway API resources under an expandable `Network` menu in the AKS toolbar.
- Added Services browse support to the AKS page, demo client, live client, and page-level YAML flow.
- Switched the HTTPRoute grid onto a non-virtualized render path so later route rows stay visible when route cells wrap.
- Added typed ingress and network-policy analysis contracts to `AksModels.cs` and additive analysis methods to `IAksClient`.
- Implemented ingress and workload-scoped network analysis in both `KubernetesAksClient` and `DemoAksClient` with explicit limitation wording.
- Added self-loading `IngressAnalysisPanel` and `NetworkPolicyAnalysisPanel` surfaces to the existing AKS side-panel column.
- Added row, context-menu, and keyboard diagnostics entry points for Deployments, StatefulSets, Pods, and Ingresses.
- Extended focused AKS app, core, and Kubernetes test coverage for the new diagnostics paths.

## Remaining

- Manual validation on a live cluster: verify all 5 new diagnostic panels surface correct cluster evidence and degrade cleanly on permissions errors.
- Broaden `KubernetesAksClient` direct-client tests for new Wave 1 methods (quota, limit-range, PDB, probe, placement) — currently covered by demo-mode unit tests only.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: All 800 unit/component tests passing on 2026-04-17
- Wave 1+2+3 covered by demo-mode tests in `DemoAksClientTests` (6 new Wave 1+3 tests, all passing)
- `AksPageBatchTests`, `AksDetailPanelsTests`, `AksTimelineSignalSourceTests` all passing

## Notes

- The page already pauses auto-refresh when detail panels are open. New diagnostics should reuse that behavior instead of inventing special polling rules.
- Evidence wording matters here too. The UI should show what the cluster reports, not over-translate it into certainty.
- Wave 2 diagnostics load on demand from the side-panel components instead of joining the main browse-data cache and periodic refresh path.
