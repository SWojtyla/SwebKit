# Frontend Plan - frontend-composition-hardening

---

title: "Frontend Plan - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Implemented"

---

## Goal

Keep the existing top-level UX intact while moving client construction, shell error surfacing, and fragile request sequencing into stable seams that are easier to test and reason about, without sacrificing a fluid, reactive loading experience.

## Impacted areas

- Existing shell and page composition points:
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/MauiProgram.cs`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Services/PageDataCache.cs`
- `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`
- Expected contract touch points:
- additive `SwebKit.Core` abstractions for Observability provider creation, Service Bus connection bootstrap, and AKS client creation
- app-layer coordinator services for shell error presentation and page bootstrap behavior

## UX notes

- No new routes or visual redesign.
- Existing loading, empty, partial, and error states stay intact for the scoped pages.
- A loading state is acceptable; a frozen surface is not.
- Prefer local busy regions over whole-page blocking overlays for the scoped shell and page flows.
- If cached or previously loaded data is already on screen, keep it visible during background refresh or reconnect unless correctness requires clearing it.
- Navigation, tab switching, and last-request-wins replacement actions should stay responsive while async work is pending.
- Shell initialization and keyboard shortcut registration failures should surface through a shared UX path rather than only through console output.
- Failure-to-logs in Observability remains a one-click action but moves to an explicit readiness handshake instead of a timing delay.
- Cached back-navigation and quick-return behavior should remain intact where `PageDataCache` already helps.

## API / contract changes

- Add small creation interfaces in `SwebKit.Core` for the concrete providers and clients currently created inside Razor pages.
- Register the real and demo implementations in `src/SwebKit.App/MauiProgram.cs` so pages request abstractions rather than concrete infrastructure types.
- Introduce an explicit page-to-child handoff for Observability logs drill-through. Acceptable forms are a pending request model, a tab-ready callback, or a small coordinator service. Timing sleeps are not acceptable.
- Any new coordinator or factory seam should support non-blocking sequencing and cancellation-aware replacement; the refactor should not force pages into synchronous wait chains before they can render or respond.
- Keep `PageDataCache` keys, TTL assumptions, and current route identifiers stable unless a verified regression requires a focused change.

## Tasks

### Wave 1 - Shared seams and shell error path

- [x] Add a shell error presentation service and route `MainLayout` background initialization and JS registration failures through it.
- [x] Add provider and client factory or connector interfaces and wire them in the composition root.
- [x] Add bUnit-friendly test doubles for the new seams.
- [x] Keep `MainLayout` first render and shell commands usable while background initialization and error surfacing run.

### Wave 2 - Observability hardening

- [x] Refactor `ObservabilityPage` to activate providers through `IObservabilityProviderFactory`.
- [x] Replace the current delay-based drill-through with an explicit pending-query and acknowledgment handoff to `ObservabilityLogs`.
- [x] Keep auto-refresh, saved queries, guided mode, and advanced mode behavior unchanged.
- [x] Ensure Observability drill-through and refresh remain responsive under delayed provider responses and do not gate the whole page on child readiness.

### Wave 3 - Service Bus and AKS bootstrap hardening

- [x] Move Service Bus namespace connection workflow out of `ServiceBusPage` into `IServiceBusNamespaceBootstrapper`.
- [x] Move AKS client bootstrap and reconnection path out of `AksPage` into `IAksClientBootstrapper` without rewriting the detail panels.
- [x] Preserve existing cancellation behavior, selection behavior, and `PageDataCache` snapshot support.
- [x] Ensure Service Bus and AKS bootstrap or reconnect can update progressively without blanking the entire workspace or freezing nearby interactions.

### Wave 4 - Regression coverage and docs alignment

- [x] Add `MainLayout` composition tests, service registration tests, and page coordination regressions.
- [x] Add delayed-response regression checks that explicitly cover perceived responsiveness, not only correctness.
- [x] Update touched functionality docs for Observability, Service Bus, and AKS when implementation lands.

## Validation

- Component tests: Completed
- Manual UX checks:
- startup and keyboard shortcut failures surface visibly through `ShellErrorPresenter`
- failure-to-logs drill-through executes exactly once per action through the page-to-logs pending-query contract
- cached page bootstrap behavior still renders immediately while Service Bus and AKS reconnect work continues in background
- scoped pages still resolve the correct demo or live implementations through `IObservabilityProviderFactory`, `IServiceBusNamespaceBootstrapper`, and `IAksClientBootstrapper`
- tab changes, navigation, and cancellation or replacement remain responsive during slow fake loads

## Notes

- Reuse `SwebKitComponentBase` where it reduces duplicated load and error plumbing.
- Do not decompose the entire page trees in this feature. Focus on bootstrap, client creation, request sequencing, and shell reporting.
- Preserve `PageDataCache` behavior and current cancellation-aware patterns as invariants unless a regression proves they need targeted change.
- This feature is not a broad performance rewrite. Use the existing cache and cancellation patterns to preserve perceived responsiveness on the scoped paths.
