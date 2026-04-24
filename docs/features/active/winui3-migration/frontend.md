# Frontend — WinUI 3 Migration Plan

This document covers the implementation approach for each migration phase. Each phase is self-contained and leaves the app bootable.

---

## Project scaffold

### New project: `src/SwebKit.WinUI/SwebKit.WinUI.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>SwebKit.WinUI</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
    <Platforms>x64</Platforms>
  </PropertyGroup>
  <ItemGroup>
    <!-- WinUI 3 -->
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.*" />
    <!-- Community Toolkit -->
    <PackageReference Include="CommunityToolkit.WinUI.Controls.DataGrid" Version="8.*" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Segmented" Version="8.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <!-- Charts -->
    <PackageReference Include="LiveChartsCore.SkiaSharpView.WinUI" Version="2.*" />
    <!-- Domain projects -->
    <ProjectReference Include="..\SwebKit.Core\SwebKit.Core.csproj" />
    <ProjectReference Include="..\SwebKit.Azure\SwebKit.Azure.csproj" />
    <ProjectReference Include="..\SwebKit.Kubernetes\SwebKit.Kubernetes.csproj" />
    <ProjectReference Include="..\SwebKit.Redis\SwebKit.Redis.csproj" />
    <ProjectReference Include="..\SwebKit.DevOps\SwebKit.DevOps.csproj" />
    <ProjectReference Include="..\SwebKit.Observability\SwebKit.Observability.csproj" />
  </ItemGroup>
</Project>
```

### Solution entry (`SwebKit.slnx`)

Add to the `/src/` folder:

```xml
<Project Path="src/SwebKit.WinUI/SwebKit.WinUI.csproj" />
```

Do NOT remove `SwebKit.App` until Phase 9. Both projects coexist in the solution.

---

## Phase 0 — Blank shell + DI host

### Entry point

WinUI 3 does not use `MauiApp.CreateBuilder`. Instead, use a generic host in `App.xaml.cs`:

```csharp
// App.xaml.cs
public partial class App : Application
{
    public IHost Host { get; }

    public App()
    {
        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureServices(RegisterServices)
            .Build();

        InitializeComponent();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Copy registrations from MauiProgram.cs verbatim
        // Replace builder.Services.AddXxx(...) → services.AddXxx(...)
        // Remove: AddMauiBlazorWebView, AddBlazorWebViewDeveloperTools, AddFluentUIComponents
        // Keep: everything from SwebKit.Core, SwebKit.Azure, SwebKit.Kubernetes, etc.
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
    }
}
```

### `MainWindow.xaml` (Phase 0 placeholder)

```xml
<Window x:Class="SwebKit.WinUI.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SwebKit">
    <Grid>
        <TextBlock Text="SwebKit — WinUI 3 shell placeholder"
                   VerticalAlignment="Center" HorizontalAlignment="Center" />
    </Grid>
</Window>
```

### Known one-line fix: `WindowsTrayLifecycleService.cs`

Line 288 currently reads:

```csharp
var app = Microsoft.Maui.Controls.Application.Current;
```

Replace with:

```csharp
var app = Microsoft.UI.Xaml.Application.Current;
```

This file lives in `SwebKit.App/Platforms/Windows/`. It will be **copied** to `SwebKit.WinUI/Platforms/Windows/` and patched there. The original in `SwebKit.App` is not touched.

---

## Phase 1 — Shell

### Shell layout structure

```
MainWindow
└── NavigationView (left nav, collapsible)
    ├── NavigationViewItem: Service Bus
    ├── NavigationViewItem: AKS
    ├── NavigationViewItem: Redis
    ├── NavigationViewItem: Storage
    ├── NavigationViewItem: Pipelines
    ├── NavigationViewItem: Observability
    ├── NavigationViewItem: Incident Timeline
    └── NavigationViewItem (footer): Settings
        Content:
        └── Frame (host for tab-based or single-page content)
            └── TabViewPage (wraps TabView for multi-tab areas)
```

### Tab system

Port `TabService` to a plain `ObservableObject` ViewModel. The `TabView` control in WinUI 3 (`Microsoft.UI.Xaml.Controls.TabView`) handles the tab strip natively; `TabService` becomes the backing model only.

Key difference from Blazor: WinUI 3 tab content is a `Frame` per tab with page navigation. Each tab stores its `PageType` and navigation parameter; on tab switch, the frame navigates to the correct page.

### `OperatorWorkspaceService` and command palette

`OperatorWorkspaceService` is a pure .NET service — it moves unchanged. The command palette becomes an `AutoSuggestBox` in a flyout triggered by `Ctrl+K` via a WinUI 3 keyboard accelerator:

```csharp
// In MainWindow, registered after window activation:
var accelerator = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
accelerator.Invoked += (_, _) => CommandPaletteFlyout.ShowAt(MainGrid);
```

### Settings persistence

No changes to `ProfileRepository`, `UiStateRepository`, `UserSettingsRepository`. These are injected directly into ViewModels. Same `%APPDATA%/SwebKit` path.

## UI foundation architecture (added 2026-04-24)

The migration now needs an explicit reusable UI layer, not just more pages.

### Current repo evidence

- `src/SwebKit.WinUI/App.xaml` currently merges only `XamlControlsResources`, so there is not yet an app-specific resource-dictionary stack.
- Current WinUI pages (`ServiceBusPage.xaml`, `AksPage.xaml`, `SettingsPage.xaml`) compose cards, headers, and layout inline rather than through shared shell/page primitives.
- `SettingsViewModel` currently persists only `system`, `dark`, and `light` theme keys, while the MAUI host already defines a curated theme vocabulary that is richer than a simple light/dark toggle.

This is enough for bootstrap work, but it is not yet a durable UI architecture.

### Foundation goals

- New workspaces should be assembled from shared primitives rather than inventing new card, header, and pane layouts per page.
- Theme selection should be a global shell concern with semantic tokens, not page-local brush choices.
- The app should feel sleek and intentional from the first real feature passes, not after a cleanup round.
- Service Bus, AKS, Redis, Storage, Pipelines, Observability, and Incident Timeline should all inherit one visual and interaction system.

### Proposed shared layer

```
src/SwebKit.WinUI/
├── Resources/
│   ├── Tokens/           # spacing, radius, typography, icon sizes, semantic elevations
│   ├── Themes/           # curated light/dark theme dictionaries and theme overrides
│   └── Styles/           # shared control, card, banner, badge, and layout styles
├── Controls/
│   ├── Shell/            # title bar, status bar, workspace hub, notification host, command palette host
│   └── Shared/           # page scaffold, section card, metric card, state view, detail pane host
├── Views/
├── ViewModels/
└── Services/
    └── Theme/            # theme coordinator and shell-level theme application
```

### Resource and theme architecture

- Add semantic design tokens instead of relying on raw WinUI brushes directly in every page.
- Keep WinUI theme resources as the base layer, then map them into SwebKit semantic resources such as shell chrome, workspace surface, warning banner, metric accent, and detail-pane background.
- Curated theme variants should live in dedicated dictionaries and override semantic tokens, not duplicate whole page styles.
- Theme selection should be applied globally through one shell-level theme coordinator that reads `UserSettingsRepository` and updates the active dictionaries without requiring per-page logic.
- New pages should consume semantic resources first. Raw brush references should be rare and justified.

### Reusable shell and page primitives

- `PageScaffold` or `WorkspaceScaffold`: page title, subtitle, meta badges, toolbar slot, primary content, and optional secondary pane.
- `SectionCard` and `MetricCard`: consistent surface, spacing, header treatment, and action placement.
- `StateView`: shared empty, loading, error, and not-configured states.
- `Banner` primitives: production cue, demo cue, profile recovery warning, and destructive-action warning.
- Shared chrome components: shell title bar, status bar, notification panel/history surface, workspace hub host, and command palette host.
- Shared split-layout host for workspaces that combine browse and detail panes.

### Page composition contract

- Each routed page should declare its content through a common scaffold before adding page-specific widgets.
- The first screen region should always be recognizable: route title, operator context, primary actions, and state cues.
- Complex workspaces should use a repeatable structure: header/action row, summary strip, primary list/tree/grid, and optional detail or diagnostics pane.
- Loading, empty, error, and not-configured experiences must come from shared state components rather than page-specific wording and layout every time.
- Production-sensitive actions must reuse shared confirmation affordances and warning language.

### Visual direction

- Aim for an operations-oriented interface: dense but calm, strong information hierarchy, restrained accents, and consistent surface depth.
- Use a small set of repeatable surfaces rather than a new visual treatment per workspace.
- Keep typography and spacing intentional so dense data views still read as one product, not a stack of unrelated WinUI samples.
- Let theme variants change mood and brand cues, but keep layout, hierarchy, and interaction patterns stable.

### Adoption strategy

- Build the shared layer before broadening far beyond the currently active AKS route.
- Use `Settings`, `ServiceBus`, and `AKS` as proving grounds to refactor early pages onto the shared scaffold and token system.
- Only after that foundation is stable should Redis, Storage, Pipelines, and Observability proceed in parallel.

## Cutover Parity Baseline (added 2026-04-24)

The original migration plan was too page-oriented and understated the current MAUI app surface. For this feature, parity means matching the operator workflows that exist today, not just recreating route shells.

### Included in this plan before Phase 9

#### Shell, dashboard, and look-and-feel

- Dashboard route with readiness summary, health tiles, favorites, recent activity, and pod-health monitoring summary.
- Shell chrome parity: top-bar context, current-area connection badge, production/demo/profile-recovery cues, status bar, background-task display, and port-forward session indicator.
- Workspace hub parity: current resource card, named favorites, recents, dashboard pins, and route-first restore backed by `OperatorWorkspaceService`.
- Notification history plus toast surfaces.
- Command palette parity for commands, favorites, recent resources, and resource search results.
- Theme/look-and-feel parity at the product level: persisted theme selection, six curated dark/light presets, and a recognizable WinUI-native shell identity rather than a pixel-for-pixel CSS port.
- Full Settings parity beyond the current WinUI baseline: all existing sections, readiness summaries, live checks, and query/deep-link entry points.
- Windows tray continuity for the monitoring flows that already depend on it today.

#### Service Bus

- Namespace add/remove plus cached reconnect semantics.
- Active, DLQ, and scheduled-message workspaces with semantic tab restore.
- Compose, replay, edit, and schedule workflows, including template management.
- Advanced multi-field filters, saved filter state, column chooser, custom columns, row-density preferences, and load-more behavior.
- Selected-message and filtered destructive actions, JSON export, purge, and production-safe confirmations.
- Favorite/resource snapshot restore parity through the shared workspace model.

#### AKS

- Resource browse parity for Deployments, StatefulSets, Pods, Services, Ingresses, GatewayClasses, Gateways, HTTPRoutes, Jobs, CronJobs, and Events.
- Diagnostics panel parity for YAML plus search, pod and multi-pod logs with history/export, container details, ConfigMap and Secret inspection, HPA detail, ingress analysis, network-policy analysis, and Helm history/values/rollback.
- Operational action parity for restart, scale, delete, CronJob run-now, Job rerun, port-forward session management, and pod shell launch.
- Monitoring and continuity parity for all-namespaces mode, namespace filtering, tray-backed pod-health monitoring, and semantic workspace restore.

#### Redis

- Multi-cache configuration and connection-test parity.
- Pattern scan, load-more pagination, namespace-tree grouping, separator persistence, and type-badge loading.
- TTL visualization plus set/remove operations, value editing, rename/export flows, and selected-key bulk delete.
- Prefix memory analysis, keyspace health explorer, slow-log surface, and set-member paging.

#### Storage

- Container tree, virtual-folder traversal, breadcrumb navigation, and blob detail parity.
- Inline preview parity for text, JSON, and XML with the same size-gated behavior.
- Download parity for blob/version download progress, bulk ZIP download, SAS copy, and direct URL copy flows.
- Shared workspace snapshot restore for account, container, and blob context.

#### Pipelines, releases, and approvals

- Pipeline browser parity with project tree, recent-run detail, and inline trigger flows.
- Activity feed parity across Azure DevOps projects.
- Release-record parity with component-by-environment matrix, local persistence, and tag manager workflows.
- Approval center parity with aging/SLA state, inline approve/reject flows, and production confirmation requirements.
- Failure-classification and `/releases` alias behavior retained.

#### Observability

- Resource discovery parity across subscriptions plus the current identity cue.
- Five-tab parity: Overview, Failures, Performance, Logs, and Availability.
- Guided and advanced KQL flows, presets, saved queries, drill-to-logs handoff, export/copy actions, and Monaco-hosted editing.
- Threshold-aware indicators and query row-cap settings parity.
- Shared workspace restore for resource, tab, and time-range context.

#### Incident Timeline

- Workbench parity for scope toolbar, source coverage strip, evidence timeline, and detail panel.
- Mapping guidance and deep-link behavior into Settings.
- Investigation-seed flows from Observability, Service Bus, and Pipelines.
- Snapshot export and advisory mapping-proposal flows.

### Round 2 candidate backlog

- Additional WinUI-only personalization beyond the current six curated theme presets.
- Post-parity visual polish that refines the native WinUI presentation without changing functionality.

Use this section, not the short phase headlines alone, as the cutover source of truth.

## Recommended Execution Sequence (added 2026-04-24)

### Ordering principle

- Shared shell/dashboard/settings/theme work is now a first-class dependency, not a final polish pass.
- AKS remains the active domain and should continue in bounded slices because the WinUI route is already live.
- Later workspaces should start only after the shared shell is stable enough that their parity work is not forced to rework navigation, notifications, theme behavior, or readiness flows.

### Ordered implementation tracks

#### Track 1 — UI foundation and shared shell completion

Do this before opening more domain breadth beyond the already-active AKS route.

- Resource/theme foundation: semantic tokens, theme dictionaries, and a global theme coordinator.
- Reusable primitives: shell chrome components, page/workspace scaffold, section/metric/state surfaces, and detail-pane layout hosts.
- Dashboard route and cards: readiness summary, health tiles, favorites, recent activity, and pod-health summary.
- Shell chrome: top bar, status bar, notification history, current-area cues, production/demo/recovery banners, and route context.
- Workspace surfaces: current-resource hub, favorites, recents, dashboard pins, and route-first restore affordances.
- Theme system: expand the current `system` / `dark` / `light` baseline into the curated theme set and apply it to the actual WinUI shell, not just the settings selector.
- Settings/readiness pass: section parity, readiness cards, live checks, and existing deep-link/query entry behavior.

#### Track 2 — AKS completion

Continue AKS immediately, but keep the slices narrow.

- Slice 2: pod logs, YAML viewer/search, and basic diagnostics entry points.
- Slice 3: port-forward sessions and pod shell.
- Slice 4: remaining diagnostics panels, monitoring continuity, and broader resource/workflow parity.

#### Track 3 — Redis and Storage

Start these once Track 1 is stable. They are the best parallelizable next workstreams because they have fewer cross-domain dependencies than Pipelines or Observability.

- Redis: key tree, TTL/value workflows, health/prefix tooling, export/delete parity.
- Storage: container/blob browse, preview, SAS/download workflows, and workspace restore.

#### Track 4 — Pipelines and Observability

Start these after the shared shell is stable and the WinUI equivalents for Monaco hosting and LiveCharts usage are fixed.

- Pipelines/Releases: browser, activity feed, release records, approvals, tag manager.
- Observability: discovery, five-tab experience, guided/advanced logs, saved queries, charts.

#### Track 5 — Incident Timeline

Schedule this after AKS, Pipelines, and Observability baseline parity is credible. It is a dependent integration surface, not an isolated page migration.

- Scope toolbar and evidence views.
- Investigation-seed flows.
- Snapshot export and mapping guidance/proposals.

#### Track 6 — Cutover hardening

- End-to-end validation and regression sweep.
- Architecture/codebase-guide updates.
- Test migration/update.
- Removal of `SwebKit.App` only after all cutover-critical surfaces are validated.

### Cutover gate and parallelization rules

The table below makes the two dimensions explicit: whether work is required before cutover, and whether it can proceed in parallel once prerequisites exist.

| Workstream                                                                        | Must land before cutover | When it can run                                                         |
| --------------------------------------------------------------------------------- | ------------------------ | ----------------------------------------------------------------------- |
| UI foundation, shared shell, dashboard, settings-readiness, and theme application | Yes                      | First; treat as a foundation track                                      |
| AKS completion                                                                    | Yes                      | Continue now in bounded slices alongside the shared shell pass          |
| Redis baseline parity                                                             | Yes                      | After the shared shell pass stabilizes                                  |
| Storage baseline parity                                                           | Yes                      | After the shared shell pass stabilizes; can run alongside Redis         |
| Pipelines/Releases baseline parity                                                | Yes                      | After the shared shell pass stabilizes; can run alongside Observability |
| Observability baseline parity                                                     | Yes                      | After the shared shell pass stabilizes and editor/chart seams are fixed |
| Incident Timeline baseline parity                                                 | Yes                      | After AKS, Pipelines, and Observability are credible enough to feed it  |
| Extra personalization and pixel-match polish                                      | No                       | Round 2 only                                                            |

Parallelizable does not mean optional. Everything in the table marked `Yes` still belongs in the current migration plan.

---

## Phase 2–8 — Domain migration pattern

Each domain follows this repeatable pattern.

### Folder layout

```
src/SwebKit.WinUI/
├── Views/
│   ├── ServiceBus/
│   │   ├── ServiceBusPage.xaml
│   │   ├── ServiceBusPage.xaml.cs       ← wires ViewModel to DataContext, nothing else
│   │   ├── EntityTreeView.xaml
│   │   └── EntityTreeView.xaml.cs
│   ├── Aks/
│   ├── Redis/
│   ├── ...
├── ViewModels/
│   ├── ServiceBus/
│   │   ├── ServiceBusPageViewModel.cs   ← all state, commands, service calls
│   │   └── EntityTreeViewModel.cs
│   ├── Aks/
│   ├── Redis/
│   ├── ...
```

### 1. ViewModel owns all state and commands

Each Blazor component's `@code` block becomes a separate ViewModel class. XAML code-behind is minimal — it resolves the ViewModel from DI and sets `DataContext`, nothing else.

```csharp
// ServiceBusPage.xaml.cs — code-behind
public sealed partial class ServiceBusPage : Page
{
    public ServiceBusPageViewModel ViewModel { get; }

    public ServiceBusPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServiceBusPageViewModel>();
        InitializeComponent();
    }
}
```

```csharp
// ViewModels/ServiceBus/ServiceBusPageViewModel.cs
public partial class ServiceBusPageViewModel : ObservableObject
{
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly TabService _tabs;
    // ... other injected services

    public ServiceBusPageViewModel(IServiceBusClientFactory sbFactory, TabService tabs, ...)
    {
        _sbFactory = sbFactory;
        _tabs = tabs;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private ObservableCollection<NamespaceState> _namespaceStates = [];
    [ObservableProperty] private ObservableCollection<TabState> _openTabs = [];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct) { ... }

    [RelayCommand]
    private async Task AddNamespaceAsync() { ... }
}
```

```xml
<!-- ServiceBusPage.xaml — binds to ViewModel, no logic -->
<Page ...>
    <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
    <Button Command="{x:Bind ViewModel.AddNamespaceCommand}" Content="Add namespace" />
    <controls:DataGrid ItemsSource="{x:Bind ViewModel.NamespaceStates, Mode=OneWay}" />
</Page>
```

**All ViewModels registered as transient** (fresh instance per page navigation):

```csharp
services.AddTransient<ServiceBusPageViewModel>();
services.AddTransient<AksPageViewModel>();
// etc.
```

### Phase 3 — AKS slice 1

The first WinUI AKS slice should replace the placeholder route with a real `Views/Aks/AksPage.xaml` backed by `ViewModels/Aks/AksPageViewModel.cs`.

- Reuse `IAksClientBootstrapper` semantics so the WinUI host follows the same demo/live/configured rules as the MAUI page.
- Persist context and namespace changes back into `AppState.Config.AksConfig`, then reload against the selected context or namespace.
- Start with a pod-focused browse surface: connection summary, context selector, namespace selector, refresh, and a native row grid showing health, status, readiness, restarts, and node.
- Defer logs, port-forward, and shell to later Phase 3 slices so the first page stays bootable and validates the bootstrap seam early.

### 2. XAML replaces Razor markup

| Blazor / Fluent UI   | WinUI 3 native                              |
| -------------------- | ------------------------------------------- |
| `FluentDataGrid`     | `CommunityToolkit.WinUI.Controls.DataGrid`  |
| `FluentSearch`       | `AutoSuggestBox` or `TextBox`               |
| `FluentButton`       | `Button`                                    |
| `FluentProgressRing` | `ProgressRing`                              |
| `FluentBadge`        | `InfoBadge`                                 |
| `FluentSelect`       | `ComboBox`                                  |
| `FluentDialog`       | `ContentDialog`                             |
| `FluentTooltip`      | `ToolTipService.ToolTip`                    |
| `FluentTabs`         | `TabView` or `Pivot`                        |
| `FluentSplitter`     | `GridSplitter` (CommunityToolkit)           |
| Custom CSS layouts   | WinUI `Grid`, `StackPanel`, `RelativePanel` |

### 3. Monaco editor (used in: Observability KQL, AKS YAML, Incident Timeline)

No JS rewrite. Host Monaco in a `WebView2` control:

```xml
<WebView2 x:Name="MonacoEditor" />
```

```csharp
await MonacoEditor.EnsureCoreWebView2Async();
MonacoEditor.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "app.local", "Assets/monaco", CoreWebView2HostResourceAccessKind.Allow);
await MonacoEditor.CoreWebView2.NavigateAsync("https://app.local/editor.html");
```

The existing `wwwroot/js/` Monaco assets are moved to `Assets/monaco/` inside the WinUI project. Same language modes, same YAML syntax highlighting.

### 4. Charts (Phase 7 — Observability)

LiveCharts2 binding pattern for WinUI 3:

```xml
<lvc:CartesianChart Series="{x:Bind ViewModel.LatencySeries}" />
```

```csharp
public ISeries[] LatencySeries { get; } =
[
    new LineSeries<double> { Values = new ObservableCollection<double>() }
];
```

LiveCharts2 covers: line charts (latency/trends), bar charts (request counts), area charts (availability). All charts currently in `ObservabilityPerformance.razor` and related components have LiveCharts2 equivalents.

---

## Phase 9 — Cutover checklist

1. Remove `SwebKit.App` from `SwebKit.slnx`
2. Delete `src/SwebKit.App/` folder (after branch merge)
3. Update `codebase-guide.md`:
   - Entry point: `src/SwebKit.WinUI/App.xaml.cs`
   - Shell: `src/SwebKit.WinUI/MainWindow.xaml`
   - All component paths updated
4. Update `architecture.md` runtime components section: `SwebKit.App` → `SwebKit.WinUI`
5. Decide fate of `tests/SwebKit.App.Tests/` — likely rename to `SwebKit.WinUI.Tests`
6. Delete `tests/SwebKit.E2E.Tests/` if WebView2 CDP approach is discontinued, or adapt
