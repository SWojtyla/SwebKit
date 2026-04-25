# Test Plan - winui3-redis-parity

---

title: "Test Plan - winui3-redis-parity"
owner: ""
status: "Done"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native Redis workspace reaches the agreed analysis and bulk-operation parity without regressing the baseline browse and edit flows.

## Scope

- In scope: health and prefix analysis, deeper insight workflows, bulk operations, shared-card adoption
- Out of scope: unrelated Redis backend changes and new operator features beyond MAUI parity

## Main scenarios (priority)

1. Scenario: operators can move from key browsing to deeper Redis analysis natively. Expected result: the planned analysis workflows are available without reverting to MAUI.
2. Scenario: bulk actions remain safe and understandable. Expected result: higher-risk operations include the right confirmations and progress states.
3. Scenario: the existing browse and typed-detail flows remain intact. Expected result: the new analytics surface does not regress baseline key operations.

## Automated coverage

- Build validation: `build-winui` is green after the WinUI Redis parity implementation.
- Unit tests: `dotnet test tests/SwebKit.WinUI.Tests/SwebKit.WinUI.Tests.csproj --filter RedisPageViewModelTests` passes and covers demo-mode fallback without Redis config, health analysis, prefix memory analysis, slow-log or hot-key loading, Pub/Sub loading, and bulk-delete gating.
- Regression target: rerun touched domain tests if Redis service behavior changes.

## Test data and setup

- Demo mode supports first-pass layout and state validation.
- Live validation needs a representative Redis workspace with keys that exercise health and prefix-analysis scenarios.

## Manual checks

- Check: analysis parity in demo mode. Steps: open Redis, run health analysis, prefix analysis, slow-log refresh, and Pub/Sub refresh, then verify the right pane stays readable while findings and summaries populate.
- Check: loaded-scope selection behavior. Steps: enter selection mode, toggle a namespace row and individual key rows, then confirm the summary and row-level state reflect only currently loaded descendants.
- Check: production bulk safety. Steps: switch to a production-marked profile, select loaded keys, verify delete stays disabled until `CONFIRM` is entered, then verify success messaging and selection reset after delete.
- Check: browse-detail regression. Steps: inspect a string key, a hash key, and TTL actions after using the analytics cards to confirm the baseline detail workflows still respond correctly.

## Regression risks & mitigations

- Risk: analytics surfaces overwhelm the baseline layout. Mitigation: use shared cards and keep the browse/detail interaction intact.
- Risk: bulk actions mutate state unexpectedly. Mitigation: validate confirmation and result handling with representative data.

## Acceptance criteria

- The Redis analysis and bulk workflows called out in this plan are available natively.
- Baseline browse and typed-detail flows remain stable.
- `build-winui` stays green, focused Redis WinUI tests cover the new state logic, and any remaining demo/live checks are either completed later or explicitly deferred during cutover coordination.

## Validation status

- Automated: Complete
- Manual: Deferred for now by operator acceptance

## Sign-off

- **Approved by:**
- **Date:** 2026-04-25
- **Conditions (if any):** Accepted for now; demo-mode and representative live-profile walkthroughs can be completed later if additional cutover evidence is needed.
