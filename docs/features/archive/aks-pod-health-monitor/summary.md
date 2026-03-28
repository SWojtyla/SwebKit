# Archive Summary - AKS Pod Health Monitor

---

title: "Archive Summary - AKS Pod Health Monitor"
owner: ""
jira: ""
completed_date: "2026-03-28"
pr: ""
commit: ""

---

## Goal

Provide background monitoring for user-selected AKS namespaces and alert users when pod health degrades, even when the AKS page is not in the foreground.

## Delivered

- Added singleton pod monitoring service with periodic polling and resilient error handling.
- Added pod health diff detection for failed, crash-loop, unknown, container not-ready, and terminated pod transitions.
- Added cooldown-based deduplication to reduce repeated alerts for the same pod/event.
- Added namespace monitoring selector and alert history UI components in AKS workflows.
- Integrated monitoring signals into AKS and dashboard surfaces.
- Added DI wiring for monitor service and related notification flow.
- Added and validated pod diff regression tests.

## Key decisions

- Use `PeriodicTimer` polling instead of Kubernetes watch API to keep implementation aligned with existing desktop patterns.
- Run monitoring as a DI singleton service so monitoring survives page navigation.
- Use native Windows toast notifications plus in-app notifications for visibility.
- Use a 2-minute default polling interval to balance response time and API load.

## Validation performed

- Unit tests: `dotnet test .\\tests\\SwebKit.Core.Tests\\SwebKit.Core.Tests.csproj --filter "FullyQualifiedName~PodHealthDiffTests"` -> pass (8/8).
- Source verification: core implementation references found for `PodHealthMonitorService`, AKS/Dashboard integration, namespace selector, alert history panel, and DI registration.
- Scope-boundary verification: no source-level toast activation navigation handler found for click-through navigation.

## Scope boundary

- PHM-10 (toast click activation to focus app and navigate to AKS page) is deferred.
- Deferment is non-blocking for delivered monitoring, detection, and alert visibility behavior.

## Lessons learned

- Keep active `status.md` aligned with implementation milestones to avoid closeout drift.
- For no-Jira closeout, retain only durable archive artifacts and remove transient execution files.
- Explicitly documenting deferred scope boundaries avoids ambiguity at archive time.

## Follow-up

- Implement PHM-10 toast activation navigation and add end-to-end validation for click-through behavior - owner: unassigned.

## Archive note

> This feature had no Jira ticket, so this summary is the durable historical record.
