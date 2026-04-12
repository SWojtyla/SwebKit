# Feature Overview - aks-runtime-diagnostics-depth

---

title: "Feature Overview - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Deepen the existing `/aks` page so operators can explain namespace, pod, ingress, and Helm issues from the same diagnostics surface by adding quota and limit visibility, disruption-budget status, probe-failure surfacing, network and ingress analysis, placement-constraint explanation, and Helm diff or upgrade preview.

## Value

The AKS page already covers resource grids, logs, YAML, port-forward, Jobs, CronJobs, HPA, and Helm history. What it still lacks are the higher-signal diagnostics that usually force operators back to `kubectl describe`, `kubectl get pdb`, `kubectl get quota`, and `helm diff`: why a pod is unschedulable, whether probe failures are accumulating, whether a PodDisruptionBudget is blocking action, whether network policy or ingress wiring is a likely constraint, and what a Helm change would actually introduce.

This feature keeps the page evidence-driven and read-oriented. It should explain what the cluster currently says, not pretend to emulate the scheduler or prove a network root cause.

## Scope

- Wave 1 - namespace and workload constraint visibility.
- Surface namespace `ResourceQuota` and `LimitRange` information alongside the selected namespace.
- Surface PodDisruptionBudget status relevant to selected workloads.
- Summarize recent readiness and liveness probe failures from pod status and Kubernetes events.
- Summarize placement constraints from node selectors, affinities, tolerations, topology spread constraints, and recent scheduling failures.
- Wave 2 - network and ingress diagnostics.
- Add network policy visibility for the selected namespace or workload.
- Add ingress analysis that shows host and path routing, backend service mapping, missing endpoints, and obvious TLS or rule mismatches.
- Keep the analysis evidence-based and scoped to what Kubernetes objects and events can prove.
- Wave 3 - Helm diff and upgrade preview.
- Add a read-only diff or preview surface before upgrade or rollback actions.
- Reuse existing Helm panel patterns where possible and expose capability limits when CLI support is unavailable.
- Out of scope.
- Applying or mutating quotas, limit ranges, network policies, or PDBs from SwebKit.
- Packet-level traffic tracing or full network-policy simulation.
- Full scheduler emulation beyond observed constraints and events.
- Automatic remediation or one-click repair actions.

## Dependencies

- Existing AKS feature base: `docs/architecture/functionalities/aks.md`.
- Existing route and components: `/aks`, `AksPage`, `AksDetailPanels`, `PodGrid`, `IngressGrid`, `HelmGrid`, and related detail panels.
- Existing contracts and models: `IAksClient`, `IAksClientBootstrapper`, `AksModels.cs`, `KubernetesAksClient.cs`, and `DemoAksClient.cs`.
- Cross-feature alignment: `incident-timeline-workbench` can later consume clearer probe and scheduling evidence, but this feature does not depend on Incident Timeline changes to be valuable.
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/agent-workflow.md`.

## Risks & mitigations

- Risk: probe, placement, or network summaries could overstate certainty. Mitigation: frame every summary as observed evidence or likely constraint based on current objects and events.
- Risk: Helm diff preview depends on external tooling availability. Mitigation: keep preview read-only, show capability detection explicitly, and provide a degraded fallback when full diff is unavailable.
- Risk: extra diagnostics panels overload the page. Mitigation: extend the existing panel model instead of spawning a new route.
- Risk: cluster queries become too expensive in large namespaces. Mitigation: scope calls to the selected namespace or workload, reuse existing auto-refresh pause behavior, and prefer bounded summaries over wide raw lists.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- AKS functionality: `docs/architecture/functionalities/aks.md`
- Incident Timeline functionality: `docs/architecture/functionalities/incident-timeline.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
