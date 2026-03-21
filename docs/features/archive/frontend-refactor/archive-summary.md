# Frontend Refactor — Archive Summary

**Completed:** 2026-03-21

## What was done

Paused new features to evaluate and improve frontend code quality. No user-visible behaviour changed.

## Key changes

**CSS architecture** — Added token scales for spacing (5 sizes), typography (5 sizes), and z-index (4 layers) to `app.css`. Added utility classes (`.form-input`, `.surface-card`, `.text-*`, `.empty-state*`). Replaced all magic numbers and hard-coded colours across 13 CSS isolation files. Replaced all inline z-index integers with token variables. Restructured the 1,183-line `AksPage.razor.css` with 7 section banners.

**Shared components** — Created `EmptyState`, `Modal`, `Dropdown` as reusable Razor components. Created `SelectionService<T>` and `AutoRefreshController` as reusable C# helpers. Wired them into `MessageListView.razor` and `ServiceBusPage.razor`, eliminating the duplicated inline backdrop, empty-state, selection toggle, and timer patterns.

**AksPage splitting** — `AksPage.razor` was reduced from 2,182 lines to 1,653 by extracting 9 child components: `DeploymentGrid`, `StatefulSetGrid`, `PodGrid`, `ConfigMapGrid`, `SecretGrid`, `IngressGrid`, `HelmGrid`, `CronJobGrid`, `HpaPanel`. Each has its own scoped `.razor.css`.

**Async fixes** — `PodLogView.razor`: streaming task is now tracked, renders are batched (every 20 lines), and the task is properly cancelled and awaited on dispose. `MessageListView.razor`: `EventCallback.InvokeAsync` is awaited; timer is managed by `AutoRefreshController` and disposed correctly. JS interop calls in `MessageListView`, `ServiceBusPage`, and `MainLayout` are wrapped in try/catch.

## What was deferred

- `MessageListView.razor` filter/export extraction into dedicated C# classes (still 400+ lines)
- `ServiceBusPage.razor` namespace/tab wiring extraction
- `AppDataPaths` injectable abstraction
- `DevOpsClient` factory pattern
- Functional tests for `AzureServiceBusClient` send/peek/resubmit (requires SDK wrapper interface)
