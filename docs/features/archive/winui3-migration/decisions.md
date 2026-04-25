# Decisions - winui3-migration

Library choices and rationale for the WinUI 3 host.

---

## D-1: UI Controls — Native WinUI 3 + CommunityToolkit.WinUI

**Decision:** Use native WinUI 3 controls as the primary set. Supplement with `CommunityToolkit.WinUI` for controls not in the SDK.

**Rationale:**

- WinUI 3 already implements Fluent Design 2 natively — no third-party design system needed.
- The toolkit is first-party Microsoft (same team), MIT-licensed, ships on NuGet, tested against the same Windows App SDK versions we target.
- Controls needed from the toolkit: `DataGrid`, `GridSplitter`, `Segmented`, `TitleBar`.

**Packages:**

```
Microsoft.WindowsAppSDK                       (WinUI 3 SDK)
Microsoft.Extensions.Hosting                  (generic host / DI — not bundled by Windows App SDK)
CommunityToolkit.WinUI.Controls.Segmented     (8.2.251219)
CommunityToolkit.WinUI.Controls.Sizers        (GridSplitter replacement — 8.2.251219)
```

**Note — DataGrid:** `CommunityToolkit.WinUI.UI.Controls.DataGrid` (v7) targets UWP, not WinUI 3. There is no v8 WinUI 3 port of DataGrid in the toolkit as of April 2026. The WinUI 3 / Windows App SDK does not ship a built-in DataGrid control either. The baseline checkpoint recorded these later follow-up options:

- Custom `ListView`/`ItemsView` with column headers (sufficient for most grids in this app)
- Telerik or Syncfusion DataGrid (commercial)
- Contribute to / wait for the CommunityToolkit v8 DataGrid — tracked at [github.com/CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows)

**Rejected alternatives:**

- Telerik UI for WinUI — commercial license, unnecessary cost for a personal tool.
- Syncfusion WinUI — commercial.
- DevExpress WinUI — commercial.

---

## D-2: MVVM — CommunityToolkit.Mvvm (separate ViewModel classes)

**Decision:** Classic MVVM with separate `XxxViewModel` classes. Each page and meaningful sub-component gets its own ViewModel file. Code-behind (`.xaml.cs`) is kept minimal — its only job is resolving the ViewModel from DI and setting `DataContext`.

**Rationale:**

- Cleanest separation: XAML owns layout, ViewModel owns all state and logic.
- Easiest to read and navigate — each concern lives in a predictable file.
- Also first-party Microsoft, MIT-licensed.
- Source generators (`[ObservableProperty]`, `[RelayCommand]`) eliminate all INPC boilerplate.
- `ObservableObject`, `ObservableRecipient`, `IMessenger` cover all patterns needed.
- Works natively with WinUI 3 `x:Bind` (compile-time binding, no reflection).

**Package:**

```
CommunityToolkit.Mvvm
```

**File convention per feature domain:**

```
Views/ServiceBus/
    ServiceBusPage.xaml          ← layout only, x:Bind to ViewModel
    ServiceBusPage.xaml.cs       ← DataContext = DI.GetRequiredService<ServiceBusPageViewModel>()
ViewModels/ServiceBus/
    ServiceBusPageViewModel.cs   ← all state, commands, service calls
```

**Code-behind wiring pattern:**

```csharp
// ServiceBusPage.xaml.cs
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

```xml
<!-- ServiceBusPage.xaml -->
<Page x:Class="SwebKit.WinUI.Views.ServiceBus.ServiceBusPage"
      xmlns:vm="using:SwebKit.WinUI.ViewModels.ServiceBus">
    <Page.DataContext>
        <vm:ServiceBusPageViewModel />
    </Page.DataContext>
    ...
    <Button Command="{x:Bind ViewModel.RefreshCommand}" />
    <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
</Page>
```

**ViewModel registration in DI:**

```csharp
// All ViewModels registered as transient — a fresh instance per page navigation
services.AddTransient<ServiceBusPageViewModel>();
services.AddTransient<AksPageViewModel>();
// etc.
```

**`IAppEventBus` → `IMessenger` migration:**

The existing `IAppEventBus` / `AppEventBus` pattern (publish/subscribe for cross-component events) maps directly to `IMessenger` / `WeakReferenceMessenger`:

```csharp
// Before (Blazor)
_events.Subscribe<ConnectionChangedEvent>(OnConnectionChanged);
_events.Publish(new ConnectionChangedEvent(...));

// After (ViewModel)
// In constructor:
Messenger.Register<ConnectionChangedMessage>(this, (r, m) => OnConnectionChanged(m));
// To publish:
Messenger.Send(new ConnectionChangedMessage(...));
```

`IAppEventBus` and `AppEventBus` can be deleted once all Blazor consumers are migrated.

---

## D-3: Charts — LiveCharts2

**Decision:** Replace `Blazor-ApexCharts` with `LiveChartsCore.SkiaSharpView.WinUI`.

**Rationale:**

- `Blazor-ApexCharts` wraps ApexCharts.js via JS interop — it cannot run outside a Blazor/WebView2 context. No viable migration path.
- LiveCharts2 is MIT-licensed, actively maintained, has a dedicated WinUI 3 package with SkiaSharp rendering (GPU-accelerated, no WebView2 dependency).
- Supports all chart types currently used: line series (latency trends), bar/column (request counts), area (availability), scatter (outlier detection).
- `x:Bind`-friendly: series and axes are plain .NET collections.

**Package:**

```
LiveChartsCore.SkiaSharpView.WinUI
```

**Rejected alternatives:**

- OxyPlot.WinUI — limited chart types, older visual style, not actively maintained for WinUI 3.
- WinUI Community Toolkit Charts — too basic (bar/line only, no real-time support).
- Telerik/Syncfusion charts — commercial.

**Recorded migration effort:** Medium. Each chart in `Observability/` needed conversion. The data model shapes (series, data points) were already compatible with LiveCharts2 `ISeries<T>` bindings, while axis formatting and tooltip customization still required follow-up work.

---

## D-4: Code Editor (Monaco) — WebView2 direct hosting

**Decision:** Retain Monaco editor. Host it in a native WinUI 3 `WebView2` control using virtual host mapping.

**Rationale:**

- Monaco is the best-in-class browser-based code editor. No native WinUI equivalent reaches the same level of syntax highlighting, IntelliSense-style completion, and theme support.
- WinUI 3 has `Microsoft.Web.WebView2` as a first-class control — the same engine that MAUI uses.
- The existing JS assets (`wwwroot/js/`) contain YAML syntax highlighting configuration. Moving them to `Assets/monaco/` in the WinUI project is a file copy, not a rewrite.
- Virtual host mapping (`CoreWebView2.SetVirtualHostNameToFolderMapping`) lets Monaco load without a localhost server.

**Package:**

```
Microsoft.Web.WebView2   (ships with Windows App SDK; no separate package needed for WinUI 3)
```

**Rejected alternatives:**

- AvalonEdit WinUI port — community-maintained, incomplete syntax support for KQL/YAML.
- Scintilla.NET — requires P/Invoke, poor WinUI 3 integration.

**Recorded migration effort:** Low. The baseline approach was to copy the JS assets, add an `editor.html` wrapper page, and host Monaco through `WebView2` while the view model posted content changes via `ExecuteScriptAsync("setEditorContent(...)"); `.

---

## D-5: DI Host — Microsoft.Extensions.Hosting

**Decision:** Replace `MauiApp.CreateBuilder()` with `Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()`.

**Rationale:**

- Standard .NET generic host. Same `IServiceCollection`, same `ILogger`, same `IConfiguration` API.
- All service registrations from `MauiProgram.cs` copy verbatim; only the host-builder call changes.
- Removes the only remaining mandatory MAUI dependency in the app entry point.

**Package:** Included in .NET 10 SDK — no NuGet package needed.

**Pattern:** Host is created in `App.xaml.cs` constructor (before `InitializeComponent`). `MainWindow` is resolved from the DI container in `OnLaunched`. This is the standard WinUI 3 + DI pattern.

---

## D-6: HTTP Resilience — Keep as-is

**Decision:** `Microsoft.Extensions.Http.Resilience` stays unchanged.

**Rationale:** Pure .NET, no MAUI or Blazor dependency. The `DevOpsAuthHandler` pipeline registration is identical in a generic host.

---

## D-7: YAML Parsing — Keep YamlDotNet

**Decision:** `YamlDotNet` stays unchanged.

**Rationale:** No UI dependency.

---

## D-8: Windows Credential Store — Keep WindowsCredentialStore

**Decision:** `WindowsCredentialStore.cs` is copied to `SwebKit.WinUI/Platforms/Windows/`, no changes.

**Rationale:** Already uses raw `CredentialManager` Win32 API, no MAUI dependency.

---

## D-9: Windows Toast + Tray — Keep with one patch

**Decision:** `WindowsToastNotificationService.cs` and `WindowsTrayLifecycleService.cs` are copied to `SwebKit.WinUI/Platforms/Windows/`.

**One change required:** In `WindowsTrayLifecycleService.cs` line 288:

```csharp
// Remove:
using Microsoft.Maui.ApplicationModel;
var app = Microsoft.Maui.Controls.Application.Current;

// Replace with:
var app = Microsoft.UI.Xaml.Application.Current;
```

Both `using` and the call site change. No other MAUI references in these files.

---

## D-10: UI architecture foundation before page proliferation

**Decision:** Add an explicit shared UI layer in `SwebKit.WinUI` before the remaining workspaces were migrated broadly.

**Rationale:**

- The current WinUI host proves MVVM and navigation, but `App.xaml` still only merges the default WinUI resources and current pages compose most cards/layout inline.
- If Redis, Storage, Pipelines, and Observability are added on top of that baseline, the app will accumulate one-off XAML structures that are expensive to unify later.
- The MAUI app already behaves like one product; the WinUI host needs the same shared shell and page language from the start.

**Historical implication:**

- The baseline work added app-level resource dictionaries, shell primitives, and shared page/workspace scaffolds before further page breadth.
- `Settings`, `ServiceBus`, and `AKS` served as the proving grounds for those shared primitives.

---

## D-11: Theming via semantic tokens and curated dictionaries

**Decision:** Theme the WinUI host through semantic SwebKit resource tokens plus curated theme dictionaries, not through page-local brush choices or a permanent `system/dark/light` abstraction.

**Rationale:**

- The existing MAUI shell already defines a curated theme identity that is richer than a generic dark/light toggle.
- WinUI's built-in theme resources are a strong base, but they are not by themselves a product design system.
- Semantic tokens let curated themes change shell mood and brand cues while keeping page composition and interaction patterns stable.

**Historical implication:**

- Theme selection persisted a theme key that mapped to curated theme dictionaries.
- Themes were applied centrally through a shell-level coordinator.
- Pages and shared controls preferred semantic resource names over direct brush selection.

---

## Summary table

| Concern          | Current (MAUI Blazor)                     | WinUI 3 replacement                                          | Effort                |
| ---------------- | ----------------------------------------- | ------------------------------------------------------------ | --------------------- |
| UI controls      | FluentUI.AspNetCore.Components            | Native WinUI 3 + CommunityToolkit.WinUI                      | High (150 components) |
| MVVM             | Blazor @code blocks                       | CommunityToolkit.Mvvm                                        | High (new pattern)    |
| Charts           | Blazor-ApexCharts (JS)                    | LiveChartsCore.SkiaSharpView.WinUI                           | Medium                |
| Code editor      | BlazorMonaco (JS interop)                 | WebView2 direct (same Monaco JS)                             | Low                   |
| DI host          | MauiApp.CreateBuilder                     | Microsoft.Extensions.Hosting                                 | Low                   |
| HTTP resilience  | Unchanged                                 | Unchanged                                                    | None                  |
| YAML parsing     | Unchanged                                 | Unchanged                                                    | None                  |
| Credential store | WindowsCredentialStore                    | WindowsCredentialStore (copy)                                | None                  |
| Toast / Tray     | Windows APIs                              | Windows APIs (1-line patch)                                  | Trivial               |
| UI architecture  | CSS-driven shell + shared Blazor patterns | WinUI resource dictionaries + reusable shell/page primitives | Medium                |
| Theme system     | Curated MAUI theme keys                   | Curated WinUI theme dictionaries + semantic tokens           | Medium                |
