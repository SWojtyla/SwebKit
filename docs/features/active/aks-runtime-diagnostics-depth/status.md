# Status - aks-runtime-diagnostics-depth

---

title: "Status - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-15"

---

## Quick summary

Wave 2 now extends the existing `/aks` surface with evidence-first ingress and network-policy diagnostics. The AKS toolbar groups network resources behind an expandable `Network` menu, Services remain browseable as a first-class AKS resource, HTTPRoute rows stay visible when route cells wrap, and workload or ingress rows can now open self-loading analysis panels from the same page.

Jira: not linked

Current focus: carry the same evidence-first approach into the remaining namespace, workload-constraint, and Helm-preview waves without fragmenting the existing AKS page model.

## Progress checklist

### Wave 1 - namespace and workload constraints

- [ ] Define quota, limit-range, PDB, probe, and placement models in `AksModels.cs`
- [ ] Extend `IAksClient` and `KubernetesAksClient` with additive read methods
- [ ] Decide where each diagnostic appears in the existing panel stack

### Wave 2 - network and ingress diagnostics

- [x] Group network-oriented AKS resources behind an expandable `Network` menu
- [x] Add Services as a first-class AKS browse and YAML surface
- [x] Keep HTTPRoute browse stable when several route rows are present
- [x] Define network policy and ingress analysis models
- [x] Decide how far analysis should go without implying packet-level certainty
- [x] Add drill points from workloads and ingresses into the new panels

### Wave 3 - Helm preview

- [ ] Define Helm diff capability detection and fallback behavior
- [ ] Define preview UX and test strategy for plugin-present and plugin-missing environments

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

- Write the additive `IAksClient` and `AksModels.cs` design.
- Add the remaining namespace and workload-constraint diagnostics (quota, limit-range, PDB, probe, placement).
- Add Helm preview capability detection and read-only preview behavior.
- Broaden Kubernetes-client coverage for ingress and network-policy edge cases beyond the current focused validation slice.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Focused AKS diagnostics tests passed on 2026-04-15 (`AksPageBatchTests`, `AksDetailPanelsTests`, `DemoAksClientTests`, `AksTimelineSignalSourceTests`)

## Notes

- The page already pauses auto-refresh when detail panels are open. New diagnostics should reuse that behavior instead of inventing special polling rules.
- Evidence wording matters here too. The UI should show what the cluster reports, not over-translate it into certainty.
- Wave 2 diagnostics load on demand from the side-panel components instead of joining the main browse-data cache and periodic refresh path.
