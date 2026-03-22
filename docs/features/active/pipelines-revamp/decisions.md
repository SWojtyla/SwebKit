# Decisions — pipelines-revamp

---

title: "Decisions — pipelines-revamp"
owner: ""
status: "Planned"

---

## D-001 — Pipeline-first, release-optional

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
The current feature requires creating a `ReleaseRecord` before any pipeline operation is possible.
This makes the feature unusable for day-to-day pipeline work and limits the audience to teams that
run formal, named releases.

**Decision:**
Pipelines become the primary object. The Releases concept is retained but demoted to an optional
grouping layer — a named container for a set of pipeline runs. The four-tab layout (Pipelines |
Activity | Releases | Approvals) makes this hierarchy explicit.

**Alternatives considered:**
- Keep release-first, add a "quick trigger" shortcut — rejected because it patches the symptom
  without fixing the mental model.
- Two separate nav items (Pipelines + Releases) — rejected to keep the feature cohesive and avoid
  nav bloat; the tabs achieve the same separation.

**Consequences:**
- `ReleasesPage.razor` is replaced; existing `ReleaseRecord` data and `ReleaseRepository` are
  unchanged and forward-compatible.
- The new default landing is the Pipelines tab, not a release selector.

---

## D-002 — Four-tab layout over dual-nav-items

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
An alternative was to add a second `LeftNav` entry for Pipelines and keep Releases as a separate
route. This would give each concern its own URL.

**Decision:**
Use a single route `/pipelines` with four tabs. Reasons:
1. Approvals are cross-cutting — they appear in both daily pipeline work and release flows.
2. A tab badge on Approvals is more prominent than a separate nav item.
3. Fewer nav items keep the sidebar clean; the existing nav already has 6+ entries.
4. The four concerns (browse, monitor, group, approve) are all about the same ADO connection — one
   page, one connection guard.

**Consequences:**
- `/releases` is redirected or replaced; existing routing still works via redirect.
- The Approvals tab is not independently deep-linkable, but this is acceptable for a desktop tool
  with no URL-sharing need.

---

## D-003 — Derive environment status from run stage history, not the Environments API

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
ADO provides an Environments API (`_apis/distributedtask/environments`) that tracks deployments per
environment resource. However, this API requires environment resources to be configured in ADO
(many teams do not bother), and its permission model is separate from pipeline permissions.

**Decision:**
Derive `PipelineEnvironmentStatus` by scanning stage results from the last N pipeline runs.
The latest run that completed (or is running) at each stage gives the current deployment state.

**Trade-offs:**
- Pro: Works with any pipeline type; no additional permissions needed; consistent with data already
  fetched by the existing `GetPipelineRunsAsync` method.
- Con: "Version" info (e.g., `v1.3.0`) is not natively available from stage data — it must be
  inferred from the run's source branch, tag, or a naming convention in the run name. This is
  documented as a best-effort field; if not inferrable, the cell shows the run number instead.

**Mitigation for version inference:**
If the run has a `sourceBranch` matching `refs/tags/*`, use the tag name as the version. Otherwise
show the branch name + run ID. Document this behaviour in the UI as a tooltip.

---

## D-004 — Activity feed loaded on-demand, not globally cached

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
An option was to load and cache recent run data globally in a service (similar to `AppStateService`)
so the Activity tab would open instantly.

**Decision:**
Load activity data when the Activity tab is activated. No global cache. Reasons:
1. A DevOps org with many pipelines could make the initial load slow and block the app start.
2. A global cache would add staleness complexity (invalidation, background refresh lifecycle).
3. For a desktop tool used in short sessions, fresh-on-activate is more appropriate than
   backgrounded pre-fetch.

**Consequences:**
- Activity tab shows a loading skeleton on first activation.
- An auto-refresh toggle is provided for users who want live updates.

---

## D-005 — Tag Manager promoted to a shared modal, not a dedicated tab

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
In the current design, Tag Manager is the fourth tab at the release level. In the new design there
is no "fourth slot" available at the page level (Approvals takes that position).

**Decision:**
Tag Manager becomes a modal (`TagManagerModal.razor`) launched from two places:
1. Pipeline detail panel — "Create tag for this pipeline →" link.
2. Release detail action bar — "Tag Manager" button (scoped to in-scope components).

**Consequences:**
- Tag Manager is more discoverable from the Pipeline detail view (context-aware launch).
- The modal wraps the existing `TagManager.razor` logic with minimal changes.
- No tag-management capability is lost; it is only re-housed.

---

## D-006 — Keep `ReleaseRecord` as a local construct, do not sync with ADO

**Date:** 2026-03-22
**Status:** Accepted

**Context:**
ADO has its own "Release" concept (classic Releases) and "pipeline run" tagging. An option was to
sync `ReleaseRecord` metadata back to ADO (e.g., as a tag on runs or via the work item API).

**Decision:**
Release records remain local to SwebKit (stored in `releases.json` in AppData). No ADO sync.
Reasons:
1. Sync would require write permissions beyond the PAT scopes currently needed.
2. ADO classic Releases are being deprecated; YAML multi-stage pipelines are the direction.
3. Local records are sufficient for the use case (personal / small team daily tracking).
4. Revisit in a future feature if multi-user sync is requested.

**Consequences:**
- Release records exist only on the machine where SwebKit is installed.
- Deleting SwebKit data deletes release history (acceptable; it is a developer tool, not a CMDB).
