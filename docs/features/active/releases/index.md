# Releases

---

title: "Releases"
owner: ""
status: "Draft"
created: ""
updated: ""

---

## Goal

Provide a project-scoped deployment dashboard that lets developers select, track, and trigger
Azure DevOps pipelines (CI and CD) for all services in a project — sequentially, per environment,
with approval gate support — from a single view.

## Value

Enable teams to coordinate scoped deployments from within the app, reduce manual ADO visits,
and streamline multi-pipeline promotion across environments.

## Scope

- Per-project Azure DevOps connection (org URL + PAT stored in Windows Credential Manager)
- Pipeline selector: browse ADO pipelines and link them to the current SwebKit project
- Support for both CI and CD pipeline types
- Linked pipeline list with live run status (in progress, succeeded, failed, queued, waiting for approval)
- Per-pipeline "Deploy" action — triggers a run targeting the stage mapped to the current environment
- "Deploy All" — triggers all linked pipelines sequentially for the current environment
- Approval gate support — surface pending approvals and allow approving from within the app
- "Open in browser" link per run (no in-app log viewer)
- Safety confirmation when deploying to a Production environment
- Left-nav entry: Releases (🚀, Alt+6)

## Logical outcome

A focused release dashboard where a developer configures which ADO pipelines belong to a
project, maps each pipeline's stages to SwebKit environments, and deploys all services
sequentially to the current environment in one click — including approving any manual gates —
without leaving the app.

## Dependencies

- Depends on `docs/features/active/foundation-mvp/`
- Uses `ICredentialStore` (Windows Credential Manager) for PAT storage
- Uses `TaskQueueService` for background status polling
- Uses `ConfirmDialog` shared component for production safety

## Source traceability

- Canonical feature scope: `docs/features/active/releases/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/active/releases/technical-plan-backend.md`
- `docs/features/active/releases/technical-plan-ui.md`
- `docs/features/active/releases/test-plan.md`
