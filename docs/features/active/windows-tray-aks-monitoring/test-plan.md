# Test Plan - windows-tray-aks-monitoring

---

title: "Test Plan - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-03-31"
updated: "2026-03-31"

---

## Goal

Validate that minimizing or closing the window sends the app to Windows system tray without stopping configured AKS namespace monitoring, and that alerts remain visible via toast plus tray indicator.

## Scope

- In scope: Windows window lifecycle interception, tray icon lifecycle, monitoring continuity while hidden, restore/exit semantics, alert indicator behavior.
- Out of scope: package-size optimizations, cross-platform tray support, non-AKS monitoring features.

## Main scenarios (priority)

1. Scenario: User clicks Minimize - Expected result: main window hides to tray, process remains alive, monitor loop continues.
2. Scenario: User clicks Close (X) - Expected result: window hides to tray instead of terminating process.
3. Scenario: User selects Restore from tray menu - Expected result: main window is shown and focused, no duplicate windows are created.
4. Scenario: User selects Exit from tray menu - Expected result: app exits fully and existing process-exit cleanup still runs.
5. Scenario: Monitoring active with configured namespaces, app hidden - Expected result: monitor continues polling and emitting pod health events.
6. Scenario: Pod alert occurs while hidden - Expected result: toast appears and tray indicator increments/updates.
7. Scenario: User restores app after hidden alerts - Expected result: unread tray indicator resets according to designed reset rule.
8. Scenario: Hide and restore repeated 20+ times - Expected result: no duplicate tray icons, no leaked handles, stable memory growth profile.
9. Scenario: Monitoring disabled before hide - Expected result: no background polling starts implicitly because of tray transition.
10. Scenario: App restart after monitoring enabled persisted in config - Expected result: monitoring resumes from persisted config as before; tray feature does not regress startup behavior.

## Automated coverage

- Unit tests: tests/SwebKit.Core.Tests
- Validate monitoring continuity semantics and any new coordinator logic that is framework-agnostic.
- Component/unit tests: tests/SwebKit.App.Tests
- Validate event-to-indicator state transitions and restore/reset behavior through testable services.
- Integration tests: targeted app-layer tests for tray/window service behavior where abstraction allows simulation.
- CI gates: build passes and all newly added tests pass.

## Test data and setup

- Reuse existing pod health event fixtures from PodHealthDiff-style test data patterns.
- Add synthetic pod event stream for hidden-window intervals.
- Use deterministic timestamps for unread indicator reset assertions.
- Mock/stub platform tray API boundary for non-Windows CI test execution.

## Manual checks

- Check: Minimize and Close both route to tray - steps
- Launch app, start AKS monitor, click Minimize, then Close (X) after restore; verify app remains running in tray in both paths.
- Check: Monitoring continuity while hidden - steps
- Configure monitored namespaces, hide app to tray, wait for poll cycle, verify new events appear after restore.
- Check: Alert surfacing while hidden - steps
- Trigger a pod health event, verify toast display and tray indicator change while main window is hidden.
- Check: Explicit exit semantics - steps
- Exit via tray menu and verify process termination plus no orphaned tray icon.

## Regression risks & mitigations

- Risk: Existing App process-exit cleanup (port-forward session stop) is bypassed.
- Mitigation: Ensure explicit exit path invokes existing termination flow and add regression test/manual check.
- Risk: Pod monitor starts/stops unexpectedly on window hide/restore.
- Mitigation: Keep monitor lifecycle solely controlled by current monitor service state and namespace config.
- Risk: UI-thread exceptions from tray updates on monitor callbacks.
- Mitigation: Dispatcher-safe wrapper and tests around callback threading assumptions.

## Acceptance criteria

- Minimize and Close both hide to tray and keep process alive.
- AKS monitoring for configured namespaces continues while app is hidden.
- Pod alerts continue to surface via toast + tray indicator while hidden.
- Explicit Exit from tray fully terminates app and cleanup still executes.
- Tests and docs are updated with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
