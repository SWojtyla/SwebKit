# Frontend Plan - performance-optimization-program

---

title: "Frontend Plan - performance-optimization-program"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Improve the perceived and measured performance of the SwebKit Windows desktop UI by reducing startup payload, constraining unnecessary Blazor rerenders, and optimizing repeated or high-frequency interaction surfaces by feature.

## Working assumptions

- The controlling user-perceived bottlenecks are in the Blazor Hybrid UI layer, not primarily in MAUI native layout.
- Existing shared render controls are worth extending before introducing low-level Blazor micro-optimizations.
- The plan should prefer small, reversible slices with measurable outcomes over a broad refactor.

## Impacted areas

- Files / components:
  - `src/SwebKit.App/wwwroot/index.html`
  - `src/SwebKit.App/MauiProgram.cs`
  - `src/SwebKit.App/Components/Layout/MainLayout.razor`
  - `src/SwebKit.App/Components/Layout/TopBar.razor`
  - `src/SwebKit.App/Components/Layout/StatusBar.razor`
  - `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`
  - `src/SwebKit.App/Components/Shared/SwebKitLayoutBase.cs`
  - `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
  - `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
  - `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
  - `src/SwebKit.App/Components/Pages/AksPage.razor`
  - `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
  - `src/SwebKit.App/Components/Pages/StoragePage.razor`
  - related repeated-list and detail components under `Components/Aks`, `Components/Pipelines`, `Components/Storage`, `Components/ServiceBus`, `Components/Redis`, and `Components/IncidentTimeline`
- Pages / routes:
  - `/dashboard`
  - `/observability`
  - `/aks`
  - `/service-bus`
  - `/pipelines`
  - `/storage`
  - `/redis`
  - `/incident-timeline`
  - `/settings`
- Shared components:
  - shell layout and route chrome
  - command palette
  - common render-gate base classes
  - JS interop boot and feature-local interop wrappers

## UX notes

- User flows:
  - cold launch should render shell chrome quickly and keep post-start work in the background.
  - route switches into heavy workspaces should not block on assets unrelated to the current route.
  - filters, searches, and repeated row selections should feel immediate and not trigger broad subtree rerenders.
- Component states:
  - preserve existing loading, empty, and error states; optimizations must not remove operator guidance.
  - lazy-loaded assets must still show a clear first-use loading state if they are deferred.
- Accessibility:
  - virtualization, deferred rendering, and non-rendering event handlers must preserve keyboard access and focus behavior.

## API / contract changes

- No product-level API changes are planned.
- Internal UI contracts may change where repeated components need fewer parameters, fixed cascades, or different event wiring.
- Any JS-loading refactor must preserve current component contracts so feature pages do not take on shell-specific startup knowledge.

## Scope

This module covers the Blazor Hybrid host, shared shell, route pages, repeated UI components, and JS-backed feature surfaces. It does not cover backend SDK optimization or service-layer refactors unless a measured UI bottleneck is proven to originate there.

## Architecture touchpoints

- Project: `src/SwebKit.App/`
- Entry points:
  - `src/SwebKit.App/MauiProgram.cs`
  - `src/SwebKit.App/MainPage.xaml`
  - `src/SwebKit.App/wwwroot/index.html`
  - `src/SwebKit.App/Components/Layout/MainLayout.razor`
- Contracts / interfaces changed or added:
  - likely no external contracts; internal helper seams may be added for lazy asset loading, event throttling, or targeted refresh behavior.
- Data flow:
  - MAUI host boots the WebView and DI graph.
  - layout cascades and app state drive shell rendering.
  - route pages render heavy workspace subtrees that often contain repeated lists, timers, JS interop, and on-demand detail panels.

## Slice-by-slice execution plan

### Slice 1A - Baseline capture

Objective:
Create the measurement baseline that all later slices compare against.

Primary files or areas:

- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- routed pages under `src/SwebKit.App/Components/Pages/`
- feature docs in `docs/features/active/performance-optimization-program/`

Concrete tasks:

- Define the exact timing checkpoints for launch, first shell paint, first usable route, route switches, and first Logs open.
- Decide whether temporary diagnostic logging belongs in existing app logging or a minimal local-only timing helper.
- Run the baseline route sequence for Dashboard, Observability, AKS, Service Bus, and Pipelines.
- Record the before-state evidence in the status and test-plan documents.

Exit criteria:

- Before-state numbers exist for all working budgets in `index.md` that apply to Wave 1.
- The next startup slice can be validated without re-deciding how to measure it.

### Slice 1B - Startup asset deferral

Objective:
Remove non-essential editor or heavy JS startup cost from the initial app boot path.

Primary files or areas:

- `src/SwebKit.App/wwwroot/index.html`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
- supporting JS under `src/SwebKit.App/wwwroot/js/`

Preferred implementation shape:

- Remove static Monaco script loading from `index.html`.
- Introduce a route-local or component-local loader that initializes Monaco only when the Logs experience is first used.
- Ensure loader state is idempotent so repeated tab switches do not reload assets unnecessarily.
- Keep DOM-dependent initialization in `OnAfterRenderAsync` to avoid Blazor Hybrid interop timing failures.

Validation focus:

- Cold-start improvement versus baseline.
- Successful first editor open, reopen, and route-return behavior.
- No console or JS interop errors during first use.

### Slice 1C - Shell rerender hygiene

Objective:
Reduce unnecessary shell-wide rerenders while preserving startup hydration, navigation, and background initialization behavior.

Primary files or areas:

- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- `src/SwebKit.App/Components/Shared/SwebKitLayoutBase.cs`
- `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

Concrete tasks:

- Review every shell-level `CascadingValue` and mark `IsFixed` only where the value is truly stable for the subtree lifetime.
- Confirm that changing shell state does not force heavy route content to rerender unnecessarily.
- Review command palette, notification, and shortcut state flow for broad parent rerender triggers.
- Keep two-phase startup intact: shell first, heavier initialization second.

Validation focus:

- No stale shell indicators, banners, theme changes, or tab restore behavior.
- No duplicate keyboard shortcut registration.
- Improved route switch consistency after startup.

## Design decisions

| #   | Decision                                                                                        | Rationale                                                                                        | Alternative considered                  |
| --- | ----------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | --------------------------------------- |
| 1   | Optimize startup assets before micro-optimizing components                                      | Global asset cost affects every app launch, while many component optimizations are feature-local | Start with trimming or AOT first        |
| 2   | Prefer existing render-gate base classes before adding one-off `ShouldRender` logic             | SwebKit already has shared render control primitives that fit the architecture                   | Per-component ad hoc render suppression |
| 3   | Treat Observability as the first heavy workspace optimization target                            | It combines large child trees, tabs, charts, JS interop, and Monaco                              | Start with lower-cost forms or pages    |
| 4   | Reserve manual `SetParametersAsync` and non-rendering event wrappers for measured hotspots only | These techniques add complexity and stale-UI risk                                                | Apply them broadly as a default pattern |

## Implementation tasks

### Global program

- [ ] Capture a Windows baseline for cold start, first interactive shell, route switching, and memory on representative workflows.
- [ ] Document target budgets for startup and high-traffic interactions.
- [ ] Review and mark fixed cascades where safe in shell-level `CascadingValue` usage.
- [ ] Standardize render and event guidance for repeated components using the shared base classes.

### Wave 1 - Startup and shell

- [ ] Remove global Monaco startup cost by lazy-loading editor assets on first Logs usage.
- [ ] Review whether chart assets can be deferred from startup-critical paths.
- [ ] Audit `MainLayout`, `TopBar`, `StatusBar`, and shared shell interactions for unnecessary rerenders.
- [ ] Validate startup and shell navigation behavior after asset and cascade changes.

### Wave 2 - Detailed feature plans

#### Observability

Goal:
Make Observability the first optimized heavy workspace because it combines tab orchestration, charts, logs, JS interop, and auto-refresh.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
- related components under `src/SwebKit.App/Components/Observability/`

Concrete tasks:

- Revisit the current keep-mounted tab strategy and identify which tabs genuinely need state persistence versus which can be recreated cheaply.
- Ensure only the active heavy child subtree refreshes when tab-local state changes.
- Separate first-use initialization cost from steady-state rerender cost.
- Review auto-refresh and explainer or comparison state for repeated page-wide redraws.

Exit criteria:

- Tab switches feel responsive.
- Logs first use is deferred from startup and remains reliable.
- Refresh behavior no longer causes broad unnecessary rerenders.

#### Service Bus

Goal:
Reduce redraw scope in the queue or topic workspace, especially list selection and detail pane updates.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- related components under `src/SwebKit.App/Components/ServiceBus/`

Concrete tasks:

- Keep message selection state local to the active tab or detail component boundary.
- Review repeated row rendering and event handler creation in the message list.
- Ensure opening or closing the detail pane does not force unrelated namespace or tab UI to redraw.
- Review list size handling and virtualization opportunities if the current rendering strategy is still too broad.

Exit criteria:

- Message selection and detail updates are perceptibly faster.
- All message actions remain correct and safe.

#### Pipelines and Releases

Goal:
Reduce repeated rerenders from tree navigation, detail selection, and activity updates.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pipelines/`
- `src/SwebKit.App/Components/Releases/`

Concrete tasks:

- Split broad page state from tree-node state and detail-view state where needed.
- Review delegate churn in repeated tree or list nodes.
- Keep selection changes from invalidating the entire workspace.

Exit criteria:

- Expanding nodes and opening details stay responsive with no stale selection issues.

### Wave 3 - Detailed feature plans

#### AKS

Goal:
Protect the already-optimized AKS workspace from regressions and tighten the remaining hot paths around filters, panels, and dense interaction.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Aks/AksDetailPanels.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- other `src/SwebKit.App/Components/Aks/` components involved in filters or repeated lists

Concrete tasks:

- Debounce filter inputs only where rapid typing currently causes broad redraws.
- Review panel resize, drag, or mousemove-heavy paths for throttling needs.
- Preserve existing batched log rendering and virtualization behavior.
- Verify that pausing auto-refresh during detail work remains intact after any render-scope changes.

Exit criteria:

- Filter typing and panel interactions feel stable and immediate.
- Logs, YAML, shell, and port-forward behavior remain correct.

#### Storage

Goal:
Reduce redraw cost in container and blob browsing flows.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Storage/`

Concrete tasks:

- Review whether container changes redraw more than the active list and detail pane.
- Apply incremental rendering or virtualization if blob lists are large enough to justify it.
- Keep detail preview and download actions isolated from list rendering where possible.

Exit criteria:

- Container switches and blob selection remain correct and feel faster.

#### Redis

Goal:
Reduce rerender scope in key browsing and detail inspection.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Redis/`

Concrete tasks:

- Separate tree browsing from detail rendering boundaries.
- Ensure key selection does not refresh unrelated parent UI.
- Keep value-type-specific detail rendering local to the detail component boundary.

Exit criteria:

- Key browsing and detail updates remain correct and more responsive.

#### Incident Timeline

Goal:
Keep scope editing and explicit refresh responsive while preserving the evidence-first model.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/IncidentTimeline/`

Concrete tasks:

- Preserve the explicit refresh model instead of reintroducing automatic expensive refreshes.
- Review coverage strip, event list, and detail pane rendering boundaries.
- Ensure only the latest result version can paint after a refresh.

Exit criteria:

- Scope changes remain responsive.
- Refresh and detail updates do not create stale or duplicated UI.

#### Settings

Goal:
Remove any visible keystroke lag in configuration-heavy forms without changing persistence behavior.

Primary files or areas:

- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Pages/*ConfigForm.razor`

Concrete tasks:

- Identify forms where every keystroke triggers broad rerenders.
- Split local form state from page-level state where needed.
- Keep validation and persistence behavior unchanged.

Exit criteria:

- Editing settings fields feels immediate.
- Save and reload behavior stays correct.

### Wave 4 - Publish-time optimization

- [ ] Evaluate trim safety for package-heavy UI assemblies.
- [ ] Test package-size reduction opportunities and publish properties only after behavior-level gains are measured.

## Concrete implementation rules

- Do not optimize two heavy feature areas in the same slice; each slice should have one clear behavioral target.
- Prefer adding or strengthening component boundaries over introducing broad `ShouldRender` logic at the page root.
- When deferring assets or first-use initialization, design the first-use loading state explicitly instead of letting the page appear broken.
- If a change relies on JS interop, keep DOM-coupled work inside `OnAfterRenderAsync` and make the initialization idempotent.
- If a streaming or refresh path calls `StateHasChanged` frequently, batch or throttle updates instead of rendering per event.

## Validation

- Component tests: add or update behavior-scoped tests for shell startup behavior, lazy asset loading, and render-sensitive slices.
- Manual UX checks:
  - cold launch to first shell
  - first open of Observability Logs
  - route switching between heavy pages
  - filtering and selection in AKS, Service Bus, and Pipelines
- Passing result:
  - measurable or clearly perceptible responsiveness improvement for the slice
  - no stale UI regressions
  - no broken JS interop initialization

## Notes

- Existing strengths to preserve:
  - two-phase startup in `MainLayout`
  - coalesced render gates in `SwebKitComponentBase` and `SwebKitLayoutBase`
  - existing AKS virtualization and batched log rendering
- Relevant constraints from pitfalls:
  - JS interop must wait for the DOM
  - `OnParametersSetAsync` guards must be set before `await`
  - frequent `StateHasChanged` calls must be throttled or batched on streaming paths
  - `@if` destroys component state, so keep-alive decisions must be intentional
