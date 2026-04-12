# Backend Plan - remove-environment-model

---

title: "Backend Plan - remove-environment-model"
owner: ""
status: "Implemented"

---

## Goal

Remove the abandoned local environment/profile model from persistence and runtime contracts while keeping the rest of the app on the existing single `AppConfig` behavior operators already use.

## Impacted areas

- Persistence and app state:
   - `src/SwebKit.Core/Configuration/ProfileRepository.cs`
   - `src/SwebKit.Core/Configuration/AppDataPaths.cs`
   - `src/SwebKit.Core/Services/AppStateService.cs`
- Domain and runtime models:
   - `src/SwebKit.Core/Models/IncidentTimelineModels.cs`
   - `src/SwebKit.Core/Domain/IncidentTimelineConfig.cs`
- Backend consumers and aggregators:
   - `src/SwebKit.Core/Services/IncidentTimelineService.cs`
   - `src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs`
   - `src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs`
   - `src/SwebKit.Observability/IncidentTimeline/AppInsightsTimelineSignalSource.cs`
   - `src/SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs`
- Test fixtures that seed legacy profile data across `tests/*`

## Contract changes

- `ProfileData` becomes a single-config persistence model with `Config` as the only authoritative app configuration payload.
- `ProfileRepository` no longer exposes `Environments`, `ActiveEnvironmentName`, `CloneEnvironment`, `SwitchEnvironment`, or `RemoveEnvironment` as active runtime behavior.
- `AppStateService` no longer exposes environment-list or active-environment APIs.
- `IncidentWorkloadScope` drops the `EnvironmentName` parameter and its scope key becomes `{ClusterContext}|{Namespace}|{WorkloadKind}|{WorkloadName}`.
- Azure DevOps DTOs that represent remote pipeline/release environment metadata remain unchanged.

## Migration strategy

Preferred approach: load-time compatibility with simplified save behavior.

1. Accept existing `profiles.json` files that still contain `Environments` and `ActiveEnvironmentName`.
2. Select one config to keep:
    - first preference: config matching `ActiveEnvironmentName`
    - fallback: `Config`
    - final fallback: first entry in `Environments`
3. Normalize the in-memory profile to a single-config shape.
4. Persist only the simplified shape on the next save.

This keeps startup non-fatal and avoids hard-breaking existing local installs.

## Implementation tasks

- [x] Refactor `ProfileRepository.LoadAsync()` and `ReplaceProfileData()` to normalize legacy multi-environment payloads.
- [x] Remove now-dead environment mutation methods from `ProfileRepository` and `AppStateService`.
- [x] Update `IncidentWorkloadScope`, `ToScopeKey()`, and every constructor/caller in frontend and backend layers.
- [x] Adjust incident-timeline matching code in `IncidentTimelineConfig` to rely only on remaining scope fields.
- [x] Verify `DevOpsReleaseTimelineSignalSource` still filters by mapping-defined Azure DevOps environment names rather than any local profile state.
- [x] Update unit tests and fixtures that currently create `ProfileData` with `Environments` and `ActiveEnvironmentName`.

## Validation

- Core tests added or updated:
   - profile migration round-trip coverage in `tests/SwebKit.Core.Tests`
   - incident scope key assertions in `tests/SwebKit.Core.Tests`
   - signal-source fixture updates in `tests/SwebKit.Azure.Tests`, `tests/SwebKit.Kubernetes.Tests`, and `tests/SwebKit.DevOps.Tests`
- Build gate:
   - all projects compile after the API removals

## Notes

- Do not remove or rename Azure DevOps `EnvironmentName` fields that come from remote API payloads; they are still part of live release and pipeline behavior.
- Keep cancellation behavior intact when touching timeline code; `OperationCanceledException` must continue to flow correctly.
