# Feature Overview - performance-optimization-program

---

title: "Feature Overview - performance-optimization-program"
owner: "GitHub Copilot"
status: "Planned"
jira: ""
created: "2026-04-28"
updated: "2026-04-28"

---

## Goal

Define and sequence a concrete, measurable performance improvement program for SwebKit that improves startup responsiveness, reduces unnecessary Blazor rerendering, and removes lag in the heaviest operator workflows.

## Success metrics

These are working budgets for implementation planning. Wave 1 must validate or revise them using actual baseline data before later waves are started.

| Area                                | Working target                                                            | Notes                                                         |
| ----------------------------------- | ------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Cold launch to visible shell chrome | Improve baseline by at least 20% or 300 ms                                | Measured from app launch to first shell paint                 |
| Cold launch to first usable route   | Improve baseline by at least 20%                                          | Measured through startup hydration and first interactive page |
| Route switch into heavy page        | Under 500 ms perceived delay after warm navigation                        | Measured for Observability, AKS, Service Bus, Pipelines       |
| Filter or selection response        | Under 150 ms for common interactions                                      | Applies to repeated lists, grids, and detail panes            |
| First Monaco open                   | No cold-start cost; first interactive editor under 1500 ms                | Only on first Logs usage                                      |
| Auto-refresh and streaming paths    | No visible render thrash or runaway CPU spikes during a 60 second session | Manual and diagnostic validation                              |

## Non-goals

- Rewriting feature pages into a different UI framework.
- Refactoring integration SDK layers without evidence that they are the controlling bottleneck.
- Broad visual redesign work that is not required to support performance changes.
- AOT, trimming, or publish-time experiments before behavior-level wins are measured.

## Value

SwebKit is a desktop operations tool that is expected to feel instant during navigation, filtering, tab switching, and investigation work. The current app already applies some good patterns, such as two-phase startup and render coalescing, but it still carries startup cost from globally loaded web assets and has several feature areas with repeated UI and high-frequency events. This feature creates the execution plan needed to improve user-perceived responsiveness without speculative refactors.

## Scope

- In scope:
  - Establish an app-wide performance strategy for the MAUI Blazor Hybrid host and the Blazor UI tree.
  - Define a startup optimization wave focused on shell interactivity and always-loaded assets.
  - Define a rendering optimization wave focused on rerender control, event frequency, and repeated UI patterns.
  - Define feature-by-feature optimization work for Shell/Dashboard, Observability, AKS, Service Bus, Pipelines/Releases, Storage, Redis, Incident Timeline, and Settings.
  - Define validation budgets, profiling checkpoints, and success criteria.
- Out of scope:
  - Implementing the optimizations in code.
  - Shipping changes to Azure DevOps or creating a PR.
  - Reworking app functionality unrelated to measured or likely performance cost.

### Planned waves

- Wave 1 - Baseline and startup: measure cold-start and first-interactive timings, then reduce startup cost from globally loaded assets and shell-wide rerenders.
- Wave 2 - Heavy workspace rendering: optimize Observability, Service Bus, and Pipelines/Releases where repeated UI and tab switching are most likely to lag.
- Wave 3 - Interaction hot paths: optimize AKS, Storage, Redis, and Incident Timeline filters, panels, and repeated lists.
- Wave 4 - Publish-time optimization: test trimming, package-size reduction, and publish configuration changes only after behavior-level wins are measured.

## Execution order

| Slice | Scope                          | Primary files or areas                                                                                                                  | Deliverable                                                         | Exit criteria                                                 |
| ----- | ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------- |
| 1A    | Baseline capture               | `src/SwebKit.App/Components/Layout/MainLayout.razor`, route pages, targeted logging hooks                                               | Before-state timings and interaction notes recorded in feature docs | Cold start, route switch, and key interaction baselines exist |
| 1B    | Startup asset deferral         | `src/SwebKit.App/wwwroot/index.html`, `src/SwebKit.App/Components/Observability/`, shared JS boot helpers                               | Monaco no longer loads globally at startup                          | Startup improves and Logs still initializes correctly         |
| 1C    | Shell rerender hygiene         | `src/SwebKit.App/Components/Layout/`, shared render bases                                                                               | Shell cascades and global layout rerender less often                | No stale shell state; navigation and shortcuts still work     |
| 2A    | Observability                  | `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`, `src/SwebKit.App/Components/Observability/`                                 | Tab lifecycle and heavy child rendering tightened                   | Tab switch, refresh, and Logs first open meet budgets         |
| 2B    | Service Bus                    | `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`, `src/SwebKit.App/Components/ServiceBus/`                                       | Message list and detail pane redraw scope reduced                   | Selection and detail interactions remain correct and faster   |
| 2C    | Pipelines/Releases             | `src/SwebKit.App/Components/Pages/PipelinesPage.razor`, `src/SwebKit.App/Components/Pipelines/`, `src/SwebKit.App/Components/Releases/` | Tree and detail view rerender behavior tightened                    | Expand, select, and detail open flows stay responsive         |
| 3A    | AKS                            | `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Aks/`                                                     | Filter and panel hot paths throttled where needed                   | No regression in logs, YAML, shell, or port-forward flows     |
| 3B    | Storage and Redis              | `src/SwebKit.App/Components/Pages/StoragePage.razor`, `src/SwebKit.App/Components/Pages/RedisPage.razor`                                | Repeated list and detail pane work reduced                          | Selection, paging, and preview flows stay correct             |
| 3C    | Incident Timeline and Settings | `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`, `src/SwebKit.App/Components/Pages/SettingsPage.razor`                    | Explicit refresh and forms remain responsive                        | No stale UI and no keystroke lag                              |
| 4A    | Publish-time experiments       | publish configuration, package references, startup assets                                                                               | Optional packaging improvements with documented safety limits       | No runtime breakage and measurable startup or footprint gain  |

## Dependencies

- Architecture constraints:
  - [docs/architecture/architecture.md](c:/Projects/Personal/SwebKit/docs/architecture/architecture.md)
  - [docs/architecture/design.md](c:/Projects/Personal/SwebKit/docs/architecture/design.md)
  - [docs/architecture/codebase-guide.md](c:/Projects/Personal/SwebKit/docs/architecture/codebase-guide.md)
- Relevant source guidance:
  - Microsoft Learn: .NET MAUI app performance
  - Microsoft Learn: ASP.NET Core Blazor rendering performance
  - Syncfusion: .NET MAUI performance best practices
- Relevant packages and runtime boundaries:
  - `Microsoft.AspNetCore.Components.WebView.Maui`
  - `BlazorMonaco`
  - `Blazor-ApexCharts`
  - Fluent UI Blazor components
- Pitfall files that apply:
  - [docs/pitfalls/blazor-maui.md](c:/Projects/Personal/SwebKit/docs/pitfalls/blazor-maui.md)
  - [docs/pitfalls/dotnet-csharp.md](c:/Projects/Personal/SwebKit/docs/pitfalls/dotnet-csharp.md)

## Architecture touchpoints

- Startup and DI composition: `src/SwebKit.App/MauiProgram.cs`
- Shell bootstrap and background hydration: `src/SwebKit.App/Components/Layout/MainLayout.razor`
- Command and shortcut plumbing: `src/SwebKit.App/Services/CommandRegistry.cs`, `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`
- Shared rerender controls: `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`, `src/SwebKit.App/Components/Shared/SwebKitLayoutBase.cs`
- Page-level heavy workspaces: `src/SwebKit.App/Components/Pages/*.razor`
- State persistence boundaries: `src/SwebKit.Core/Configuration/ProfileRepository.cs`, `src/SwebKit.Core/Configuration/UiStateRepository.cs`, `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`

## Risks & mitigations

- Risk: optimizing before measuring leads to churn and hard-to-explain regressions. - Mitigation: require a baseline and a narrow validation step for each optimization slice.
- Risk: trimming or publish-time changes break reflection-heavy UI libraries. - Mitigation: keep trimming and packaging changes in a late wave with targeted smoke coverage.
- Risk: aggressive rerender suppression causes stale UI. - Mitigation: restrict `ShouldRender`, `IHandleEvent`, or manual parameter optimizations to measured hotspots and verify state transitions explicitly.
- Risk: feature-local optimizations drift from shared shell patterns. - Mitigation: prefer shared render and event patterns via existing base classes before introducing one-off behavior.
- Risk: route-local asset loading introduces first-use failures in Blazor Hybrid. - Mitigation: make first-use loading explicit, keep JS interop inside `OnAfterRenderAsync`, and validate reopen behavior.
- Risk: baseline data becomes noisy or incomparable across waves. - Mitigation: record one stable machine profile, one stable route sequence, and one stable demo and real-data comparison path.

## Related documents

- Architecture section: [docs/architecture/architecture.md](c:/Projects/Personal/SwebKit/docs/architecture/architecture.md)
- Design flows: [docs/architecture/design.md](c:/Projects/Personal/SwebKit/docs/architecture/design.md)
- Code navigation: [docs/architecture/codebase-guide.md](c:/Projects/Personal/SwebKit/docs/architecture/codebase-guide.md)
- Pitfalls:
  - [docs/pitfalls/blazor-maui.md](c:/Projects/Personal/SwebKit/docs/pitfalls/blazor-maui.md)
  - [docs/pitfalls/dotnet-csharp.md](c:/Projects/Personal/SwebKit/docs/pitfalls/dotnet-csharp.md)

## Quick links

- Jira: not linked
- Status: [status.md](c:/Projects/Personal/SwebKit/docs/features/active/performance-optimization-program/status.md)
- Tests: [test-plan.md](c:/Projects/Personal/SwebKit/docs/features/active/performance-optimization-program/test-plan.md)
- Implementation modules:
  - [frontend.md](c:/Projects/Personal/SwebKit/docs/features/active/performance-optimization-program/frontend.md)
  - [decisions.md](c:/Projects/Personal/SwebKit/docs/features/active/performance-optimization-program/decisions.md)
