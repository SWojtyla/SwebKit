# SwebKit Deep-Dive Implementation Plan

> Concrete, phase-by-phase improvements for `SWojtyla/SwebKit`. Each phase lists the exact files to change, the expected code shape, and acceptance criteria. Phases are ordered to minimize risk: tooling and security first, then reliability, then UI decomposition, then long-term architecture.
>
> **Revised**: original plan by Devin corrected for inaccuracies and updated to reflect work already merged in PR #27. .NET 10 is the target — no downgrade.
> **Caveat:** Run `dotnet build` and `dotnet test` after each phase.

---

## Phase 0 — Engineering hygiene (foundation for everything)

### 0.1 Add central build configuration

**Files to create / modify**
- `.editorconfig` (new, root)
- `Directory.Build.props` (new, root)
- `global.json` (new, root)
- `NuGet.config` (new, root)
- `Directory.Packages.props` (new, root)

**Note**: `SwebKit.slnx` already exists — no solution file creation needed.

**Concrete changes**
1. Add `Directory.Build.props` (do **not** set TargetFramework here — the MAUI app needs a platform-specific TFM):
   ```xml
   <Project>
     <PropertyGroup>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <EnableNETAnalyzers>true</EnableNETAnalyzers>
       <AnalysisLevel>latest-Recommended</AnalysisLevel>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
     </PropertyGroup>
   </Project>
   ```
   Remove per-project `<Nullable>`, `<ImplicitUsings>` duplicates where they match the defaults. Keep per-project `<TargetFramework>` since the MAUI app uses `net10.0-windows10.0.19041.0`.
2. Add `global.json` pinning the .NET 10 SDK:
   ```json
   {
     "sdk": { "version": "10.0.100-preview.3.25201.16", "rollForward": "latestPatch" }
   }
   ```
   (Use the exact SDK version installed on the dev machine. Run `dotnet --version` to find it.)
3. Add `Directory.Packages.props` and convert every `Version="*"` / floating reference to an exact version. Example:
   ```xml
   <PackageVersion Include="Azure.Storage.Blobs" Version="12.21.2" />
   <PackageVersion Include="JsonPath.Net" Version="0.8.5" />
   <PackageVersion Include="Bogus" Version="35.5.8" />
   <PackageVersion Include="Azure.Security.KeyVault.Secrets" Version="4.6.0" />
   ```
4. Change all `<PackageReference Include="X" Version="Y" />` to `<PackageReference Include="X" />`.

**Acceptance criteria**
- `dotnet build SwebKit.slnx` succeeds from repo root.
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
- Architecture test passes (the `@using` leaks were already fixed in PR #27; the test prevents regression).

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
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (lines 255 and 1238)
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

### 3.1 Decompose `AksPage.razor` using child components + state service

`AksPage.razor` is 2,653 LOC. Unlike `DashboardPage` and `ApiClientPage` (which were decomposed via partial classes in PR #27), `AksPage` should use **child components with `[Parameter]` binding** and a **page state service** to actually reduce coupling and improve testability.

**Files to create**
- `src/SwebKit.App/Components/Pages/AksPageState.cs` (new — page state record)
- `src/SwebKit.App/Services/AksPageOrchestrator.cs` (new — owns loading, selection, command logic)
- `src/SwebKit.App/Components/Aks/AksToolbar.razor` (new — filter bar, resource type selector)
- `src/SwebKit.App/Components/Aks/AksResourceGrid.razor` (new — grid with `[Parameter] Data`)
- `src/SwebKit.App/Components/Aks/AksDetailHost.razor` (new — detail panel host)
- `src/SwebKit.App/Components/Aks/AksPortForwardDialog.razor` (new — port-forward dialog)

**Concrete approach**
1. Create `AksPageState` record holding all UI state:
   ```csharp
   public sealed record AksPageState(
       string CurrentNamespace,
       string ActiveResourceType,
       string ActiveFilter,
       IReadOnlyList<Deployment> Deployments,
       IReadOnlyList<Pod> Pods,
       // ... other resource collections
       bool IsLoading,
       string? ErrorMessage);
   ```
2. Create `AksPageOrchestrator` service that owns all business logic:
   ```csharp
   public sealed class AksPageOrchestrator(IAksClient client, ILogger<AksPageOrchestrator> logger)
   {
       public async Task<AksPageState> LoadAsync(AksPageState current, CancellationToken ct);
       public async Task<AksPageState> ScaleDeploymentAsync(AksPageState state, string name, int replicas, CancellationToken ct);
       public async Task<AksPageState> RestartDeploymentAsync(AksPageState state, string name, CancellationToken ct);
       // ... other commands
   }
   ```
3. Extract child components with `[Parameter]` binding:
   ```razor
   <AksToolbar Filter="@State.ActiveFilter" Resource="@State.ActiveResourceType"
              OnFilterChanged="@OnFilterChanged" OnResourceChanged="@OnResourceChanged" />
   <AksResourceGrid Data="@filteredResources" OnRowClick="@OnResourceSelected" />
   <AksDetailHost Selected="@selectedResource" OnOpenLogs="@OpenLogsAsync" />
   ```
4. `AksPage.razor` becomes a thin shell: markup, `@inject`, state binding, and delegation to `AksPageOrchestrator`.

**What NOT to do**: Do not create more `AksPage.*.cs` partial-class files. Partials share one class and do not reduce coupling. The `DashboardPage` and `ApiClientPage` partial-class decomposition improved file size but not testability.

**Acceptance criteria**
- `AksPage.razor` drops below 800 LOC.
- `AksPageOrchestrator` is unit-testable without bUnit.
- `dotnet build` and bUnit tests still pass.
- No behavioral change (verify manually: open AKS page, switch context, open logs, scale deployment).

### 3.2 Move per-resource filter logic into a reusable `AksResourceFilterService`

**Files to create / modify**
- `src/SwebKit.App/Services/AksResourceFilterService.cs` (new)
- `src/SwebKit.App/Services/AksPageOrchestrator.cs` (from Phase 3.1)

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

### 3.3 ~~Decompose `DashboardPage.razor`~~ — Already done in PR #27

`DashboardPage.razor` was decomposed from 2,960 to 710 LOC via partial classes (`.Builder.cs`, `.CustomTiles.cs`, `.Health.cs`, `.Preferences.cs`, `.Rendering.cs`). It also already inherits `SwebKitComponentBase`.

**Future improvement**: If testability becomes a concern, consider extracting a `DashboardPageOrchestrator` service (same pattern as proposed for `AksPage` in §3.1) to move business logic out of the partial classes.

### 3.4 ~~Continue API Client extraction~~ — Already done in PR #27

`ApiClientPage.razor` was decomposed from 1,947 to 529 LOC via partial classes (`.Collections.cs`, `.Commands.cs`, `.Curl.cs`, `.LinkedSave.cs`, `.Requests.cs`, `.Secrets.cs`, `.Tabs.cs`, `.Tree.cs`).

**Future improvement**: If testability becomes a concern, extract the request lifecycle into an `ApiClientRequestLifecycleService` as originally proposed. The partials currently mutate page-owned state directly.

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

### 4.2 ~~Remove direct implementation namespace imports from `.razor` files~~ — Already done in PR #27

The `@using SwebKit.Kubernetes.AksClient`, `@using SwebKit.Redis`, and `@using SwebKit.Azure.Storage` imports were removed from `.razor` files in PR #27. No action needed.

**Recommendation**: Add the architecture test from Phase 0.2 to prevent regression.

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

### 6.1 Pin .NET 10 SDK and stabilize packages

**Files to modify**
- `global.json` (created in Phase 0.1)
- `Directory.Packages.props` (created in Phase 0.1)
- All `*.csproj` `PackageReference` entries

**Concrete changes**
.NET 10 is the target framework and is intentionally chosen. Do **not** downgrade.
1. Ensure `global.json` pins the exact .NET 10 SDK version.
2. Ensure all floating `*` versions are replaced with exact pins via `Directory.Packages.props`.
3. Add a CI check for floating versions:
```yaml
- name: Check for floating versions
  run: |
    if grep -R 'Version="\*' src tests; then exit 1; fi
```

**Acceptance criteria**
- `dotnet --version` reports the pinned .NET 10 SDK.
- No `Version="*"` or `Version="0.*"` in repo.
- `dotnet list package --vulnerable` is clean of high/critical issues.
- `dotnet build SwebKit.slnx` and `dotnet test` pass.

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
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore SwebKit.slnx
      - run: dotnet build SwebKit.slnx --no-restore
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
        with: { dotnet-version: '10.0.x' }
      - run: dotnet workload install maui
      - run: dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0
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
| 1 | **0.1** (build props, global.json, CPM) | Everything else depends on a reproducible build. |
| 2 | **7.1** (CI pipeline) | Locks in quality early — every subsequent change is verified. |
| 3 | **1.1** and **1.2** (kubectl security + Azure credentials) | High-impact security fixes; small surface area. |
| 4 | **2.x** (exception hygiene, port-forward cleanup) | Improves reliability with small, safe changes. |
| 5 | **3.1** (AksPage child-component decomposition) | Largest maintainability win; use child components + state service, not partials. |
| 6 | **5.2** and **5.3** (log buffering, async startup tracing) | Quick performance wins; independent of architecture changes. |
| 7 | **4.1** (MauiProgram modularization) | Makes the codebase scalable. |
| 8 | **5.1** (SwebKitComponentBase adoption) | Broader render coalescing after structure is cleaner. |
| 9 | **4.3** (demo client move) | Reduces Core coupling; lower urgency. |
| 10 | **7.2** and **7.3** (new unit tests) | Locks in refactoring quality. |

**Already completed** (PR #27):
- ~~3.3~~ DashboardPage decomposition (2,960→710 LOC)
- ~~3.4~~ ApiClientPage decomposition (1,947→529 LOC)
- ~~4.2~~ Implementation namespace `@using` leaks removed

**Rejected**:
- ~~6.1 (downgrade to .NET 8)~~ — .NET 10 is the latest version and is intentionally chosen.
- ~~SwebKit.Composition project~~ — adds unnecessary indirection. Use extension methods instead.
- ~~Partial-class decomposition for AksPage~~ — use child components + state service instead.

---

*Originally generated by Devin on 2026-07-18. Revised by Cascade on 2026-07-18.*
