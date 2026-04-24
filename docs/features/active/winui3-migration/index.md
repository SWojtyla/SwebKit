# Feature Overview - winui3-migration

---

title: "Feature Overview - winui3-migration"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-23"
updated: "2026-04-24"

---

## Goal

Replace the MAUI Blazor Hybrid host (`SwebKit.App`) with a native WinUI 3 application (`SwebKit.WinUI`) that renders all feature areas using native XAML controls and ViewModels, while preserving operator-facing feature parity for the current dashboard, shell, settings, and domain workflows.

## Value

MAUI Blazor Hybrid on Windows ships unnecessary cross-platform abstractions, forces WebView2 for all UI, requires CSS-based layout instead of native layout primitives, and produces known build friction (suppressed APPX warnings, `WindowsPackageType=None` workarounds, XAML generator break-on-exception hacks). Moving to native WinUI 3 eliminates all of this while retaining the entire domain and integration layer unchanged.

## Scope

**In scope:**

- New `src/SwebKit.WinUI/` project — WinUI 3 host, bootstrapped with `Microsoft.Extensions.Hosting` + MVVM
- Migration of the full operator surface: Dashboard, shell chrome, Service Bus, AKS, Redis, Storage, Pipelines/Releases/Approvals, Observability, and Settings
- MVVM ViewModels replacing Blazor `@code` blocks
- Replacement of all Fluent UI Blazor components with native WinUI 3 controls
- Replacement of Blazor-ApexCharts with LiveCharts2 (WinUI)
- Monaco editor retained via WinUI 3 `WebView2` control (same JS, no rewrite)
- UI architecture foundation in `SwebKit.WinUI`: app-level resource dictionaries, semantic tokens, reusable shell primitives, and shared page/workspace scaffolds
- Shell workspace parity: command palette, favorites, recents, route-first restore, notifications, readiness surfaces, and tray continuity where already supported today
- Theme and look-and-feel parity at the product level: persisted light/dark preset selection, recognizable production/demo cues, and a WinUI-native equivalent of the current shell identity
- Windows-specific services (`WindowsCredentialStore`, `WindowsToastNotificationService`, `WindowsTrayLifecycleService`) reused unchanged
- `SwebKit.Core` remains unchanged, and integration-project changes stay limited to bounded parity-support fixes required to preserve the existing workflows in WinUI
- Deletion of `SwebKit.App` and its test project after all domains are migrated

**Out of scope:**

- Broad changes to `SwebKit.Core`, `SwebKit.Azure`, `SwebKit.Kubernetes`, `SwebKit.Redis`, `SwebKit.DevOps`, or `SwebKit.Observability` beyond targeted WinUI parity support
- Incident Timeline migration or redesign; the current feature is intentionally excluded from the WinUI cutover plan
- E2E test migration (defer until the host is stable)
- Non-Windows targets (never existed in practice)
- Cross-platform portability

## Phases

| Phase | Deliverable                                                                                                                              | State       |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------- | ----------- |
| 0     | Blank `SwebKit.WinUI` project added to solution, boots to empty window, all domain projects referenced                                   | Done        |
| 1     | Shell + Dashboard: `MainWindow`, navigation, workspace hub, command palette, status/notification surfaces, settings and theme baseline   | Done        |
| 2     | Service Bus workspace: namespace connect, entity tree, active/DLQ/scheduled tabs, composer/templates, filters/columns, workspace restore | Done        |
| 3     | AKS workspace: cluster/resource browse, diagnostics panels, batch actions, monitoring continuity, pod shell                              | In Progress |
| 4     | Redis workspace: scan/tree, TTL and value workflows, health/prefix tooling, bulk operations                                              | In Progress |
| 5     | Storage workspace: container/blob browse, preview, SAS/download workflows, workspace restore                                             | In Progress |
| 6     | Pipelines delivery hub: pipelines, activity, release records, approvals, tagging                                                         | In Progress |
| 7     | Observability workspace: discovery, five tabs, guided/advanced logs, saved queries, charts                                               | In Progress |
| 8     | Delete `SwebKit.App`, update solution, verify all tests pass                                                                             | Not started |

## Parity Scope Additions

The original migration outline under-described the current MAUI app. Cutover cannot be judged by page existence alone; it must be judged by operator-surface parity.

### Keep in this plan

- Dashboard parity: readiness summary, health tiles, favorites, recent activity, and pod-health monitoring summary.
- Shell parity: top-bar context, workspace hub, notification history, command palette, status bar, connection badges, production/demo cues, and profile-load warning/recovery cues.
- Settings parity: all existing configuration sections, readiness summaries, live checks, and section/deep-link entry paths that exist today.
- Theme/look-and-feel parity: persisted theme selection, curated dark/light presets, and a recognizable WinUI-native shell identity. Exact CSS-era pixel matching is not required.
- Detailed per-domain parity for Service Bus, AKS, Redis, Storage, Pipelines, and Observability as listed in `frontend.md`.

### Round 2 candidate backlog

- Extra WinUI-only personalization beyond today's MAUI baseline, such as additional preset themes or finer-grained styling controls.
- Visual polish passes that chase exact CSS-era aesthetics after functional parity is stable.

## Execution strategy

The remaining work should not be treated as a flat page-by-page migration.

- Start with a reusable UI foundation: resource dictionaries, theme application, shell primitives, and a page/workspace scaffold should exist before the remaining feature pages multiply.
- Move shared shell completion earlier: dashboard, top-bar/status surfaces, workspace hub, notification history, theme application, and settings-readiness flows should be completed before broadening into the remaining domains.
- Keep AKS moving in bounded slices because it is already live and is the highest-risk workspace, but do not let AKS consume the entire roadmap while shell/dashboard/theme parity stays unfinished.
- After the shared shell pass is stable, Redis and Storage are the best next parallel workstreams.
- Pipelines and Observability follow once the shared shell is stable and the WinUI chart/editor seams are locked.
- Cutover hardening follows once the remaining shared-shell, Pipelines, and Observability parity gaps are credible.

`frontend.md` is the detailed source of truth for execution ordering and cutover gates.

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
- **Risk:** Shell identity drift — replacing the CSS-driven MAUI shell with native WinUI 3 can accidentally drop the current dashboard, notification, and theme affordances even if the pages technically exist.  
  **Mitigation:** Treat the curated theme system, shell status cues, dashboard, and settings/readiness surfaces as explicit cutover scope rather than optional polish.

- **Risk:** Feature parity regression during the parallel branch period.  
  **Mitigation:** `SwebKit.App` stays fully operational. `SwebKit.WinUI` is a separate csproj. No changes to the existing app until Phase 8.

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Design: `docs/architecture/design.md`
- Codebase guide: `docs/architecture/codebase-guide.md`
- Pitfalls: `docs/pitfalls/dotnet-csharp.md`
- Follow-up hardening plan: `docs/features/active/winui3-cutover-audit-hardening/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Implementation: `frontend.md`
- Test plan: `test-plan.md`
- Library decisions: `decisions.md`
- Follow-up: `docs/features/active/winui3-cutover-audit-hardening/`
