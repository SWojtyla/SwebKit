# Release Train Cockpit — Test Plan

This plan maps to `technical-plan.md` and `ux-plan.md`. Tests for later phases may be skipped until those phases are implemented, but each completed phase should have its tests passing before the PR is opened.

## Unit / Integration

- `SwebKit.Core` model tests
  - Legacy `ReleaseStoreData` and `ReleaseRecord` deserialize without fields introduced by trains.
  - `DevOpsConfig` validation fails for missing organization, missing PAT when mode is `Pat`, and unknown `AuthenticationMode`.
  - Group/component binding round-trips through JSON and bundle import/export.
  - `ReleaseTrainRecord` invariants: at least one component, immutable version after creation, exact state progression.
  - `ReleaseRepository` add/update/find/delete/atomic-save for trains survives concurrent writes.

- `SwebKit.DevOps` client tests
  - Connection test success/failure with both PAT and demo auth.
  - PR listing, creation, duplicate detection, and source/target commit extraction.
  - Branch/tag ref retrieval and tag collision by name.
  - Pipeline run mapping includes source branch and `sourceVersion`/`sourceCommit` where ADO provides it.
  - Build detail lookup by repository ID and source version.
  - Timeline/approval mapping and checkpoint/waiting-gate detection.
  - Cancellation mid-request aborts cleanly without leaking secrets.
  - Malformed ADO responses produce friendly, redacted errors.

- `SwebKit.Sidecar` endpoint tests
  - Validation errors return `ValidationProblem` with `errors` keyed by JSON property names.
  - Missing PAT or connection failure returns `503`/problem detail without PAT value.
  - Group CRUD and train create endpoints are idempotent on retry with identical payload.
  - Preflight returns per-component source commit, expected tag name, and existing PR/tag conflicts.
  - Confirmed start creates pre-merge tags and PRs; retries safely after partial failure without duplicating already-created artifacts.
  - Refresh/correlation attaches the matching pipeline run by `sourceVersion` and repository ID, not merely by branch name.
  - Train state resumes from `ReleaseRepository` after service restart.

## End-to-end (Playwright)

- Demo mode journey
  - Toggle demo mode; open Release Trains page; open wizard with default group; preflight; confirm; verify active cockpit shows Tag → PR → TST stage progression.
  - Trigger simulated stage success and approval; verify deep links, component matrix, Confluence draft preview/copy.
  - Simulate PR merge and run correlation; verify drift warning if source commit moved.
  - Refresh page and confirm active train resumes.

- Settings UI
  - Open DevOps settings; save organization and PAT status; create release group with two components; set stage aliases; save and reload.
  - Verify every state-changing control has `data-testid` and is reachable by keyboard/click.

## Manual / smoke

- PAT smoke test against a real ADO organization with two repos:
  - Create a throwaway release group.
  - Preflight; confirm branch refs resolve.
  - Start train; confirm pre-merge tags and PRs are created and linked correctly.
  - Merge PRs in ADO; confirm SwebKit discovers the pipeline run and correlates the correct build.
  - Verify TST/STG/PRD visibility and Confluence draft contents.
- Merge-strategy validation against at least one fast-forward and one merge-commit repository; record findings in feature docs.
- Full demo-mode walkthrough from fresh state.

## Security / compliance

- Aikido full scan for every modified first-party file.
- Verify PAT is never written to logs, returned in HTTP bodies, or present in exported bundles.
- Verify branch/project/PR names are escaped in URLs and query parameters.
- Verify cancellation and auth-failure paths do not expose raw HTTP responses.
