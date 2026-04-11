# Test Plan - frontend-composition-hardening

---

title: "Test Plan - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Validated"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Validate that the frontend hardening removes hidden composition fragility without changing current operator workflows, and that shell and page coordination becomes deterministic, visible, testable, and fluid during loading. The shell and scoped pages should stay reactive while async work is in flight.

## Scope

- In scope: shell startup and keyboard shortcut failure handling, provider and client creation seams for Observability, Service Bus, and AKS bootstrap, Observability failure-to-logs drill-through behavior, cancellation and last-request-wins behavior on refactored paths, perceived responsiveness and local loading behavior on the scoped paths, `PageDataCache` preservation for cached navigation and bootstrap scenarios, and DI registration plus demo-mode selection behavior for the new seams.
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
11. Scenario: slow shell startup or background initialization. Expected result: the shell renders a usable frame immediately, remains navigable, and surfaces progress or failure without locking the whole UI.
12. Scenario: slow Observability refresh or failure-to-logs handoff. Expected result: tab changes and surrounding page interactions remain responsive, loading feedback is local, and the page does not freeze while waiting for the logs surface.
13. Scenario: slow Service Bus or AKS bootstrap or reconnect. Expected result: cached or previous state remains visible where already supported, the operator can still navigate away or change selection, and superseded work cancels cleanly.

## Automated coverage

- Component tests: use `tests/SwebKit.App.Tests` to cover `MainLayout` startup and error cases, Observability drill-through and resource activation, `ServiceBusPage` namespace bootstrap, AKS bootstrap, and demo-mode selection.
- Use delayed fakes in component tests to assert that loading indicators appear without blocking shell navigation, tab switching, or cancellation and replacement flows.
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
- Check: responsiveness under delayed loads. Steps: interact with the shell and scoped pages during deliberately delayed loads and confirm the UI remains fluid, the visible surface does not freeze, and background work can be superseded by a new request.

## Regression risks & mitigations

- Risk: factory extraction changes page behavior while looking like an internal refactor.
- Mitigation: preserve current routes and states in component assertions before cleanup.
- Risk: error surfacing becomes noisy or user-hostile.
- Mitigation: route only actionable shell failures to the shared UI surface and keep raw detail in `ILogger` output.
- Risk: DI registration drift breaks demo mode or real mode selection.
- Mitigation: add explicit registration and mode-selection tests.
- Risk: refactoring around cancellation accidentally swallows `OperationCanceledException`.
- Mitigation: add targeted tests for cancellation passthrough and stale-response rejection.
- Risk: the refactor introduces broader blocking loaders or clears visible state too aggressively during background work.
- Mitigation: prefer local busy regions, preserve cached or existing state where already supported, and add delayed-response tests that assert interactivity.

## Acceptance criteria

- No scoped page or shell path in this feature directly constructs `AzureAppInsightsProvider`, `AzureServiceBusClient`, or `KubernetesAksClient`.
- Observability drill-through no longer depends on render timing sleeps.
- Shell startup and JS registration failures are visible through a shared error path and no longer remain console-only.
- Existing page behavior is preserved for current routes, cached snapshots, and cancellation semantics.
- Scoped shell and page flows remain interactive during startup, refresh, drill-through, and reconnect. No new full-surface blocking wait state is introduced on the affected paths.
- New shell and composition tests pass with the existing app test suite.
- Docs reflect final abstraction names and touched functionality docs when implementation lands.

## Validation status

- Automated: Passed. `dotnet test .\tests\SwebKit.App.Tests\SwebKit.App.Tests.csproj --no-restore --filter "FullyQualifiedName~ObservabilityPageTests|FullyQualifiedName~ServiceBusNamespaceBootstrapperTests|FullyQualifiedName~ServiceBusPageBootstrapTests|FullyQualifiedName~ServiceBusPageTests|FullyQualifiedName~AksClientBootstrapperTests|FullyQualifiedName~AksPageBootstrapTests|FullyQualifiedName~AksPageBatchTests|FullyQualifiedName~ObservabilityProviderFactoryTests|FullyQualifiedName~ShellErrorPresenterTests"` completed with 23 passed, 0 failed.
- Manual: Not run in this implementation pass.

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
