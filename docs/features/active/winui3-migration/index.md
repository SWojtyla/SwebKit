# Feature Overview - winui3-migration

---

title: "Feature Overview - winui3-migration"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-23"
updated: "2026-04-23"

---

## Goal

Replace the MAUI Blazor Hybrid host (`SwebKit.App`) with a native WinUI 3 application (`SwebKit.WinUI`) that renders all feature areas using native XAML controls and ViewModels.

## Value

MAUI Blazor Hybrid on Windows ships unnecessary cross-platform abstractions, forces WebView2 for all UI, requires CSS-based layout instead of native layout primitives, and produces known build friction (suppressed APPX warnings, `WindowsPackageType=None` workarounds, XAML generator break-on-exception hacks). Moving to native WinUI 3 eliminates all of this while retaining the entire domain and integration layer unchanged.

## Scope

**In scope:**

- New `src/SwebKit.WinUI/` project — WinUI 3 host, bootstrapped with `Microsoft.Extensions.Hosting` + MVVM
- Migration of all 10 feature domains (Shell, ServiceBus, AKS, Redis, Storage, Pipelines, Releases, Observability, IncidentTimeline, Settings)
- MVVM ViewModels replacing Blazor `@code` blocks
- Replacement of all Fluent UI Blazor components with native WinUI 3 controls
- Replacement of Blazor-ApexCharts with LiveCharts2 (WinUI)
- Monaco editor retained via WinUI 3 `WebView2` control (same JS, no rewrite)
- Windows-specific services (`WindowsCredentialStore`, `WindowsToastNotificationService`, `WindowsTrayLifecycleService`) reused unchanged
- All 6 integration projects and `SwebKit.Core` left completely untouched
- Deletion of `SwebKit.App` and its test project after all domains are migrated

**Out of scope:**

- Any changes to `SwebKit.Core`, `SwebKit.Azure`, `SwebKit.Kubernetes`, `SwebKit.Redis`, `SwebKit.DevOps`, `SwebKit.Observability`
- E2E test migration (defer until the host is stable)
- Non-Windows targets (never existed in practice)
- Cross-platform portability

## Phases

| Phase | Deliverable                                                                                             | State       |
| ----- | ------------------------------------------------------------------------------------------------------- | ----------- |
| 0     | Blank `SwebKit.WinUI` project added to solution, boots to empty window, all domain projects referenced  | Not started |
| 1     | Shell: `MainWindow`, `NavigationView`, `TabView`, DI host, settings persistence, credential store wired | Not started |
| 2     | ServiceBus domain: namespace connect, queue/topic tree, message browse, DLQ                             | Not started |
| 3     | AKS domain: cluster connect, pod list, logs, port-forward, pod shell                                    | Not started |
| 4     | Redis domain: key browser, TTL/value inspector                                                          | Not started |
| 5     | Storage domain: container/blob browse, download                                                         | Not started |
| 6     | Pipelines + Releases + Approvals                                                                        | Not started |
| 7     | Observability: resource picker, overview/failures/performance/logs/availability tabs + charts           | Not started |
| 8     | Incident Timeline workbench                                                                             | Not started |
| 9     | Delete `SwebKit.App`, update solution, verify all tests pass                                            | Not started |

## Dependencies

- `SwebKit.Core` — all domain contracts and repositories (no changes)
- `SwebKit.Azure`, `SwebKit.Kubernetes`, `SwebKit.Redis`, `SwebKit.DevOps`, `SwebKit.Observability` — all integration projects (no changes)
- NuGet: `Microsoft.WindowsAppSDK`, `CommunityToolkit.WinUI`, `CommunityToolkit.Mvvm`, `LiveChartsCore.SkiaSharpView.WinUI`, `Microsoft.Web.WebView2`
- Pitfalls: `docs/pitfalls/dotnet-csharp.md` (CS-3 DelegatingHandler, CS-4 atomic writes)

## Risks & mitigations

- **Risk:** Tab system parity — `TabService` in MAUI has a rich tab model (restore, pinned ports, closeable, context). WinUI 3 `TabView` covers basics but the service layer must be ported carefully.  
  **Mitigation:** Port `TabService` as a ViewModel-layer service before migrating any feature domain.
- **Risk:** `WindowsTrayLifecycleService` references `Microsoft.Maui.Controls.Application.Current` in one call (line 288). Must be replaced with a WinUI 3 `Application` reference.  
  **Mitigation:** Trivial one-line change; flag in Phase 0 as a known touch.
- **Risk:** Monaco editor in `WebView2` — current MAUI `BlazorWebView` hosts Monaco via JS interop rooted in the Blazor rendering loop. In WinUI 3, Monaco will be hosted directly in a `WebView2` control with a custom HTML wrapper.  
  **Mitigation:** The JS assets (`keyboardShortcuts.js`, YAML highlighting) are already in `wwwroot/js/` and can be served from the WinUI 3 app package or loaded as app content.

- **Risk:** Feature parity regression during the parallel branch period.  
  **Mitigation:** `SwebKit.App` stays fully operational. `SwebKit.WinUI` is a separate csproj. No changes to the existing app until Phase 9.

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Design: `docs/architecture/design.md`
- Codebase guide: `docs/architecture/codebase-guide.md`
- Pitfalls: `docs/pitfalls/dotnet-csharp.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Implementation: `frontend.md`
- Library decisions: `decisions.md`
