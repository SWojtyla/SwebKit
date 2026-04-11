# Test Plan - frontend-composition-hardening

---

title: "Test Plan - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Validate that the frontend hardening removes hidden composition fragility without changing current operator workflows, and that shell and page coordination becomes deterministic, visible, and testable.

## Scope

- In scope: shell startup and keyboard shortcut failure handling, provider and client creation seams for Observability, Service Bus, and AKS bootstrap, Observability failure-to-logs drill-through behavior, cancellation and last-request-wins behavior on refactored paths, `PageDataCache` preservation for cached navigation and bootstrap scenarios, and DI registration plus demo-mode selection behavior for the new seams.
- Out of scope: visual redesign or UX restyling, new routes or feature surfaces, Incident Timeline or any cross-source workflow, broad cleanup of Dashboard, Storage, Redis, or other unscoped pages, and large E2E expansion unless a shell regression cannot be expressed reliably in component tests.

## Main scenarios (priority)

1. Scenario: `MainLayout` background initialization throws a non-cancellation exception. Expected result: the shell remains usable, the failure is surfaced through a shared UI error path, and technical detail is logged.
2. Scenario: keyboard shortcut registration fails in `OnAfterRenderAsync`. Expected result: the failure is visible to the operator or status surface and does not remain console-only.
3. Scenario: Observability resource activation runs through an injected provider factory. Expected result: demo mode and real mode both resolve the correct implementation without page-owned concrete construction.
4. Scenario: drill from Observability Failures into Logs. Expected result: the Logs tab activates and executes exactly once with the selected KQL through explicit readiness handoff and no timing sleep.
5. Scenario: rapid range, resource, or tab changes during Observability refresh. Expected result: stale requests are cancelled, the last request wins, and no disposed-component update occurs.
6. Scenario: Service Bus namespace bootstrap runs through an injected connector. Expected result: per-namespace success and failure states render as today, cached snapshot restore still works, and reconnect continues in the background.
7. Scenario: AKS context or namespace change runs through an injected client-creation seam. Expected result: existing resource grids and detail panels keep working with no stale data flashes or swallowed cancellation.
8. Scenario: shell regression around commands and navigation. Expected result: `MainLayout` still registers existing navigation commands and current areas without relying on page-specific side effects.
9. Scenario: demo mode toggle after the refactor. Expected result: scoped pages still resolve demo implementations correctly after seam extraction.
10. Scenario: preserved-strength regression audit. Expected result: `SwebKitComponentBase` load and error behavior plus `PageDataCache` snapshot semantics remain unchanged for affected pages.

## Automated coverage

- Component tests: use `tests/SwebKit.App.Tests` to cover `MainLayout` startup and error cases, Observability drill-through and resource activation, `ServiceBusPage` namespace bootstrap, AKS bootstrap, and demo-mode selection.
- Unit tests: add pure coordinator or contract tests in the app or core test project only when logic moves out of the Razor page into testable non-UI types.
- Composition and registration tests: verify `src/SwebKit.App/MauiProgram.cs` resolves the new seams for both demo and real modes.
- End-to-end tests: not required for the initial cut. Add them only if a shell or navigation regression cannot be expressed reliably in component tests.

## Test data and setup

- Fake observability provider factory, fake Service Bus connector, and fake AKS client factory.
- JS interop stubs for keyboard registration, theme loading, local storage, and window width.
- Deterministic cancellation harnesses and render-ready handshake tests with no `Task.Delay` assertions.
- Cached `PageDataCache` snapshots for Service Bus and AKS back-navigation behavior.

## Manual checks

- Check: startup and keyboard shortcut failure surfacing. Steps: simulate both shell failures and verify the shared message path is visible and the shell remains usable.
- Check: Observability failure-to-logs drill-through. Steps: trigger drill-through repeatedly and verify one query execution per click with no intermittent misses.
- Check: cached page bootstrap behavior. Steps: navigate away from and back to Service Bus and AKS pages and verify snapshot restore and background reconnect behavior remain intact.
- Check: demo mode resolution. Steps: toggle demo mode and confirm scoped pages still use the correct demo implementations.

## Regression risks & mitigations

- Risk: factory extraction changes page behavior while looking like an internal refactor.
- Mitigation: preserve current routes and states in component assertions before cleanup.
- Risk: error surfacing becomes noisy or user-hostile.
- Mitigation: route only actionable shell failures to the shared UI surface and keep raw detail in `ILogger` output.
- Risk: DI registration drift breaks demo mode or real mode selection.
- Mitigation: add explicit registration and mode-selection tests.
- Risk: refactoring around cancellation accidentally swallows `OperationCanceledException`.
- Mitigation: add targeted tests for cancellation passthrough and stale-response rejection.

## Acceptance criteria

- No scoped page or shell path in this feature directly constructs `AzureAppInsightsProvider`, `AzureServiceBusClient`, or `KubernetesAksClient`.
- Observability drill-through no longer depends on render timing sleeps.
- Shell startup and JS registration failures are visible through a shared error path and no longer remain console-only.
- Existing page behavior is preserved for current routes, cached snapshots, and cancellation semantics.
- New shell and composition tests pass with the existing app test suite.
- Docs reflect final abstraction names and touched functionality docs when implementation lands.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):