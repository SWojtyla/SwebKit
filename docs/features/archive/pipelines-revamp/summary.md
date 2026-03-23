# Archive Summary — pipelines-revamp

---

title: "Archive Summary — pipelines-revamp"
owner: ""
completed_date: "2026-03-23"
pr: ""
commit: ""

---

## Goal

Redesign the Releases feature from a release-centric model into a pipeline-first DevOps hub. The previous design required creating a `ReleaseRecord` before any pipeline operation, blocking daily pipeline work. The new design makes pipelines the primary object and turns releases into an optional, lightweight grouping layer.

## Delivered

- Renamed `/releases` route to `/pipelines` with redirect support; updated LeftNav, DashboardPage, StatusBar, and MainLayout command palette
- Four-tab page layout: **Pipelines | Activity | Releases | Approvals**
- **Pipelines tab:** two-panel layout with `PipelineTree` (project/pipeline tree with lazy expand and last-run status) and `PipelineDetail` (environment status table, recent runs, inline trigger panel) or `PipelinesOverview` (project summary cards)
- **Activity tab:** `PipelineActivity` with filter bar, grouped rows, and auto-refresh toggle
- **Releases tab:** `ReleaseList` + `ReleaseDetail` replacing former `ReleaseBoard` and `ReadinessGate`; component-by-environment matrix with readiness pill and action bar
- **Approvals tab:** `ApprovalCenter` refactored to global scope (all projects, no Release dependency) with badge count wired to tab label
- New `PipelineEnvironmentStatus` model and `IDevOpsClient.GetEnvironmentStatusAsync()` method, implemented in both `DevOpsClient` and `DemoDevOpsClient`
- Tag Manager toggle promoted to Pipeline detail and Release detail action bar
- Deleted superseded components: `ReleasesPage`, `ReleaseBoard`, `ReadinessGate`, `PipelineTriggerHub`
- Updated `docs/architecture/functionalities/releases.md`
- CSS accent color renamed from `--color-nav-releases` to `--color-nav-pipelines`

## Key decisions

- **D-001 Pipeline-first, release-optional** — Pipelines become the primary object; releases demoted to optional grouping. Avoids patching the release-first model and fixes the mental model.
- **D-002 Four-tab layout over dual-nav-items** — Single `/pipelines` route with tabs keeps the sidebar clean and makes Approvals cross-cutting badge more prominent.
- **D-003 Derive env status from run stage history** — Scanning recent pipeline run stages is more reliable across pipeline types and avoids extra ADO Environments API permissions.
- **D-004 Activity feed loaded on-demand** — No global cache; fresh-on-activate avoids blocking app start and staleness complexity.
- **D-005 Tag Manager as shared modal** — Accessible from both Pipeline detail and Release detail; no capability lost.
- **D-006 ReleaseRecord stays local** — No ADO sync; avoids extra write permissions and aligns with personal/small-team use case.

## Validation performed

- Build passes with zero warnings and zero errors across all source projects
- All five implementation phases completed and verified against the plan
- Manual QA performed across all four tabs (Pipelines, Activity, Releases, Approvals) and demo mode

## Lessons learned

- Deriving environment status from stage history is a pragmatic compromise when the ADO Environments API is not consistently configured across teams
- Absorbing small components inline (ReadinessGate, PipelineTriggerHub) during a redesign reduces component count without losing functionality
- Keeping `ReleaseRecord` and `ReleaseRepository` unchanged made the migration safe — no data model risk

## Follow-up

- Version inference from pipeline runs is best-effort (tag name or branch + run ID); could be improved if naming conventions are enforced
- If performance becomes an issue with many pipelines in the Activity tab, add a dedicated `GetRecentRunsAcrossProjectsAsync()` method
- Multi-ADO-organization support remains out of scope for a future feature

## Archive metadata

- Feature folder: `docs/features/archive/pipelines-revamp/`
- Related architecture doc: `docs/architecture/functionalities/releases.md` (updated in this feature)
- Related components: `Components/Pipelines/`, `Components/Releases/`
