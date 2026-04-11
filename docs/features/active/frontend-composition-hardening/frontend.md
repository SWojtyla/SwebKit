# Frontend Plan - frontend-composition-hardening

---

title: "Frontend Plan - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Keep the existing top-level UX intact while moving client construction, shell error surfacing, and fragile request sequencing into stable seams that are easier to test and reason about.

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
- Shell initialization and keyboard shortcut registration failures should surface through a shared UX path rather than only through console output.
- Failure-to-logs in Observability remains a one-click action but moves to an explicit readiness handshake instead of a timing delay.
- Cached back-navigation and quick-return behavior should remain intact where `PageDataCache` already helps.

## API / contract changes

- Add small creation interfaces in `SwebKit.Core` for the concrete providers and clients currently created inside Razor pages.
- Register the real and demo implementations in `src/SwebKit.App/MauiProgram.cs` so pages request abstractions rather than concrete infrastructure types.
- Introduce an explicit page-to-child handoff for Observability logs drill-through. Acceptable forms are a pending request model, a tab-ready callback, or a small coordinator service. Timing sleeps are not acceptable.
- Keep `PageDataCache` keys, TTL assumptions, and current route identifiers stable unless a verified regression requires a focused change.

## Tasks

### Wave 1 - Shared seams and shell error path

- [ ] Add a shell error presentation service and route `MainLayout` background initialization and JS registration failures through it.
- [ ] Add provider and client factory or connector interfaces and wire them in the composition root.
- [ ] Add bUnit-friendly test doubles for the new seams.

### Wave 2 - Observability hardening

- [ ] Refactor `ObservabilityPage` to activate providers through an injected abstraction.
- [ ] Replace the current delay-based drill-through with an explicit render-ready handoff to `ObservabilityLogs`.
- [ ] Keep auto-refresh, saved queries, guided mode, and advanced mode behavior unchanged.

### Wave 3 - Service Bus and AKS bootstrap hardening

- [ ] Move Service Bus namespace connection workflow out of `ServiceBusPage` into an injected connector or page service.
- [ ] Move AKS client bootstrap and reconnection path out of `AksPage` into an injected seam without rewriting the detail panels.
- [ ] Preserve existing cancellation behavior, selection behavior, and `PageDataCache` snapshot support.

### Wave 4 - Regression coverage and docs alignment

- [ ] Add `MainLayout` composition tests, service registration tests, and page coordination regressions.
- [ ] Update touched functionality docs for Observability, Service Bus, and AKS when implementation lands.

## Validation

- Component tests: Not started
- Manual UX checks:
- confirm startup and keyboard shortcut failures surface visibly
- confirm failure-to-logs drill-through executes exactly once per action
- confirm cached page bootstrap behavior still feels instant on return navigation
- confirm demo mode still resolves the correct implementations for the scoped pages

## Notes

- Reuse `SwebKitComponentBase` where it reduces duplicated load and error plumbing.
- Do not decompose the entire page trees in this feature. Focus on bootstrap, client creation, request sequencing, and shell reporting.
- Preserve `PageDataCache` behavior and current cancellation-aware patterns as invariants unless a regression proves they need targeted change.