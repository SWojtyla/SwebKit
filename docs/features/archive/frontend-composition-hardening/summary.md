# Archive Summary - frontend-composition-hardening

---

title: "Archive Summary - frontend-composition-hardening"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-11"
pr: "n/a"
commit: "n/a"

---

## Goal

Reduce frontend change cost and hidden failure modes by introducing thin composition seams for the heaviest operational pages and the shell, while preserving a snappy, reactive UI during startup, refresh, drill-through, and reconnect flows.

## Delivered

- Added a shared shell error presentation path so `MainLayout` surfaces actionable background startup and keyboard shortcut registration failures through notifications and structured logging.
- Introduced `IObservabilityProviderFactory` so `ObservabilityPage` no longer constructs live or demo providers directly.
- Replaced the Observability failure-to-logs timing delay with an explicit pending-query and acknowledgment handoff between `ObservabilityPage` and `ObservabilityLogs`.
- Moved Service Bus namespace bootstrap behind `IServiceBusNamespaceBootstrapper`, preserving cached snapshot restore, demo mode behavior, and per-namespace progress updates.
- Moved AKS live and demo client bootstrap behind `IAksClientBootstrapper`, preventing repeated reconnect work on parent re-renders.
- Wired the new seams in `MauiProgram`, added focused composition tests, and aligned the touched design and functionality docs.
- Preserved local loading states, `PageDataCache` behavior, and cancellation-first request handling so the visible surface stays responsive instead of blocking during scoped background work.

## Key decisions

- Keep page orchestration in `SwebKit.App` and move only small creation contracts into `SwebKit.Core`.
- Replace timing-based page coordination with explicit readiness handoff instead of `Task.Delay` sequencing.
- Treat perceived responsiveness as an explicit acceptance criterion for the refactor, not an implicit nice-to-have.
- Keep the feature narrowly scoped to `MainLayout`, Observability, Service Bus, and AKS bootstrap rather than widening into a whole-app frontend rewrite.

## Validation performed

- Focused app tests passed: `dotnet test .\tests\SwebKit.App.Tests\SwebKit.App.Tests.csproj --no-restore --filter "FullyQualifiedName~ObservabilityPageTests|FullyQualifiedName~ServiceBusNamespaceBootstrapperTests|FullyQualifiedName~ServiceBusPageBootstrapTests|FullyQualifiedName~ServiceBusPageTests|FullyQualifiedName~AksClientBootstrapperTests|FullyQualifiedName~AksPageBootstrapTests|FullyQualifiedName~AksPageBatchTests|FullyQualifiedName~ObservabilityProviderFactoryTests|FullyQualifiedName~ShellErrorPresenterTests"` completed with 23 passed, 0 failed.
- Windows app build passed: `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore`.
- Manual desktop UX checks were not re-run as part of archive close-out.

## Lessons learned

- Internal composition cleanup needs an explicit responsiveness bar or it can regress perceived performance while still looking structurally cleaner.
- Deterministic page-to-child coordination is easier to test and maintain than render-timing sleeps.
- Small bootstrap and factory seams are enough to remove Razor-page-owned client construction without forcing a broader UI rewrite.

## Follow-up

- Apply the same seam pattern to other pages only when they become active maintenance hotspots. Owner: unassigned.
- If these same shell or page flows change again, run a dedicated manual responsiveness pass on desktop behavior. Owner: unassigned.

## Archive note

> This file is present because the feature had no Jira ticket (Path B). Archive location: `docs/features/archive/frontend-composition-hardening/`.