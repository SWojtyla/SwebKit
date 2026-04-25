# Status - winui3-observability-parity

---

title: "Status - winui3-observability-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The native Observability route now carries the richer overview analysis surface that MAUI already exposed: cloud-role and operation pivots, deployment comparison anchored to recorded releases, and SLO status for configured targets. The existing native logs or query baseline stays in place; the remaining work is seam reduction and live validation rather than a missing editor path.
The first route-local seam reduction slice is now in place as well: logs query or editor state lives in a dedicated child workspace view-model, while discovery, tab refresh, and readiness-to-settings behavior stay on the page VM.

**Jira:** not linked

**Current focus:** validate the richer overview and logs-workspace slice with live Azure and release-backed data, then decide whether discovery or tab orchestration still needs a second split before any more parity work lands.

## Progress checklist

- [x] Native logs or query baseline confirmed and docs corrected
- [x] Overview analysis parity implemented for deployment comparison, SLO status, and cloud-role or operation pivots
- [x] Readiness-to-settings repair path preserved
- [x] Focused automated validation added for the new overview slice
- [x] Query or editor state separated from discovery and tab orchestration inside the Observability route
- [ ] Discovery and tab orchestration split further only if richer parity still needs it
- [ ] Live credential and release-anchor validation completed
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that Observability already has native routing, discovery, and a stronger readiness story than before.
- Identified Observability as a high-refactor-pressure slice because discovery, tabs, overview analysis, and editor state still accumulate in one page seam.
- Corrected the active-feature plan so it no longer treats the native logs or query editor as missing work.
- Added native deployment comparison driven by recorded release anchors from the release repository.
- Added native SLO status for configured observability targets in the overview workspace.
- Switched the native explainer call to the same cloud-role and operation dimension keys already used by the MAUI route, so the WinUI pivot section now renders real data instead of a permanent deferred state.
- Added focused WinUI coverage for the richer overview slice and updated the existing readiness tests for the new release-repository dependency.
- Extracted logs mode, preset, saved-query, and guided-draft state into `ObservabilityLogsWorkspaceViewModel` so the page VM no longer owns both query editing and route-level discovery or tab orchestration.

## Remaining

- Decide whether discovery or provider-activation state still needs a second extraction before any further parity work lands.
- Run live validation for Azure credential readiness, release-anchor comparison, and configured SLO targets.
- Decide whether any remaining MAUI-only affordances beyond this overview slice are still cutover-critical.

## Blockers

- Local Observability file validation is clean for the extracted logs-workspace slice.
- Full `build-winui` validation is currently blocked by an existing compile error in `src/SwebKit.WinUI/ViewModels/Pipelines/PipelinesReleaseWorkspaceViewModel.cs`, which sits outside the Observability-owned surface.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: focused Problems checks report no errors in the touched Observability and readiness files. `build-winui` is blocked by an unrelated Pipelines compile error outside this slice, and the `runTests` tool returned 0 discovered tests for the focused files in this environment.

## Notes

- Observability remains cutover-critical because credential-readiness, release-backed comparison, and deeper operator workflows are all still operator-visible.
