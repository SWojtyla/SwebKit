# Status - frontend-composition-hardening

---

title: "Status - frontend-composition-hardening"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-11"
last_updated: "2026-04-11"

---

## Quick summary

Implementation is complete for the scoped frontend hardening pass. `MainLayout`, `ObservabilityPage`, `ServiceBusPage`, and the AKS bootstrap path now run through explicit seams, the Observability drill-through path is render-ready and deterministic, and the affected pages keep local loading and cached-state behavior instead of introducing new full-page waits.

Jira: not linked

Current focus: feature is ready for review. Remaining work is outside implementation scope: code review, merge, and archive/close-out steps.

## Progress checklist

### Planning

- [x] Confirmed the reviewed problem areas and the strengths that must be preserved
- [x] Kept scope distinct from `incident-timeline-workbench`
- [x] Defined wave plan, risks, and validation targets

### Implementation focus

- [x] Wave 1 - add shared provider and client creation seams plus shell error presentation
- [x] Wave 2 - harden Observability provider activation and failure-to-logs coordination
- [x] Wave 3 - move Service Bus and AKS bootstrap logic behind injected seams
- [x] Wave 4 - add shell and composition tests and align functionality docs
- [x] Validate that each wave preserves interactive shell and page behavior during slow or cancelled async work

## Completed

- Verified direct concrete construction in `ObservabilityPage`, `ServiceBusPage`, and `AksPage`.
- Verified console-only shell failure handling in `MainLayout` background initialization and keyboard shortcut registration.
- Verified timing-based failure-to-logs coordination in `ObservabilityPage`.
- Recorded that `SwebKitComponentBase`, `PageDataCache`, existing component coverage, and cancellation awareness are strengths to preserve.
- Added a shared `IShellErrorPresenter` path so `MainLayout` surfaces actionable startup and keyboard shortcut failures through notifications and structured logging while still rethrowing cancellation.
- Added additive `IObservabilityProviderFactory` and `ObservabilityProviderFactory` seams so `ObservabilityPage` no longer constructs `DemoObservabilityProvider` or `AzureAppInsightsProvider` directly.
- Wired the new shell presenter and Observability provider factory in `MauiProgram`.
- Added focused tests for the shell presenter and Observability provider factory seams.
- Replaced the Observability drill-to-logs delay with an explicit pending-query and acknowledgment contract between `ObservabilityPage` and `ObservabilityLogs`.
- Added `IServiceBusNamespaceBootstrapper` and `ServiceBusNamespaceBootstrapper` so `ServiceBusPage` no longer owns namespace connection/bootstrap creation logic, while preserving cached snapshot restore, demo namespaces, and per-namespace progress updates.
- Added `IAksClientBootstrapper` and `AksClientBootstrapper` so `AksPage` no longer owns live/demo client creation or context and namespace bootstrap, and so repeated parent renders no longer retrigger identical reconnect work.
- Added focused regressions for the new Observability, Service Bus, and AKS seams plus delayed bootstrap behavior.
- Updated the Observability, Service Bus, and AKS functionality docs and the shared design flow to reflect the final seam names and runtime flow.

## Remaining

- No implementation work remains within this feature scope.

## Blockers

- None.

## Validation

- Test plan: `test-plan.md`
- Validation status: `dotnet test .\tests\SwebKit.App.Tests\SwebKit.App.Tests.csproj --no-restore --filter "FullyQualifiedName~ObservabilityPageTests|FullyQualifiedName~ServiceBusNamespaceBootstrapperTests|FullyQualifiedName~ServiceBusPageBootstrapTests|FullyQualifiedName~ServiceBusPageTests|FullyQualifiedName~AksClientBootstrapperTests|FullyQualifiedName~AksPageBootstrapTests|FullyQualifiedName~AksPageBatchTests|FullyQualifiedName~ObservabilityProviderFactoryTests|FullyQualifiedName~ShellErrorPresenterTests"` passed with 23 passed, 0 failed. `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore` passed. Existing unrelated warnings remain in `ObservabilityLogs.razor`, `AksPage.razor`, `TopBar.razor`, `EntityTree.razor`, and one test helper.

## Notes

- Keep existing routes and area identifiers stable.
- Do not overlap `incident-timeline-workbench` or add new user workflows in this feature.
- Preserve `PageDataCache` snapshot behavior and current cancellation-first request patterns.
- Do not accept a technically cleaner composition if it regresses perceived responsiveness or introduces new page-wide blocking waits.
