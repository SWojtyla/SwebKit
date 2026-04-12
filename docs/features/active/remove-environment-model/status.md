# Status - remove-environment-model

---

title: "Status - remove-environment-model"
owner: ""
state: "In Progress"
jira: ""
branch: "sw/dev/timeline"
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The local environment/profile model has been removed from runtime code. The remaining work is closeout: keep feature-adjacent docs aligned, decide whether to run the full Windows E2E suite before merge, and move the feature to review.

**Jira:** not linked

**Current focus:** feature closeout. Focused validation is green; the only unresolved item is whether to retry the full E2E file in a runner where the CDP endpoint comes up reliably.

## Progress checklist

### Wave 0 - Discovery and shell cleanup

- [x] Read architecture, design, codebase-guide, and relevant pitfalls
- [x] Confirm environment/profile creation and switching are no longer exposed in the UI
- [x] Identify source and test usages of `Environments`, `ActiveEnvironmentName`, and `EnvironmentName`
- [x] Remove visible shell/page environment labels as preview cleanup
- [x] Verify the preview cleanup still builds

### Wave 1 - Core model removal

- [x] Replace profile persistence with a single-config model plus legacy-load migration
- [x] Remove `AppStateService.Environments`, `ActiveEnvironmentName`, and environment mutation APIs
- [x] Remove `EnvironmentLabel` from shell context and `MainLayout`
- [x] Remove `EnvironmentName` from `IncidentWorkloadScope` and request-key generation
- [x] Update timeline/config consumers that depend on the old scope shape

### Wave 2 - Validation and docs

- [x] Update unit and component tests that seed legacy profile data
- [x] Update E2E assertions that reference shell environment UI
- [x] Update architecture functionality docs for settings/configuration and incident timeline
- [x] Run focused validation locally
- [ ] Run full E2E suite in a healthy Windows runner or explicitly waive it for this change set
- [ ] Ready for review

## Completed

- Confirmed the local environment/profile model is still present in persistence and domain code even though the UI no longer manages it.
- Removed visible environment labels from `TopBar` and `IncidentTimelinePage` as a low-risk preview cleanup.
- Rebuilt `SwebKit.App` successfully after the preview cleanup.
- Created an active feature folder with the required planning modules.
- Flattened `ProfileRepository` to a single-config runtime model while keeping load-time compatibility with legacy multi-environment `profiles.json` files.
- Removed `AppStateService` environment APIs and dropped `EnvironmentName` from `IncidentWorkloadScope`.
- Updated focused tests and added a migration test that proves legacy profile files are normalized and resaved without `Environments` or `ActiveEnvironmentName`.
- Aligned core architecture and functionality docs plus impacted active feature docs with the new single-config model.

## Remaining

- Decide whether to rerun the full Windows E2E suite in CI or accept the focused validation plus local CDP-fixture failure evidence.

## Blockers

- No hard blocker yet.
- Full E2E validation did not complete locally because the Playwright harness never observed the WebView2 CDP endpoint on `http://localhost:9222`.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Focused validation passed locally
- Verified so far:
	- `dotnet build SwebKit.slnx`
	- focused test run covering `AppStateServiceProfileLoadTests`, `AppInsightsTimelineSignalSourceTests`, `IncidentTimelineServiceTests`, `ShellFoundationTests`, `ServiceBusEvidenceSignalSourceTests`, `AksTimelineSignalSourceTests`, and `DevOpsReleaseTimelineSignalSourceTests` (20 passed)
	- attempted full `tests/SwebKit.E2E.Tests/AppUiTests.cs` run; all tests were blocked by fixture startup (`AppFixture.WaitForCdpAsync`) before any product assertions executed

## Notes

- Keep the distinction clear between the removed local environment/profile model and Azure DevOps release/pipeline environment metadata from remote APIs.
- The follow-up docs pass should keep rescoping active planning docs away from profile-environment comparison rather than reintroducing the removed model implicitly.
