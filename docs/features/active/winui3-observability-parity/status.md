# Status - winui3-observability-parity

---

title: "Status - winui3-observability-parity"
owner: ""
state: "Done"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-27"

---

## Quick summary

The native Observability route now carries the richer overview analysis surface that MAUI already exposed: cloud-role and operation pivots, deployment comparison anchored to recorded releases, and SLO status for configured targets. The existing native logs or query baseline stays in place; the remaining work is seam reduction and live validation rather than a missing editor path.
The first route-local seam reduction slice is now in place as well: logs query or editor state lives in a dedicated child workspace view-model, while discovery, tab refresh, and readiness-to-settings behavior stay on the page VM. A small follow-up also restored MAUI-style Ctrl+Enter execution for the native advanced query editor and cleaned stale copy that still described the shipped availability heatmap and overview visuals as deferred. Any further seam split and live Azure validation are deferred to future follow-up rather than treated as blockers for this delivered slice.

**Jira:** not linked

**Current focus:** no immediate feature-owned implementation work; keep the delivered native overview, compact layout, and first seam-reduction baseline stable while future follow-up decides whether deeper seam work or live validation still justify another slice.

## Progress checklist

- [x] Native logs or query baseline confirmed and docs corrected
- [x] Overview analysis parity implemented for deployment comparison, SLO status, and cloud-role or operation pivots
- [x] Readiness-to-settings repair path preserved
- [x] Focused automated validation added for the new overview slice
- [x] Query or editor state separated from discovery and tab orchestration inside the Observability route
- [x] Further discovery or tab orchestration split deferred unless later evidence shows it is still needed
- [x] Live credential and release-anchor validation moved to future follow-up instead of blocking close-out
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
- Moved the top-level discovery, provider, and time-range card into the shared compact scaffold context band so the resource list and active analysis tab reach the viewport earlier without dropping route context.
- Added MAUI-style Ctrl+Enter execution to the native advanced KQL editor and removed stale WinUI notes that still implied the native availability heatmap and overview visuals were deferred.

## Remaining

- No blocking remaining work inside this feature folder.
- Final cutover review can still exercise the shipped native Observability baseline, but it is no longer feature-local archive debt.
- Further discovery or provider-activation seam reduction, live Azure credential and release-anchor validation, and any deeper MAUI-only affordance decisions remain explicit future Observability follow-up.

## Close-out checklist

- [x] Accept the richer overview and first logs-workspace seam split as sufficient closure for this slice.
- [x] Move further seam reduction and live Azure validation into explicit future follow-up.
- [x] Promote the feature to `Done` and remove feature-local remaining work.

## Blockers

- No blocker remains inside this closed slice.
- `build-winui` later passed again after the compact Observability layout follow-up, so the historical external compile blocker no longer reflects the current repo state.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passed again after the advanced-query shortcut follow-up. Remaining live Azure validation is still future follow-up rather than a blocker for this closed slice.

## Notes

- Observability remains cutover-critical because credential-readiness, release-backed comparison, and deeper operator workflows are all still operator-visible.
