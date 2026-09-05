# Release Train Cockpit — UX Plan

## Entry points

- New top-level route `/release-trains` from the sidebar navigation and command palette.
- New Settings tab **DevOps / Release Trains** for PAT, organization/auth mode, reusable release groups, stage aliases, and default merge-strategy.

## Persona and flow

A release manager wants to ship a named bundle of components (microservices) from `development` to `main` through TST, STG, PRD with human approvals at STG and PRD. They need confidence that tags/PRs were created correctly, merges happened, pipelines ran, stages progressed, and an accurate handoff can be pasted into Confluence.

## Pages / components

### 1. Settings — DevOps / Release Trains

- Authentication card: organization URL, mode toggle (PAT / Entra), PAT status (set/replace/delete). PAT value is never shown.
- Discovery card: test connection, pin projects, browse pipelines (read-only helper only; pipeline selection happens inside release group).
- Release groups card: CRUD of named groups; within a group, add components with project, repository, default source (`development`) and target (`main`) branches, pipeline/branch per stage, stage environment aliases, merge strategy, and optional pre/post notes.
- Validation and feedback on every save; destructive actions require confirmation.

### 2. Release Trains list

- Columns: train name, status, started, ETA/human note, active stage, blocker count.
- Filters: active, waiting for human, completed, failed, archived.
- Primary action: start new train (opens wizard).

### 3. Release wizard

- Step 1 — Select group and train name/label/remark.
- Step 2 — Preflight: resolve branch heads, compute expected versions, list existing PRs/tags, report conflicts.
- Step 3 — Review: explicit per-component action list (create tag, create/attach PR, expected version) plus overall remarks.
- Step 4 — Confirm: one batch confirm; creates tags and PRs. After this the wizard closes and the cockpit opens.

### 4. Active cockpit

- Top bar: train name, status, overall remark, last refreshed, refresh button.
- Component matrix: one row per component with Tag, PR, TST, STG approval, STG, PRD approval, PRD, deep links.
- Status: pending, running, succeeded, failed, blocked, not-started.
- PR column: source → target, deep link, merge status, drift warning if source SHA moved after tagging.
- TST/STG/PRD columns: pipeline run link, stage state, last transition time.
- Blockers panel: failed stage, missing approval, untracked pipeline, merge-strategy mismatch, deep-link to ADO.
- Actions: manual refresh, attach run (search candidates), retry failed action, copy Confluence draft, edit remarks.
- Demo-mode toolbar: simulate stage completion, approval, PR merge, failure, reset.

### 5. Confluence draft

- Preview modal with rich table and Markdown source.
- Copy rich and Copy Markdown buttons with notifications.
- Includes component versions, deep links, overall remarks, blockers, and a footer noting the snapshot time.

## Interaction and accessibility

- All state-changing controls have `data-testid` attributes.
- Buttons show loading/disabled states and use the global notification toast.
- Forms validate inline and return focus to the first invalid field on submit.
- Polling pauses when the tab/window is hidden and resumes on visibility.
- Empty, loading, error, and permission-denied states are explicit and offer next steps.

## Demo mode

- Same UI, deterministic fake ADO data. The demo must exercise preflight, tag/PR creation, merge, run correlation, stage progression, approval gating, drift, and Confluence export without credentials.
