# Test Plan - shell-ux-foundation

---

title: "Test Plan - shell-ux-foundation"
owner: "GitHub Copilot"
status: "Planned"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that the SwebKit shell becomes consistent, route-aware, accessible, and trustworthy across the major routed pages without regressing existing feature behavior.

## Scope

- In scope: shell navigation state, top-bar context, page headers, empty/loading/error patterns, status-bar signals, notification-center behavior, theme persistence, and production-safety cues.
- Out of scope: command palette search precision, workspace persistence, readiness probes, and domain-specific workflow logic.

## Main scenarios (priority)

1. Scenario: direct navigation to any routed page - Expected result: the correct nav item is active even when the page is opened without a left-nav click.
2. Scenario: shell keyboard navigation - Expected result: skip link, nav toggle, command palette button, and notification center remain keyboard accessible.
3. Scenario: header consistency across top-level pages - Expected result: each page exposes one clear `h1`, one context summary, and predictable action placement.
4. Scenario: empty-state treatment on unconfigured or empty pages - Expected result: the page shows actionable CTA-driven empty states rather than passive placeholders.
5. Scenario: loading and retry patterns - Expected result: shell and page loading/error states follow one recognizable structure and do not silently stall.
6. Scenario: refresh/status trust - Expected result: shell refresh language and connection/status indicators align with the page that just refreshed.
7. Scenario: notification center polish - Expected result: unread state, timestamps, clear-all behavior, and history rendering remain coherent across toast and history views.
8. Scenario: theme persistence - Expected result: chosen theme survives app restart and shell controls remain readable in both modes.
9. Scenario: production environment safety - Expected result: operators can always tell when they are in a production-marked environment and destructive confirmation treatment is consistent.
10. Scenario: focus-on-navigate behavior - Expected result: route changes focus the page header target consistently because top-level pages expose the expected heading structure.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Cover `MainLayout`, `LeftNav`, `NavItem`, `TopBar`, `StatusBar`, `NotificationHistory`, `NotificationToast`, and any shared page-header or empty-state wrapper introduced by this feature.
- Add regression coverage for page-specific shells that adopt the shared pattern: `DashboardPage`, `ServiceBusPage`, `AksPage`, `RedisPage`, `StoragePage`, `PipelinesPage`, `ObservabilityPage`, `IncidentTimelinePage`, and `SettingsPage`.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Cover shell navigation, direct-route entry, notification center open/close behavior, theme persistence, and production-environment shell cues.
- Unit tests: n/a unless shell metadata helpers or state-formatting helpers are extracted into pure .NET services.

## Test data and setup

- One environment marked as non-production and one marked as production.
- Shell routes covering the major operator pages.
- Notification history fixtures with mixed severities and unread/read states.
- Scenarios with configured, partially configured, erroring, and empty page states.

## Manual checks

- Check: route-derived nav state - open routes directly and verify active nav state without prior shell clicks.
- Check: header consistency - inspect all major pages and confirm a stable header hierarchy and action placement.
- Check: shell trust - trigger refreshes and failures on different pages and verify status bar/top-bar language remains believable.
- Check: production safety - switch to a production-marked environment and verify shell cues are persistent but not noisy.
- Check: theme polish - toggle theme, restart the app, and verify contrast/readability across shell chrome.

## Regression risks & mitigations

- Risk: page-local CSS or inline styles override new shell primitives. Mitigation: add page-level regression tests and consolidate repeated styling into shared shell components.
- Risk: route tracking regresses command handling or refresh events. Mitigation: cover direct navigation plus refresh-event propagation in component tests.
- Risk: notification polish changes break current persisted history expectations. Mitigation: keep persisted history backward compatible and test both in-memory and persisted rendering.

## Acceptance criteria

- Major routed pages share one recognizable header and state pattern.
- Shell nav state matches the actual current route.
- Refresh and connection/status cues are understandable and consistent.
- Notification center, theme behavior, and production safety cues feel like one system rather than one-off page logic.
- No critical shell regressions are introduced in component or E2E coverage.

## Validation status

- Automated: Not started.
- Manual: Not started.

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
