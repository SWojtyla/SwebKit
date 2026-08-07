# Release Train Cockpit

## Status

Planned → In Progress. Phase 0 (feature contract) and Phase 1–2 foundation (core models, durable state, ADO API expansion) are being implemented on this branch. Phase 3 (sidecar endpoints/PAT), Phase 4–5 (React settings/wizard), Phase 6 (Confluence draft), and Phase 7 (Entra) will follow in later PRs.

## Scope

Build a resumable, local-first release-train cockpit inside the Tauri + React SwebKit application that creates and tracks grouped Azure DevOps release PRs and pre-merge tags, correlates the resulting multistage pipeline runs, monitors TST/STG/PRD gates, and produces a Confluence-ready handoff draft. The feature deliberately reverses the earlier product decision that excluded DevOps from the Tauri + React rewrite, but only for a focused **Release Trains** capability — not a wholesale port of the legacy MAUI Pipelines hub.

Azure DevOps remains authoritative for branch policies, PR completion, deployment execution, and STG/PRD approvals. SwebKit never bypasses those controls in v1: it monitors state, identifies blockers, provides exact deep links, safely retries only failed idempotent actions, and prepares the table/remarks the release manager needs for Confluence. Release state stays local in `%APPDATA%/SwebKit/releases.json` for v1.

## Why this, why now

The legacy MAUI app had a Pipelines hub that was deliberately dropped when SwebKit moved to Tauri + React. Operators still need a safe, repeatable way to run grouped releases through Azure DevOps without giving up human controls. A focused Release Trains page and settings tab reintroduces only what is needed for that workflow, reusing the existing `SwebKit.Core`/`SwebKit.DevOps` libraries and the local profile/store model.

## Non-goals

- Generic pipeline browser / activity feed port from MAUI.
- PR completion or auto-completion from SwebKit.
- STG/PRD approve/reject from SwebKit in v1.
- Triggering the deployment pipeline when the `main` merge already does so.
- Scheduling release trains automatically.
- Teams/email notifications.
- Confluence API writes or page-template management in v1.
- Shared backend/database or multi-user locking.
- Automatic rollback, tag deletion/movement, PR abandonment, or history rewriting.
- MAUI UI changes.

## Dependencies / prior art

- `src/SwebKit.Core/Domain/DevOpsConfig.cs` and `src/SwebKit.Core/Models/ReleaseModels.cs` — extended with group/train models.
- `src/SwebKit.Core/Configuration/ReleaseRepository.cs` — extended with train persistence.
- `src/SwebKit.Core/Abstractions/IDevOpsClient.cs` and `src/SwebKit.DevOps/DevOpsClient.cs` — extended with PR/ref/build correlation methods.
- `src/SwebKit.Core/Services/DemoDevOpsClient.cs` — extended with deterministic train scenarios.
- Legacy MAUI `SwebKit.App` Pipelines/Releases UI — not changed; the new feature lives in the React frontend.

## Follow-on feature

Entra authentication (Phase 7) is intentionally a separate milestone after PAT is proven. Post-v1 candidates include shared ADO-backed manifests, Teams notifications, Confluence page updates, optional PR auto-complete, and scheduled release calendar integration.

## Outcomes / definition of done

- A user can configure a PAT, organization, reusable release groups, and per-component pipeline/branch/stage mappings in Settings.
- A wizard can preflight a train, capture per-component source commits, create pre-merge tags, and create or attach `development` → `main` PRs without duplicates.
- SwebKit monitors PR merge state and discovers the merge-triggered pipeline run using repository/commit evidence.
- TST/STG/PRD status is shown per component with exact ADO deep links, but no approve/reject controls in v1.
- A Confluence-ready component table and overall remarks can be previewed and copied.
- Full demo-mode path works without credentials; real ADO merge strategy and run correlation are validated before final workflow implementation.
