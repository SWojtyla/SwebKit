# Status - aks-runtime-diagnostics-depth

---

title: "Status - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is ready for implementation. The next step is to lock the additive `IAksClient` contracts for quota, disruption-budget, probe, placement, and Helm preview data before changing the page layout.

Jira: not linked

Current focus: Wave 1 contract and model definition for namespace and workload constraints.

## Progress checklist

### Wave 1 - namespace and workload constraints

- [ ] Define quota, limit-range, PDB, probe, and placement models in `AksModels.cs`
- [ ] Extend `IAksClient` and `KubernetesAksClient` with additive read methods
- [ ] Decide where each diagnostic appears in the existing panel stack

### Wave 2 - network and ingress diagnostics

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

## Remaining

- Write the additive `IAksClient` and `AksModels.cs` design.
- Align panel layout and auto-refresh pause behavior with the new diagnostics.
- Define focused automated coverage for large namespaces, missing Helm diff support, and evidence wording.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- The page already pauses auto-refresh when detail panels are open. New diagnostics should reuse that behavior instead of inventing special polling rules.
- Evidence wording matters here too. The UI should show what the cluster reports, not over-translate it into certainty.
