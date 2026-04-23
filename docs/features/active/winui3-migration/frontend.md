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
