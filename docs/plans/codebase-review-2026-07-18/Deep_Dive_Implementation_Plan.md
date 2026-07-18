# SwebKit Deep-Dive Implementation Plan

> Concrete, phase-by-phase improvements for `SWojtyla/SwebKit`. Each phase lists the exact files to change, the expected code shape, and acceptance criteria. Phases are ordered to minimize risk: tooling and security first, then reliability, then UI decomposition, then long-term architecture.
>
> **Caveat:** This plan is based on static inspection. Run `dotnet build` and `dotnet test` after each phase.

---

## Phase 0 — Engineering hygiene (foundation for everything)

### 0.1 Add a solution file and central build configuration

**Files to create / modify**
- `SwebKit.sln` (new)
- `.editorconfig` (new, root)
- `Directory.Build.props` (new, root)
- `global.json` (new, root)
- `NuGet.config` (new, root)
- `Directory.Packages.props` (new, root)

**Concrete changes**
1. Create the solution:
   ```bash
   dotnet new sln -n SwebKit
   dotnet sln add src/SwebKit.Core/SwebKit.Core.csproj src/SwebKit.App/SwebKit.App.csproj ...
   ```
2. Add `Directory.Build.props`:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <EnableNETAnalyzers>true</EnableNETAnalyzers>
       <AnalysisLevel>latest-Recommended</AnalysisLevel>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
     </PropertyGroup>
   </Project>
   ```
   Remove per-project `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` duplicates.
3. Add `global.json` pinning the SDK:
   ```json
   {
     "sdk": { "version": "8.0.303", "rollForward": "latestPatch" }
   }
   ```
4. Add `Directory.Packages.props` and convert every `Version="*"` / floating reference to an exact version. Example:
   ```xml
   <PackageVersion Include="Azure.Storage.Blobs" Version="12.21.2" />
   <PackageVersion Include="JsonPath.Net" Version="0.8.5" />
   <PackageVersion Include="Bogus" Version="35.5.8" />
   <PackageVersion Include="Azure.Security.KeyVault.Secrets" Version="4.6.0" />
   ```
5. Change all `<PackageReference Include="X" Version="Y" />` to `<PackageReference Include="X" />`.

**Acceptance criteria**
- `dotnet build SwebKit.sln` succeeds from repo root.
- `dotnet list package --vulnerable` no longer warns on high-severity packages.
- `dotnet format --verify-no-changes` passes (or produces only intentional exceptions).

### 0.2 Introduce architecture rule tests

**Files to create**
- `tests/SwebKit.Architecture.Tests/SwebKit.Architecture.Tests.csproj`
- `tests/SwebKit.Architecture.Tests/ArchitectureRules.cs`

**Concrete changes**
Use `NetArchTest` to enforce:
```csharp
[Fact]
public void App_Components_Should_Not_Depend_On_Implementation_Projects()
{
    var app = Types.InCurrentDomain().That().ResideInNamespace("SwebKit.App");
    var forbidden = Types.InCurrentDomain()
        .That().ResideInNamespace("SwebKit.Kubernetes.AksClient")
        .Or().ResideInNamespace("SwebKit.Redis")
        .Or().ResideInNamespace("SwebKit.Azure.Storage");

    app.Should().NotDependOnAny(forbidden).Check();
}
```

**Acceptance criteria**
- Test project builds.
- Architecture test fails until the direct `@using` leaks in `AksConfigForm.razor`, `RedisKeyDetail.razor`, etc., are removed.

---

## Phase 1 — Security hardening

### 1.1 Replace all `kubectl` / `helm` string-argument invocation with `ArgumentList`

**Files to modify**
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (port-forward, apply, YAML validation)
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.LogsExec.cs` (`OpenShellAsync`, `StreamDeploymentLogsAsync` process?)
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.Helm.cs`

**Concrete changes**
1. Add a helper in `SwebKit.Kubernetes`:
   ```csharp
   internal sealed class KubectlArgumentBuilder
   {
       private readonly List<string> _args = new();

       public KubectlArgumentBuilder WithGlobalFlags(string? kubeconfigPath, string? context)
       {
           if (!string.IsNullOrWhiteSpace(kubeconfigPath))
           {
               _args.Add("--kubeconfig");
               _args.Add(kubeconfigPath);
           }
           if (!string.IsNullOrWhiteSpace(context))
           {
               _args.Add("--context");
               _args.Add(context);
           }
           return this;
       }

       public KubectlArgumentBuilder ExecInteractive(string ns, string pod, string container)
       {
           _args.Add("exec");
           _args.Add("-it");
           _args.Add(pod);
           _args.Add("-n"); _args.Add(ns);
           _args.Add("-c"); _args.Add(container);
           return this;
       }

       public KubectlArgumentBuilder PortForward(string ns, string resourceName, int localPort, int remotePort)
       {
           _args.Add("port-forward");
           _args.Add(resourceName);
           _args.Add($"{localPort}:{remotePort}");
           _args.Add("-n"); _args.Add(ns);
           return this;
       }

       public IReadOnlyList<string> Build() => _args;
   }
   ```
2. Update `RunKubectlProcessAsync` to accept `IReadOnlyList<string>` and use `ArgumentList`:
   ```csharp
   private async Task<(int ExitCode, string Stderr)> RunKubectlProcessAsync(
       IReadOnlyList<string> arguments, CancellationToken ct)
   {
       var psi = new ProcessStartInfo("kubectl")
       {
           UseShellExecute = false,
           RedirectStandardOutput = true,
           RedirectStandardError = true,
           CreateNoWindow = true
       };
       foreach (var arg in arguments) psi.ArgumentList.Add(arg);
       // ... rest unchanged
   }
   ```
3. Update `OpenShellAsync` to use the builder and fix flag ordering:
   ```csharp
   public Task OpenShellAsync(string ns, string podName, string container, CancellationToken ct = default)
   {
       ValidateKubernetesName(ns, nameof(ns));
       ValidateKubernetesName(podName, nameof(podName));
       ValidateKubernetesName(container, nameof(container));

       var args = new KubectlArgumentBuilder()
           .WithGlobalFlags(_kubeconfigPath, _kubeconfigContext)
           .ExecInteractive(ns, podName, container)
           .Add("--") // separates kubectl flags/args from the container command
           .Add("/bin/sh")
           .Build();

       var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
       foreach (var a in args) psi.ArgumentList.Add(a);
       // fallback to cmd.exe if wt.exe throws
   }
   ```
   Note: `wt.exe` does not support `ArgumentList` with `UseShellExecute = true`. If `wt.exe` requires a single argument string, escape each argument with `Regex.Escape` or use `CommandLineToArgvW` style quoting. Safer: launch `cmd.exe` with `/c start wt.exe ...` or use `wt.exe` only with `UseShellExecute = false` and `ArgumentList`. If Windows Terminal cannot be started without shell execute, document it and rely on `cmd.exe` fallback with `ArgumentList`.
4. Add `HelmArgumentBuilder` with `--kube-context` (not `--context`) and `--kube-token` support.

**Acceptance criteria**
- A namespace / pod / container name containing `; && rm` is rejected by `ValidateKubernetesName` before any process is started.
- A kubeconfig path containing spaces is handled correctly (no shell splitting).
- `helm` operations still use `--kube-context` instead of `--context`.
- All `kubectl` invocations in the project use `ArgumentList`.

### 1.2 Centralize Azure credential creation in `KubernetesAksClient`

**Files to modify**
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (lines 244 and 1227)
- `src/SwebKit.Core/Services/AzureCredentialFactory.cs`

**Concrete changes**
1. Extend the factory to accept optional exclusions:
   ```csharp
   public static TokenCredential CreateDefault(DefaultAzureCredentialOptions? options = null)
       => new DefaultAzureCredential(options ?? new DefaultAzureCredentialOptions
          {
              ExcludeEnvironmentCredential = true
          });
   ```
2. In `KubernetesAksClient`, replace both `new DefaultAzureCredential(AzureCredentialOptions)` with:
   ```csharp
   var credential = AzureCredentialFactory.CreateDefault(AzureCredentialOptions);
   ```
3. Remove `AzureCredentialOptions` from `KubernetesAksClient` if no longer needed, or move it to the factory.

**Acceptance criteria**
- `grep -R "new DefaultAzureCredential" src/` returns only `AzureCredentialFactory.cs`.
- Unit test verifies factory excludes `EnvironmentCredential`.

### 1.3 Harden API client request surface

**Files to modify**
- `src/SwebKit.Core/Services/HttpRequestExecutor.cs`
- `src/SwebKit.Core/Services/UrlBuilder.cs`
- `src/SwebKit.App/MauiProgram.cs`

**Concrete changes**
1. Add URL validation in `UrlBuilder.Build`:
   ```csharp
   if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
       || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
   {
       throw new InvalidOperationException($"URL must be an http(s) absolute URL: {baseUrl}");
   }
   ```
2. Add per-collection allow/block lists in `HttpRequestExecutor.ExecuteAsync`:
   ```csharp
   if (!IsHostAllowed(request.Url, collection.AllowedHosts))
   {
       return new HttpRequestResult { ErrorMessage = "Host is not in the collection allow-list." };
   }
   ```
3. Change `ServerCertificateCustomValidationCallback` registration to require an explicit per-environment opt-in:
   ```csharp
   ServerCertificateCustomValidationCallback =
       settings.VerifyApiClientSsl && activeEnvironment?.VerifySsl != false
           ? null
           : (_, _, _, _) => true;
   ```
4. Surface a non-dismissible warning banner in `ApiClientPage.razor` when `VerifyApiClientSsl` is false.

**Acceptance criteria**
- A request to `file://` or `http://localhost` is rejected or allowed only by explicit allow-list.
- When SSL verification is disabled, the UI shows a persistent warning.

### 1.4 Fix OAuth2 local callback port race condition

**Files to modify**
- `src/SwebKit.App/Services/OAuth2TokenManager.cs`

**Concrete changes**
Replace the `TcpListener` port probe with direct binding:
```csharp
private static HttpListener StartLocalCallbackListener(out string redirectUri)
{
    var listener = new HttpListener();
    var port = 0;
    // Bind to a random port by passing 0 is not supported by HttpListener Prefixes.
    // Instead, try a small range of ports and throw if none are free.
    for (var p = 49152; p < 65535; p++)
    {
        try
        {
            redirectUri = $"http://127.0.0.1:{p}/oauth/callback/";
            listener.Prefixes.Clear();
            listener.Prefixes.Add(redirectUri);
            listener.Start();
            return listener;
        }
        catch (HttpListenerException) { }
    }
    throw new InvalidOperationException("No free local port for OAuth callback.");
}
```
Also validate `auth.OAuth2TokenUrl` and `auth.OAuth2AuthUrl` start with `https://`, with a localhost http opt-in flag.

**Acceptance criteria**
- `AuthorizeWindowsAsync` no longer releases the socket between port discovery and listener start.
- HTTP token/auth URLs are rejected unless the environment explicitly allows local development.

---

## Phase 2 — Reliability & exception hygiene

### 2.1 Stop silently swallowing critical exceptions

**Files to modify**
- `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`
- `src/SwebKit.App/Services/ConnectionWarmupService.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (`TryApplyAzureCredentialFallback`)
- `src/SwebKit.Core/Configuration/AppDataFileStore.cs`
- `src/SwebKit.Core/Services/HttpRequestExecutor.cs` (`BuildResultAsync`)

**Concrete changes**
Use the pattern:
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex, "Optional operation failed: {Operation}", operationName);
    // return fallback if appropriate
}
```
For `WindowsCredentialStore`:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Credential store operation failed for key {Key}", key);
    return null;
}
```
For `ConnectionWarmupService`:
```csharp
catch (OperationCanceledException) { /* expected */ }
catch (Exception ex)
{
    _logger.LogWarning(ex, "Warmup failed for {Area}", areaName);
}
```

**Acceptance criteria**
- `grep -R "catch\s*\(.*Exception.*\)\s*\{\s*\}" src/` returns zero matches.
- All broad catch blocks either log or rethrow.
- `dotnet test` still passes (some tests may need updating if they relied on silent failures).

### 2.2 Ensure graceful shutdown of port-forward processes

**Files to modify**
- `src/SwebKit.App/App.xaml.cs`

**Concrete changes**
```csharp
private static void OnProcessExit(object? sender, EventArgs e)
{
    var sessions = IPlatformApplication.Current?.Services.GetService<IPortForwardSessionService>();
    if (sessions is not null)
    {
        try
        {
            sessions.StopAllAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Port-forward cleanup failed: {ex.Message}");
        }
    }
}
```

**Acceptance criteria**
- After closing the app, `kubectl port-forward` child processes are terminated within 5 seconds in normal scenarios.

### 2.3 Add cancellation-link lifetime to streaming components

**Files to audit**
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor` (auto-refresh)

**Concrete changes**
Ensure each component owns one `CancellationTokenSource` and cancels it in `Dispose`/`DisposeAsync`:
```csharp
private readonly CancellationTokenSource _cts = new();
public void Dispose() => _cts.Cancel();
```
For `StreamPodLogsAsync`, do not call `StateHasChanged` per line; buffer lines and flush on a timer or after N lines.

**Acceptance criteria**
- Navigating away from a pod log page cancels the stream and no `ObjectDisposedException` appears in logs.

---

## Phase 3 — UI decomposition (biggest maintainability win)

### 3.1 Apply the API Client extraction pattern to `AksPage`

`AksPage.razor` is 2,957 LOC. The `api-client-page-decomposition` feature already defines a successful slicing pattern. Apply the same pattern to AKS.

**Files to create**
- `src/SwebKit.App/Components/Pages/AksPage.Bootstrap.cs`
- `src/SwebKit.App/Components/Pages/AksPage.Resources.cs`
- `src/SwebKit.App/Components/Pages/AksPage.DetailPanels.cs`
- `src/SwebKit.App/Components/Pages/AksPage.PortForward.cs`
- `src/SwebKit.App/Components/Pages/AksPage.ContextMenu.cs`
- `src/SwebKit.App/Components/Pages/AksPage.Shortcuts.cs`

**Concrete slicing**
- **Bootstrap** — `BootstrapAndLoadAsync`, `CanReuseWarmBootstrapResult`, `SyncFromEnvironment`, `NormalizeDefaultNamespace`, bootstrap `CancellationTokenSource` handling.
- **Resources** — `LoadAsync`, `Load*Async` methods, filters (`ActiveFilter`, `FilteredDeployments`, etc.), `ActiveResourceType` switching.
- **Detail panels** — `OpenYamlAsync`, `OpenLogsAsync`, `OpenShellAsync`, `OpenPortForwardDialogAsync`, `ScaleDeploymentAsync`, `RestartDeploymentAsync`, `Delete*Async` and all `_pending*` dialog fields.
- **Port-forward** — `StartPortForwardAsync`, `StopPortForwardAsync`, `PinnedPortForwards` interactions.
- **Context menu** — `OnTableContextMenu`, `ShowContextMenuFor*`, `OnCtx*` methods.
- **Shortcuts** — `HandleGridKeyDown`, `JumpToLogsAsync`, command registrations.

What stays in `AksPage.razor`:
- Markup and `@inject`.
- The page state object `AksPageState` (create a new `AksPageState.cs` record/class).
- `OnParametersSet` orchestration only.

**Concrete first slice: `AksPage.Bootstrap.cs`**
```csharp
namespace SwebKit.App.Components.Pages;

public partial class AksPage
{
    private CancellationTokenSource _bootstrapCts = new();
    private AksBootstrapSignature? _lastBootstrapSignature;

    protected override void OnParametersSet()
    {
        var config = AppState.Config.AksConfig;
        var signature = new AksBootstrapSignature(
            ClientOverride,
            AppState.UseDemoData,
            config?.KubeconfigPath,
            config?.KubeconfigContext,
            NormalizeDefaultNamespace(config));

        if (_lastBootstrapSignature == signature) { _ = Workspaces.ApplyPendingRestoreAsync("aks"); return; }

        _lastBootstrapSignature = signature;
        SyncFromEnvironment();
        IsLoading = true;
        _ = BootstrapAndLoadAsync(ActiveContext, CurrentNamespace);
    }

    private async Task BootstrapAndLoadAsync(string requestedContext, string requestedNamespace)
    {
        // existing logic, but contained in this file
    }
}
```

**Acceptance criteria**
- `AksPage.razor` drops below 800 LOC.
- `dotnet build` and bUnit tests still pass.
- No behavioral change (verify manually: open AKS page, switch context, open logs, scale deployment).

### 3.2 Move per-resource filter logic into a reusable `AksResourceFilterService`

**Files to create / modify**
- `src/SwebKit.App/Services/AksResourceFilterService.cs` (new)
- `src/SwebKit.App/Components/Pages/AksPage.Resources.cs`

**Concrete changes**
Replace the twelve separate filter fields and computed `IQueryable<T>` properties with a generic filter state:
```csharp
public sealed class AksResourceFilterService
{
    public string GetFilter(string resourceType);
    public void SetFilter(string resourceType, string value);
    public IReadOnlyList<T> ApplyFilter<T>(IReadOnlyList<T> source, string resourceType, Func<T, string> nameSelector) where T : class;
}
```
This removes the `DeploymentFilter`, `PodFilter`, etc., duplication and makes filters unit-testable.

**Acceptance criteria**
- Filter behavior unchanged.
- New unit tests cover filtering and caching.

### 3.3 Decompose `DashboardPage.razor`

**Files to create**
- `src/SwebKit.App/Components/Pages/DashboardPage.ViewState.cs`
- `src/SwebKit.App/Components/Pages/DashboardPage.Tiles.cs`
- `src/SwebKit.App/Components/Pages/DashboardPage.Filters.cs`
- `src/SwebKit.App/Components/Pages/DashboardPage.Helpers.cs`

**Concrete changes**
Move:
- `GetRenderState()`, `GetAttentionCount()`, `Get*Label()` helpers into `DashboardPage.ViewState.cs`.
- Tile rendering / refresh logic into `DashboardPage.Tiles.cs`.
- View, area, severity, time-window filter handlers into `DashboardPage.Filters.cs`.
- Static helpers (`NormalizeCssToken`, `GetAreaIcon`, `RelativeTime`) into `DashboardPage.Helpers.cs` or a shared `DashboardFormatting` static class.

**Acceptance criteria**
- `DashboardPage.razor` below 800 LOC.
- Dashboard renders the same tiles and filters.

### 3.4 Continue and extend the API Client extraction plan

The active plan is in `docs/features/active/api-client-page-decomposition/extraction-plan.md`. Do not replace it; continue it.

**Next concrete step**: finish Slice 2 (Secrets) and Slice 3 (Tabs), then move to extracting the request lifecycle into a real service.

**Files to create / modify**
- `src/SwebKit.App/Components/Pages/ApiClientPage.Secrets.cs`
- `src/SwebKit.App/Components/Pages/ApiClientPage.Tabs.cs`
- `src/SwebKit.Core/Services/ApiClientRequestLifecycleService.cs` (new)

**Concrete changes for request lifecycle service**
Move the following out of `ApiClientPage` into a service:
- `OnRequestSelectedAsync`
- `OnRequestChangedAsync`
- `AutoSaveLoopAsync`
- `OnRequestResultAsync`
- `OnSubscriptionMessageAsync` / `OnSubscriptionStoppedAsync`
- `SaveResponseExampleAsync`

The service takes `ApiClientState` and the required repositories/executors as parameters:
```csharp
public sealed class ApiClientRequestLifecycleService(
    IHttpRequestExecutor executor,
    IVariableSubstitutionService substitution,
    IWebSocketClientService webSocketClient)
{
    public async Task ExecuteRequestAsync(ApiClientState state, CancellationToken ct);
    public async Task AutoSaveAsync(ApiClientState state, CancellationToken ct);
    public void CancelActiveRequest(ApiClientState state);
}
```

**Acceptance criteria**
- `ApiClientPage.razor` below 1,000 LOC.
- The existing extraction-plan status file is updated.

---

## Phase 4 — Architecture cleanup

### 4.1 Split `MauiProgram` registration into feature modules

**Files to create**
- `src/SwebKit.App/Hosting/SwebKitServiceCollectionExtensions.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Core.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Azure.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Kubernetes.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.DevOps.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Observability.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Agents.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.Redis.cs`
- `src/SwebKit.App/Hosting/ServiceRegistration.App.cs`

**Concrete changes**
`MauiProgram.CreateMauiApp` becomes:
```csharp
builder.Services
    .AddSwebKitCore()
    .AddSwebKitAzure()
    .AddSwebKitKubernetes()
    .AddSwebKitDevOps()
    .AddSwebKitObservability()
    .AddSwebKitRedis()
    .AddSwebKitAgents()
    .AddSwebKitAppServices();
```
Each extension lives in its own partial class file under `Hosting/`.

**Acceptance criteria**
- `MauiProgram.cs` below 80 lines.
- `dotnet build` succeeds and the app still starts.

### 4.2 Remove direct implementation namespace imports from `.razor` files

**Files to modify**
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor` — replace `@using SwebKit.Kubernetes.AksClient` with `SwebKit.Core.Models` / `SwebKit.Core.Abstractions` if needed.
- `src/SwebKit.App/Components/Storage/BlobDetailPane.razor` — remove `@using SwebKit.Azure.Storage`.
- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor`, `RedisNamespaceTreeNode.razor`, `RedisKeyList.razor` — remove `@using SwebKit.Redis`.

**Concrete changes**
If these imports are not actually used by the markup, delete them. If extension methods are needed, move them to `SwebKit.Core` extension classes.

**Acceptance criteria**
- Architecture test from Phase 0 passes.

### 4.3 Move demo clients out of `SwebKit.Core`

**Files to move / modify**
- Move `src/SwebKit.Core/Services/Demo*.cs` into `src/SwebKit.<Integration>/Demo/`.
- Update `MauiProgram` (or new `Hosting` modules) registrations.
- Update any remaining pages that inject concrete demo types to use factory interfaces.

**Concrete changes**
Create `IDemoDataProvider` per integration:
```csharp
public interface IDemoAksDataProvider : IAksClient { }
```
In `SwebKit.Kubernetes/Demo/DemoAksClient.cs`:
```csharp
public sealed class DemoAksClient : IAksClient { ... }
```
Then `SwebKit.Core` has no concrete demo code.

**Acceptance criteria**
- `SwebKit.Core` no longer contains `DemoAksClient`, `DemoRedisClient`, `DemoDevOpsClient`, `DemoServiceBusClient`, `DemoStorageClient`, or `DemoObservabilityProvider`.
- Demo mode still works end-to-end.

### 4.4 Remove `SwebKit.Agent.PocConsole` or archive it

**Files to modify / delete**
- `src/SwebKit.Agent.PocConsole/` — delete or move to `samples/`.

**Concrete changes**
If it is no longer used, delete the project and remove it from any build scripts. If kept as a sample, ensure it does not reference `SwebKit.Kubernetes.AksClient` directly and has its own `Program.cs` DI setup.

**Acceptance criteria**
- No `SwebKit.Agent.PocConsole` project in `src/` unless intentionally kept as a sample.

---

## Phase 5 — Performance & rendering

### 5.1 Adopt `SwebKitComponentBase` everywhere and replace direct `StateHasChanged`

**Files to modify**
- All `.razor` files under `src/SwebKit.App/Components/Pages/`, `Components/ServiceBus/`, `Components/Aks/`, `Components/Redis/`, `Components/Storage/`, `Components/Observability/`, `Components/Pipelines/`, `Components/Releases/`, `Components/Monitoring/`.

**Concrete changes**
1. In each `.razor` file, add at the top:
   ```razor
   @inherits SwebKit.App.Components.Shared.SwebKitComponentBase
   ```
2. Replace direct `StateHasChanged()` and `InvokeAsync(StateHasChanged)` calls with `RequestRender()` or `RequestCoalescedRender()`.
3. Remove redundant manual `InvokeAsync(StateHasChanged)` calls inside event callbacks when the base class already handles them.

Do this incrementally per feature area to keep PRs reviewable.

**Acceptance criteria**
- `grep -R "StateHasChanged()" src/SwebKit.App/Components` shows only necessary calls in non-inheriting components.
- UI responsiveness is equivalent or better on the AKS and Service Bus pages.

### 5.2 Coalesce high-frequency `StateHasChanged` calls in log streaming and metrics

**Files to modify**
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`

**Concrete changes**
Use a `Channel<string>` / `List<string>` buffer + `PeriodicTimer`:
```csharp
private readonly List<string> _logBuffer = new();
private readonly PeriodicTimer _flushTimer = new(TimeSpan.FromMilliseconds(150));

private async Task AppendLineAsync(string line)
{
    lock (_logBuffer) { _logBuffer.Add(line); }
    if (_logBuffer.Count > 50) await FlushAsync();
}

private async Task FlushAsync()
{
    List<string> batch;
    lock (_logBuffer)
    {
        batch = _logBuffer.ToList();
        _logBuffer.Clear();
    }
    _lines.AddRange(batch);
    await TruncateToMaxLinesAsync();
    RequestCoalescedRender();
}
```

**Acceptance criteria**
- Long log streams do not freeze the WebView.
- Memory usage stays bounded (cap at e.g., 10,000 lines).

### 5.3 Make startup tracing async

**Files to modify**
- `src/SwebKit.App/Services/PerformanceBaselineRecorder.cs`

**Concrete changes**
Convert to a lightweight async channel:
```csharp
internal static class PerformanceBaselineRecorder
{
    private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    static PerformanceBaselineRecorder()
    {
        _ = Task.Run(async () =>
        {
            await foreach (var line in _channel.Reader.ReadAllAsync())
            {
                try { await File.AppendAllTextAsync(AppDataPaths.PerformanceBaselineLog, line); }
                catch { }
            }
        });
    }

    public static void Record(string category, string message)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message)) return;
        var line = $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}";
        _channel.Writer.TryWrite(line);
    }
}
```

**Acceptance criteria**
- `Record` no longer blocks the calling thread.
- Startup trace file still contains entries.

---

## Phase 6 — Runtime & dependency stabilization

### 6.1 Migrate from .NET 10 preview to .NET 8 LTS

**Files to modify**
- All `.csproj` files
- `Directory.Build.props`
- `global.json`
- `MauiVersion` in `SwebKit.App.csproj`

**Concrete changes**
1. Set `TargetFramework` to `net8.0` (or `net8.0-windows10.0.19041.0` for the MAUI app).
2. Pin `MauiVersion` to `8.0.70` or latest stable 8.x.
3. Update package references:
   - `Microsoft.Extensions.*` from `10.0.x`/`10.6.x` to `8.0.x`.
   - `Microsoft.NET.Test.Sdk` from `18.6.0` to `17.x`.
   - `xUnit` unify all test projects to `2.9.x`.
4. Check `Microsoft.FluentUI.AspNetCore.Components` 4.14.2 for .NET 8 compatibility; if not, use 4.10.x for .NET 8.

**Acceptance criteria**
- `dotnet --version` reports 8.0.x.
- `dotnet build` and `dotnet test` pass.
- App launches on Windows.

### 6.2 Centralize and pin all package versions

**Files to modify**
- `Directory.Packages.props`
- All `*.csproj` `PackageReference` entries

**Concrete changes**
Remove floating `*` versions and exact duplicates. Add a CI check:
```yaml
- name: Check for floating versions
  run: |
    if grep -R 'Version="\*' src tests; then exit 1; fi
```

**Acceptance criteria**
- No `Version="*"` or `Version="0.*"` in repo.
- `dotnet list package --vulnerable` is clean of high/critical issues.

---

## Phase 7 — Testing & CI

### 7.1 Add a CI workflow

**Files to create**
- `.github/workflows/build.yml`
- `.github/workflows/sonar-or-codeql.yml` (optional)

**Concrete `build.yml`**
```yaml
name: Build & Test
on: [push, pull_request]
jobs:
  core:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore SwebKit.sln
      - run: dotnet build SwebKit.sln --no-restore
      - run: dotnet test SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --no-build
      - run: dotnet test SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj --no-build
      - run: dotnet test SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj --no-build
      - run: dotnet test SwebKit.DevOps.Tests/SwebKit.DevOps.Tests.csproj --no-build
      - run: dotnet list package --vulnerable --include-transitive
  maui:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet workload install maui
      - run: dotnet build src/SwebKit.App/SwebKit.App.csproj -f net8.0-windows10.0.19041.0
```

**Acceptance criteria**
- CI passes on `main`.

### 7.2 Add command-line construction tests

**Files to create**
- `tests/SwebKit.Kubernetes.Tests/KubectlArgumentBuilderTests.cs`

**Concrete test cases**
```csharp
[Theory]
[InlineData("default", "my-pod", "container", new[] { "exec", "-it", "my-pod", "-n", "default", "-c", "container", "--", "/bin/sh" })]
public void Builds_Interactive_Exec_Arguments(string ns, string pod, string container, string[] expected)
{
    var actual = new KubectlArgumentBuilder()
        .ExecInteractive(ns, pod, container)
        .Add("--")
        .Add("/bin/sh")
        .Build();
    Assert.Equal(expected, actual);
}

[Fact]
public void Rejects_Shell_Metacharacter_In_PodName()
{
    Assert.Throws<ArgumentException>(() => ValidateKubernetesName("my-pod; rm -rf /", "podName"));
}
```

**Acceptance criteria**
- 100% branch coverage on `KubectlArgumentBuilder` and `ValidateKubernetesName`.

### 7.3 Add rendering/performance tests

**Files to create**
- `tests/SwebKit.App.Tests/AksPageRenderTests.cs`
- `tests/SwebKit.App.Tests/DashboardRenderTests.cs`

**Concrete test**
```csharp
[Fact]
public void AksPage_Renders_Without_Calling_StateHasChanged_In_Loop()
{
    // Arrange: use bUnit to render AksPage with a fake IAksClient
    // Act: trigger LoadAsync
    // Assert: verify no exceptions and grid renders
}
```

**Acceptance criteria**
- Existing `bUnit` tests still pass.
- New tests cover the refactored `AksPage` slices.

---

## 8. Recommended order of attack

| Order | Phase | Why first |
|-------|-------|-----------|
| 1 | **0.1** (solution, build props, global.json, CPM) | Everything else depends on a reproducible build. |
| 2 | **6.1** (downgrade to .NET 8) | Stabilizes the foundation before deeper refactors. |
| 3 | **1.1** and **1.2** (kubectl security + Azure credentials) | High-impact security fixes; small surface area. |
| 4 | **2.x** (exception hygiene, port-forward cleanup) | Improves reliability with small, safe changes. |
| 5 | **3.1** and **3.3** (AksPage / DashboardPage slicing) | Largest maintainability win; do incrementally. |
| 6 | **4.x** (MauiProgram modularization, demo client move) | Makes the codebase scalable. |
| 7 | **5.x** (render coalescing, async startup tracing) | Performance after structure is cleaner. |
| 8 | **7.x** (CI, architecture tests, new unit tests) | Locks in quality. |

---

*Generated by Devin on 2026-07-18.*
