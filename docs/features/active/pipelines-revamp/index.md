# Feature Overview — pipelines-revamp

---

title: "Feature Overview — pipelines-revamp"
owner: ""
status: "Planned"
created: "2026-03-22"
updated: "2026-03-22"

---

## Goal

Redesign the Releases feature from a release-centric model into a **pipeline-first DevOps hub**. The
current design forces users to create a `ReleaseRecord` before they can do anything; this blocks daily
pipeline work and makes the feature feel like a weekly-release-only tool. The new design makes
pipelines the primary object and turns releases into an optional, lightweight grouping on top.

## Value

- Developers can trigger, monitor, and approve pipelines on any day without first creating a release.
- A unified Activity feed provides at-a-glance visibility across all ADO projects.
- Global Approvals tab surfaces pending gates from everywhere, not just the selected release.
- Release groupings remain available for teams that run formal sprint/weekly releases — same power,
  less mandatory ceremony.
- Pipeline environment status ("where is this deployed?") is visible per pipeline, not only inside a
  release matrix.

## Scope

**In scope:**

- Rename `/releases` route to `/pipelines`; update `LeftNav` entry, icon, and area color.
- New four-tab top-level layout: **Pipelines | Activity | Releases | Approvals**.
- **Pipelines tab:** two-panel layout — project/pipeline tree on the left, pipeline detail panel on
  the right. Detail panel shows environment deployment status, recent runs, trigger controls.
- **Activity tab:** chronological feed of all pipeline runs across all ADO projects; filterable by
  project, pipeline, status, and date.
- **Releases tab:** current Release Board functionality, moved here. Left panel lists release records;
  main panel shows the component × environment matrix. Retains Create/Edit/Delete/Manage Scope.
- **Approvals tab:** global approval list extracted from `ApprovalCenter.razor`; badge count in tab
  label. Same approve/reject UX including PROD confirmation gate.
- New `PipelineEnvironmentStatus` model and corresponding `IDevOpsClient` method to resolve the
  latest run per environment per pipeline.
- Tag Manager promoted from a tab to a panel/modal accessible from both the Pipeline detail view
  and the Release detail view.
- Navigation updated: `LeftNav` area key changes from `releases` to `pipelines`; accent color
  updated accordingly.
- `DashboardPage` quick-link card updated to point to `/pipelines`.
- Architecture doc `docs/architecture/functionalities/releases.md` updated and renamed to reflect
  the new model.

**Out of scope:**

- Multi-ADO-organization support (still single org per profile).
- Build pipeline management (CI only) — this feature focuses on pipelines that deploy to environments.
- Pull request / work item integration.
- Real-time push notifications (polling / manual refresh only, consistent with the rest of the app).
- Removing or replacing `IDevOpsClient` — only additive changes.

## Dependencies

- `IDevOpsClient` — requires one new method; all existing methods are consumed as-is.
- `ReleaseRepository` — unchanged; release records and snapshots continue to persist locally.
- Fluent UI Blazor — `FluentTabs`, `FluentSplitter` (or CSS flexbox split), `FluentBadge`,
  `FluentDataGrid` for activity feed rows.
- Existing `ApprovalCenter.razor` and `TagManager.razor` — refactored, not rewritten.

## Risks & Mitigations

- **Risk:** Route rename `/releases` → `/pipelines` breaks any stored navigation state or deep links.
  — **Mitigation:** Add a redirect from `/releases` to `/pipelines` via a thin redirect component, or
  simply accept the break (no external link-sharing in this tool).
- **Risk:** `GetPipelineRunsAsync` called across all pipelines for the Activity tab could be slow if
  an org has many pipelines. — **Mitigation:** Limit to top-N runs per pipeline; load lazily on
  scroll or per-project expansion; add a loading skeleton.
- **Risk:** Pipeline environment status requires new ADO REST calls; environment API surface is
  limited and may not always carry "deployed version" info. — **Mitigation:** Derive environment
  status from stage results on latest runs; document the fallback in `backend.md`.
- **Risk:** Two-panel layout inside a `BlazorWebView` may have sizing quirks on smaller windows.
  — **Mitigation:** Minimum panel widths with CSS `min-width`; left panel collapses to icon-only
  at narrow widths.

## Related Documents

- Architecture (current): [docs/architecture/functionalities/releases.md](../../../architecture/functionalities/releases.md)
- Backend plan: [backend.md](backend.md)
- Frontend plan: [frontend.md](frontend.md)
- Test plan: [test-plan.md](test-plan.md)
- Decisions: [decisions.md](decisions.md)
- Status: [status.md](status.md)

## Quick Links

- Status: [status.md](status.md)
- Frontend: [frontend.md](frontend.md)
- Backend: [backend.md](backend.md)
- Decisions: [decisions.md](decisions.md)
