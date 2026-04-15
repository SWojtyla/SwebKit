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

Wave 2 UI work has started on the existing `/aks` surface. The AKS toolbar now groups network resources behind an expandable `Network` menu, Services are browseable as a first-class AKS resource, and the HTTPRoute grid has been hardened so variable-height route rows do not disappear when several routes are present.

Jira: not linked

Current focus: keep extending the AKS page toward evidence-first network and ingress diagnostics without fragmenting the page navigation.

## Progress checklist

### Wave 1 - namespace and workload constraints

- [ ] Define quota, limit-range, PDB, probe, and placement models in `AksModels.cs`
- [ ] Extend `IAksClient` and `KubernetesAksClient` with additive read methods
- [ ] Decide where each diagnostic appears in the existing panel stack

### Wave 2 - network and ingress diagnostics

- [x] Group network-oriented AKS resources behind an expandable `Network` menu
- [x] Add Services as a first-class AKS browse and YAML surface
- [x] Keep HTTPRoute browse stable when several route rows are present
- [ ] Define network policy and ingress analysis models
- [ ] Decide how far analysis should go without implying packet-level certainty
- [ ] Add drill points from pods and ingresses into the new panels

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

## Remaining

- Write the additive `IAksClient` and `AksModels.cs` design.
- Align panel layout and auto-refresh pause behavior with the new diagnostics.
- Define focused automated coverage for large namespaces, missing Helm diff support, and evidence wording.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Targeted AKS component and client tests in progress

## Notes

- The page already pauses auto-refresh when detail panels are open. New diagnostics should reuse that behavior instead of inventing special polling rules.
- Evidence wording matters here too. The UI should show what the cluster reports, not over-translate it into certainty.
