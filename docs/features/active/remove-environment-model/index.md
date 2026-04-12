# Feature Overview - remove-environment-model

---

title: "Feature Overview - remove-environment-model"
owner: ""
status: "In Progress"
jira: ""
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Remove the abandoned local environment/profile model so SwebKit operates on a single persisted app configuration without carrying dead environment-selection state in the shell, persistence layer, or incident-timeline scope model.

## Value

The current codebase still carries `Environments`, `ActiveEnvironmentName`, shell environment labels, and `IncidentWorkloadScope.EnvironmentName` even though the app no longer exposes environment creation or switching in the UI. That leaves dead state in persistence, unnecessary migration logic in `ProfileRepository`, and extra scope semantics that make timeline and settings code harder to reason about. Removing the model reduces configuration complexity and removes a category of stale state that no longer benefits operators.

## Scope

- In scope:
	- remove local multi-environment persistence from `ProfileRepository`, `ProfileData`, and `AppStateService`
	- simplify shell context so `TopBar` and `MainLayout` no longer carry environment labels
	- remove `EnvironmentName` from `IncidentWorkloadScope` and update request-key generation and consumers
	- preserve Azure DevOps stage/release environment metadata that comes from Azure DevOps itself
	- migrate legacy `profiles.json` files to the simplified single-config shape on load/save
	- update unit, component, and E2E coverage that still references the local environment model
	- update functionality docs affected by the new single-config model
- Out of scope:
	- removing demo mode or demo providers
	- changing Azure DevOps pipeline/release environment concepts returned by remote APIs
	- redesigning settings UX beyond removing obsolete environment-specific copy

Wave 0 - Shell cleanup already started:
- remove visible environment labels from shared shell/header surfaces
- verify the shell still builds cleanly before deeper model removal

Wave 1 - Core model removal:
- flatten profile persistence to a single `AppConfig`
- remove `ActiveEnvironmentName` / `Environments` APIs and migration-only baggage
- remove `EnvironmentName` from incident scope and cache/request keys

Wave 2 - Validation and docs alignment:
- update tests, fixtures, and documentation across affected projects
- validate incident timeline, settings persistence, and shell behavior end to end

## Dependencies

- Architecture constraints:
	- `docs/architecture/architecture.md`
	- `docs/architecture/design.md`
	- `docs/architecture/codebase-guide.md`
- Functional docs to update:
	- `docs/architecture/functionalities/settings-and-configuration.md`
	- `docs/architecture/functionalities/incident-timeline.md`
- Pitfalls that apply:
	- `docs/pitfalls/agent-workflow.md`
	- `docs/pitfalls/blazor-maui.md`
	- `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: legacy `profiles.json` files fail to load or silently lose data during flattening.
	- Mitigation: keep load-time compatibility with legacy `Environments` / `ActiveEnvironmentName`, normalize in memory, and cover round-trip migration with unit tests.
- Risk: incident timeline request keys or workload mapping lookups regress after removing `EnvironmentName` from `IncidentWorkloadScope`.
	- Mitigation: update `IncidentTimelineModels`, `IncidentTimelineConfig`, and all signal-source tests together; validate deterministic request keys explicitly.
- Risk: remote Azure DevOps environment terminology is confused with the abandoned local environment model.
	- Mitigation: keep remote `EnvironmentName` fields in DevOps DTOs and filters, and record the distinction in `decisions.md`.
- Risk: docs drift because some preview UI cleanup has already landed before the full backend refactor.
	- Mitigation: keep `status.md` current and treat Wave 0 as completed preview work.

## Related documents

- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules:
	- `backend.md`
	- `frontend.md`
	- `decisions.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`, `decisions.md`
