# Status - shell-ux-foundation

---

title: "Status - shell-ux-foundation"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The shell foundation implementation is complete. Route-derived shell context, grouped navigation, shared routed-page headers, status/refresh shell signals, and broad page adoption are landed, and the closeout slice finished the remaining validation gap by stabilizing the shared Playwright page, adding focused shell E2E coverage, and correcting the final alias-route nav-state regression in `LeftNav`.

Jira: not linked

Current focus: review the final validation evidence, avoid reopening stable shell primitives, and move the feature through normal closeout once feedback is complete.

## Progress checklist

### Wave 1 - Shell context and navigation

- [x] Define scope and implementation waves
- [x] Route-derived shell context model
- [x] Navigation grouping and active-state behavior
- [x] Top-bar context for current area and shell safety state

### Wave 2 - Shared state patterns and status signals

- [x] Shared page-header contract
- [x] Shared loading/error/empty-state pattern rollout
- [x] Trustworthy refresh/status signal updates
- [x] Cross-page adoption on core routed pages

### Wave 3 - Polish and safety

- [x] Notification-center polish
- [x] Theme consistency and token cleanup
- [x] Production-safety treatment alignment
- [x] Component and E2E regression coverage

## Completed

- Landed the core shell primitives: route-derived shell context, grouped navigation, top-bar context, status-bar refresh language, notification-center polish, and production-safety shell cues.
- Rolled the shared routed-page header plus loading, error, and empty-state pattern across the core routed pages.
- Applied one narrow review-stage routed-page chrome adjustment by moving `RoutePageHeader` body copy into a visually hidden shell heading, leaving the top bar as the only visible page-context header while keeping route semantics and header actions intact.
- Added component-level shell coverage, including a StoragePage regression that fails if pasted source text leaks into rendered markup.
- Completed this stabilization repair slice by removing the duplicated StoragePage methods that were rendering as markup and by deleting the unsupported AKS metrics-unavailable banner state.
- Stabilized the shared Playwright fixture so demo-mode tests and route-based checks no longer depend on leaked prior state from the previous test.
- Added focused shell closeout E2E coverage for direct alias entry on `/releases`, theme persistence via `swebkit-ui-theme` across reload and CDP reconnect, and `FocusOnNavigate` heading focus on route changes.
- Corrected the `LeftNav` active-area binding so direct alias entry on `/releases` activates the Pipelines nav item and `aria-current` state correctly.
- Addressed one narrow review-stage shell chrome polish item by refining the top-bar nav-toggle styling and improving brand/context spacing without reopening broader shell scope.
- Applied one additional narrow repair by removing the one-off Service Bus route-header Settings button so the page keeps more workspace width and relies on the shared shell/settings entry points instead.

## Remaining

- Review-stage feedback only.
- Archive or close out the feature once review is complete.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Targeted Playwright validation previously passed via `tests/SwebKit.E2E.Tests/AppUiTests.cs` (19/19). Shell component regression coverage previously passed via `tests/SwebKit.App.Tests/ComponentTests.cs` and `tests/SwebKit.App.Tests/ShellFoundationTests.cs` (23/23 combined). This review-stage hidden-route-header adjustment was rerun through the focused shell coverage: `RoutePageHeader_HidesBodyContextByDefault_AndKeepsActionsVisible`, `RoutePageHeader_CanKeepSupportingContentInHiddenCopyWhenRequested`, `Navigation_DirectAliasRoute_UsesPipelinesNavAndHiddenRouteHeader`, and `FocusOnNavigate_FocusesSettingsHeadingAfterRouteChange` all passed.

## Notes

- The shell primitives slice is treated as stable enough that follow-up work should prefer narrow repair slices over reopening `MainLayout`, `LeftNav`, `TopBar`, or `StatusBar` without a concrete regression.
- Any request that adds new shell-level surface area during implementation should still be evaluated against this feature before landing as a one-off page change.
- The final validation slice stayed intentionally narrow: shared E2E fixture updates, feature-doc alignment, and one small `LeftNav` correction for alias-route active-state behavior.
- Review feedback remained similarly narrow after that closeout slice: top-bar nav-toggle styling and brand/context spacing polish only, with the feature staying in `Review` rather than reopening scope.
- A later review-stage repair stayed within that same narrow shell boundary by trimming the Service Bus route-header chrome instead of adding another page-specific shell affordance.
