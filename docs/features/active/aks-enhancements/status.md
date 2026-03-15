# Status — AKS New Capabilities

---

title: "Status - AKS New Capabilities"
owner: ""
state: "In Review"
branch: ""
started: ""
last_updated: "2026-06-27"

---

## Quick summary

Current state: In Review — all 6 features implemented across backend, frontend, and tests. Build clean, 30/30 unit tests passing.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation
- [x] Frontend implementation
- [x] Tests (unit / integration / manual)
- [ ] Docs aligned (architecture/functionalities/aks.md pending)
- [ ] Ready for review

## Completed

- Feature scoping and planning
- `backend.md`, `frontend.md`, `test-plan.md`, `decisions.md` authored
- New model types in `AksModels.cs` (10 types: AggregatedLogLine, StatefulSetInfo, ConfigMapInfo, SecretInfo, ContainerDetail, ResourceRequirements, EnvVarSourceKind, EnvVarDetail, HpaInfo, HpaMetricStatus, HpaCondition)
- Extended `IAksClient` with 9 new method signatures + multi-namespace StatefulSets overload
- `DemoAksClient` — all 9 new methods with realistic demo data, build clean
- `KubernetesAksClient` — all 9 new methods with real k8s API calls, build clean
- Feature 1: Multi-pod log aggregation — `MultiPodLogView.razor` + `AksPage.razor` "Logs for all pods" action + `OnCtxAllPodsLogs/StatefulSet`
- Feature 2: StatefulSets tab — `GetStatefulSetsAsync`, grid UI, restart/scale context menu actions
- Feature 3: ConfigMap/Secret viewer — `ConfigMapDetailPanel.razor`, `SecretDetailPanel.razor`, context menu actions
- Feature 4: Container image/env quick-view — `ContainerDetailPanel.razor`, context menu from pod and deployment
- Feature 5: HPA inline status — `GetHpasAsync`, HPA badge columns in Deployments/StatefulSets grids, HPA detail panel
- Feature 6: Open shell in pod — `OnCtxOpenPodShell` wired to existing `OpenShellAsync`
- `AksPage.razor` fully updated: ResourceTypes, grids, panels, context menus, LoadAsync, all new handlers, CloseAllMenus
- Unit tests: 12 new tests in `DemoAksClientTests` covering all new methods (30/30 pass)

## Remaining

- Manual validation per `test-plan.md`
- Update `docs/architecture/functionalities/aks.md`

## Blockers

None.

## Validation

Unit tests: 30/30 passing (`SwebKit.Core.Tests`).
Build: `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` all clean.
Manual validation not yet performed.
