# Status — AKS New Capabilities

---

title: "Status - AKS New Capabilities"
owner: ""
state: "Done"
branch: ""
started: ""
last_updated: "2026-03-17"

---

## Quick summary

Current state: Done — all 6 features implemented, architecture doc updated, 44/44 unit tests passing.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation
- [x] Frontend implementation
- [x] Tests (unit / integration / manual)
- [x] Docs aligned (architecture/functionalities/aks.md updated)
- [x] Ready for review

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

None.

## Blockers

None.

## Validation

Unit tests: 44/44 passing (full `SwebKit.Core.Tests` suite).
Build: `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` all clean.
Architecture doc: `docs/architecture/functionalities/aks.md` updated.
