# SwebKit Codebase Review & Improvement Plan

> Scope: code quality, architecture, performance, security, reliability, and engineering-experience improvements.
> Method: static review of `SWojtyla/SwebKit` (≈86k source LOC, ≈29k test LOC). Verified against current `main` as of 2026-07-18. .NET 10 is the target framework and is intentionally chosen — this plan does **not** recommend downgrading.
> **Revised**: original plan by Devin corrected for inaccuracies and updated to reflect work already merged in PR #27.

---

## 1. Executive summary

SwebKit is a large .NET MAUI Blazor Hybrid operator workspace targeting .NET 10. The project shows strong architectural intent (domain-driven `SwebKit.Core`, integration-per-project, DI, abstractions, atomic persistence, custom file logging, warmup/caching, demo mode) and a mature pitfalls/rules culture. The codebase is in an **active state of consolidation**: PR #27 already decomposed `DashboardPage` (2,960→710 LOC) and `ApiClientPage` (1,947→529 LOC) via partial classes, adopted `SwebKitComponentBase` in 6 components, and removed implementation namespace leaks from `.razor` files. Remaining work: (1) decompose `AksPage` and other large pages, (2) harden command/exec security, (3) introduce CI + static analysis, (4) clean up exception handling, (5) add engineering tooling (`.editorconfig`, CPM, `global.json`).

---

## 2. Code-quality observations

### 2.1 The `SwebKit.App` UI layer is a monolith

- `src/SwebKit.App` contains **54,586 of 86,385 source LOC** (63%).
- **Already decomposed** (PR #27):
  - `DashboardPage.razor` — 710 lines (was 2,960; split into `.Builder.cs`, `.CustomTiles.cs`, `.Health.cs`, `.Preferences.cs`, `.Rendering.cs`)
  - `ApiClientPage.razor` — 529 lines (was 1,947; split into `.Collections.cs`, `.Commands.cs`, `.Curl.cs`, `.LinkedSave.cs`, `.Requests.cs`, `.Secrets.cs`, `.Tabs.cs`, `.Tree.cs`)
- **Still needing decomposition**:
  - `AksPage.razor` — 2,653 lines
  - `MessageListView.razor` — 1,613 lines
  - `RedisPage.razor` — 1,262 lines
  - `ObservabilityLogs.razor` — 1,120 lines
  - `AksDetailPanels.razor` — 1,036 lines
  - `MultiPodLogView.razor` — 833 lines
  - `ServiceBusPage.razor` — 831 lines

The partial-class decomposition used for `DashboardPage` and `ApiClientPage` improved file size but **did not reduce coupling** — the partials still share one class and mutate page-owned state directly. For `AksPage` and remaining large pages, a different approach is needed.

**Recommendations**
- For `AksPage`: extract **child components** with `[Parameter]` binding (e.g., `AksToolbar`, `AksResourceGrid`, `AksDetailHost`) rather than more partial classes. Move state logic into a page-specific `AksPageState` record and an `AksPageOrchestrator` service.
- For `MessageListView`: extract `MessagePeekPanel`, `MessageColumnChooser`, `MessageAutoRefreshToggle` as separate components.
- Introduce page-level state objects / ViewModels in `SwebKit.Core` or `SwebKit.App/Services` so pages only bind and dispatch.
- **Do not** use partial classes as the primary decomposition technique for remaining pages. Partials are acceptable for organizing an already-small page, but they do not reduce coupling or improve testability.

### 2.2 `SwebKitComponentBase` adoption is growing but incomplete

`SwebKitComponentBase` provides `RunAsync`, `RequestCoalescedRender`, a `ShouldRender` gate, and now shadows `StateHasChanged()` to open the gate automatically. `OnParametersSet` is also overridden to call `RequestRender()`, ensuring parent-driven parameter changes always render. An `SwebKitComponentAsyncBase` variant exists for `IAsyncDisposable` components.

**Currently adopted by 6 components**: `TopBar.razor`, `StatusBar.razor`, `DashboardPage.razor`, `ServiceBusPage.razor`, `ServiceBusGrid.razor` (async variant), `CollectionTree.razor`.

**Not yet adopted**: `AksPage.razor`, `RedisPage.razor`, `ObservabilityLogs.razor`, `MessageListView.razor`, and ~40+ other components that still call `StateHasChanged()` / `InvokeAsync(StateHasChanged)` directly.

Current counts: **159 direct `StateHasChanged()` calls** in `.razor` files, **429 `InvokeAsync(StateHasChanged)` calls** across all `src/`.

**Recommendations**
- Continue adopting `SwebKitComponentBase` incrementally per feature area. The `ShouldRender` gate is now safe thanks to the `StateHasChanged()` shadow and `OnParametersSet` override.
- Replace direct `StateHasChanged()` calls with `RequestRender()` / `RequestCoalescedRender()` as components are migrated.
- For streaming components (logs, metrics), use `RequestCoalescedRender()` with the debounce window.

### 2.3 Broad exception handling is still common

A scan found **39 `catch (Exception ...)` blocks** in `src/`. Most log and return a fallback value, which is acceptable for optional telemetry, but several are too broad:

- `WindowsCredentialStore.cs` — `Save` has `catch { }` on line 13, `Get` has `catch { return null; }` on line 26, `Delete` has `catch { }` on line 39, `ListKeys` has `catch { return []; }` on line 53. All silent, no logging.
- `ConnectionWarmupService.cs` — `WarmAksAsync`, `WarmRedisEntryAsync`, `WarmServiceBusNamespaceAsync` all have `catch (Exception) { // silently discard }` blocks (lines 91, 120, 146). Network/auth failures are not logged.
- `HttpRequestExecutor.cs` — `BuildResultAsync` has bare `catch { /* Swallow body read errors */ }` on line 290. GraphQL variable parsing has `catch { }` on line 177. Error parsing has `catch { return null; }` on line 237.
- `DevOpsClient.cs` — `catch { continue; }` on line 492 (only bare `catch {}` remaining in the codebase).

**Recommendations**
- Replace bare `catch` / `catch (Exception)` with specific exception types where possible, and at minimum log at `Debug`/`Warning` level.
- For `WindowsCredentialStore`: log at `Warning` without the secret value. Distinguish "not found" (expected) from "access denied" / "vault error" (unexpected).
- For `ConnectionWarmupService`: log warmup failures at `Warning` and surface a connection health indicator.
- For `HttpRequestExecutor`: catch `IOException` / `HttpRequestException` specifically for body read errors.
- For integration clients, distinguish *transient* vs *permanent* failures so callers can decide whether to retry, show a user message, or degrade gracefully.
- Never swallow `Exception` silently in credential/crypto code; at least emit an obfuscated log entry.

### 2.4 There is dead / legacy code

- `src/SwebKit.App/Services/PodHealthMonitorService.cs` (544 LOC) is replaced by the alert-monitor system; `MauiProgram.cs` registers `NullPodHealthMonitorService` for backward compat.
- `src/SwebKit.Agent.PocConsole` is a Phase 0 console that references implementation namespaces directly (`SwebKit.Kubernetes.AksClient`) and duplicates a lot of setup.
- `DashboardPage.razor` still injects `SwebKit.Core.Services.DemoAksClient`, `DemoDevOpsClient`, and `DemoRedisClient` directly instead of going through factory abstractions.

**Recommendations**
- Remove `PodHealthMonitorService` and the `IPodHealthMonitorService` registration once `Monitoring` coverage is complete.
- Archive `SwebKit.Agent.PocConsole` to a separate repo or delete it; if kept, it should not reference `SwebKit.Kubernetes.AksClient` directly.
- Stop injecting concrete demo clients into pages; provide `IDemoDataProvider` or similar abstractions.

### 2.5 No repository-wide code-style tooling

- No `.editorconfig` file.
- No `Directory.Build.props` / `Directory.Build.targets` for common settings (nullable, implicit usings, warnings-as-errors, package version centralization).
- **Solution file exists**: `SwebKit.slnx` (XML-based solution format, supported by .NET 9+). `dotnet build SwebKit.slnx` works from repo root.

**Recommendations**
- Add a root `.editorconfig` and `Directory.Build.props`.
- Turn on `TreatWarningsAsErrors` or at least `WarningsAsErrors` for nullable/reference warnings and a curated set of analyzers.
- Add `global.json` to pin the .NET 10 SDK version.
- Add `Directory.Packages.props` for Central Package Management (see §6.2).

---

## 3. Architecture observations

### 3.1 Project layering is good in principle but leaky in practice

`SwebKit.Core` holds abstractions, domain models, repositories, and shared services. `SwebKit.Azure`, `SwebKit.Kubernetes`, `SwebKit.Redis`, `SwebKit.DevOps`, and `SwebKit.Observability` are concrete integration projects. This is clean on paper.

**Leaks found**
- ~~`SwebKit.App` components import implementation namespaces via `@using`~~ — **Fixed in PR #27**. No `@using SwebKit.Kubernetes.*`, `@using SwebKit.Redis`, or `@using SwebKit.Azure.Storage` found in `.razor` files anymore.
- `SwebKit.Agent.PocConsole/Program.cs` still directly uses `SwebKit.Kubernetes.AksClient`.
- `MauiProgram.cs` (273 LOC) is both the DI composition root and a registry of concrete integration types; it does not use an `IHostBuilder`-style modular registration pattern.

**Recommendations**
- ~~Create a `SwebKit.Composition` project~~ — **Not recommended**: adds a new project for DI registration that standard MAUI apps keep in `MauiProgram.cs`. Instead, split registration into extension methods (see §3.2).
- Components should consume only `SwebKit.Core.Abstractions` / `SwebKit.Core.Models`; the `@using` leaks are already fixed.
- Add architecture tests (e.g., with `NetArchTest`) that enforce "`SwebKit.App` does not depend on `SwebKit.*` implementation namespaces" to prevent regression.

### 3.2 `MauiProgram.cs` is too large and knows too much

It currently contains all service registration, crash handlers, logging provider setup, warmup wiring, and conditional platform registrations (273 LOC). This is a single point of failure and makes unit testing the composition root impossible.

**Recommendations**
- Split registration into feature-module extension methods (no new project needed):
  - `AddSwebKitCore(this IServiceCollection)`
  - `AddSwebKitAzure(this IServiceCollection)`
  - `AddSwebKitKubernetes(this IServiceCollection)`
  - `AddSwebKitAgents(this IServiceCollection)`
  - `AddSwebKitObservability(this IServiceCollection)`
  - `AddSwebKitDevOps(this IServiceCollection)`
  - `AddSwebKitRedis(this IServiceCollection)`
- Keep these as `static partial class SwebKitServiceCollectionExtensions` files under `src/SwebKit.App/Hosting/`.
- Move crash-handler wiring and logging provider construction into a `SwebKit.App/Hosting/AppBootstrap.cs` class.

### 3.3 Demo clients are large and live in `Core`

`DemoAksClient.cs` (2,300 LOC), `DemoRedisClient.cs` (685 LOC), `DemoDevOpsClient.cs` (416 LOC), `DemoServiceBusClient.cs` (398 LOC), `DemoObservabilityProvider.cs` (386 LOC), and `DemoStorageClient.cs` (304 LOC) total over 5,000 LOC in `SwebKit.Core/Services`. They implement the same `IAksClient`/`IRedisClient`/etc. abstractions as the real clients.

This means `Core` is coupled to the *shape* of every integration and must be updated whenever a new operation is added. It also duplicates real logic.

**Recommendations**
- Move demo implementations into their respective integration projects (`SwebKit.Core` should not know how to fake AKS).
- Or, better, replace hand-written demo clients with a **record-based fake data source** driven from JSON/YAML seed files + a single generic `Demo*Client` adapter per integration. This dramatically reduces code volume and makes demo data configurable.

### 3.4 `IAksClient` and `KubernetesAksClient` mix SDK + kubectl/Helm responsibilities

`KubernetesAksClient.cs` plus its partial files (`LogsExec.cs`, `Helm.cs`, `Networking.cs`, `Workloads.cs`) is 1,549 LOC and does:

- Kubernetes API calls via `k8s.Kubernetes`
- `kubectl` CLI process spawning (port-forward, apply, shell)
- Helm diff
- Azure token fallback
- Kubeconfig parsing
- Pod log aggregation with channels

This violates single-responsibility and is hard to test.

**Recommendations**
- Split into:
  - `KubernetesApiClient` (SDK)
  - `KubectlProcessRunner` (CLI, all command building/escaping, validation)
  - `HelmRunner`
  - `AksTokenProvider`
- Keep `KubernetesAksClient` as a thin orchestrator that delegates to the above.

### 3.5 `DefaultAzureCredential` is constructed directly in `KubernetesAksClient`

`docs/pitfalls/azure-sdk.md` (AZ-4) mandates using `AzureCredentialFactory.CreateDefault()` for every Entra ID client, but `KubernetesAksClient.cs` constructs `new DefaultAzureCredential(AzureCredentialOptions)` at lines 255 and 1238. This is the exact anti-pattern the project documents.

**Recommendations**
- Replace both call sites with `AzureCredentialFactory.CreateDefault(AzureCredentialOptions)` or extend the factory to accept an options predicate.
- Add an analyzer or test that bans `new DefaultAzureCredential` outside `AzureCredentialFactory`.
- **Priority**: high — this violates the project's own documented rules.

---

## 4. Performance observations

### 4.1 `StateHasChanged` is called very frequently and inconsistently

Current counts: **159 direct `StateHasChanged()` calls** in `.razor` files, **429 `InvokeAsync(StateHasChanged)` calls** across all `src/`. `SwebKitComponentBase` coalescing is available but only adopted by 6 components.

**Recommendations**
- Replace direct per-event `StateHasChanged()` calls with coalesced batch updates as components are migrated to `SwebKitComponentBase`.
- For streaming scenarios (logs, metrics), buffer N lines or use a short flush timer rather than rendering per line.
- Adopt `SwebKitComponentBase` across all components and use `RequestCoalescedRender()`.

### 4.2 Startup path has synchronous file I/O on the UI thread

`PerformanceBaselineRecorder.Record` in `src/SwebKit.App/Services/PerformanceBaselineRecorder.cs` calls `File.AppendAllText` under a `lock` during `MauiProgram.CreateMauiApp` and `App.CreateWindow`. This is a blocking call on the startup/UI thread.

**Recommendations**
- Make startup tracing asynchronous (e.g., channel + background Task) or optional in Release builds.
- At minimum, wrap writes in `Task.Run` and batch them.

### 4.3 `MessageListView` and grid pages may re-render whole datasets

`MessageListView.razor` is 1,613 LOC and includes column chooser, custom property columns, density toggles, peek count, auto-refresh, etc. Every `PeekCount` change or auto-refresh tick likely triggers a full `StateHasChanged` and re-renders the entire list.

**Recommendations**
- Use `Virtualize` for large message grids if not already present.
- Keep list state (`PeekCount`, filter, density`) in a separate state object and pass immutable snapshots to the view.
- Avoid re-fetching the full message list on every timer tick when only metadata changed.

### 4.4 `ConnectionWarmupService` swallows failures silently

`WarmAksAsync`, `WarmRedisEntryAsync`, and `WarmServiceBusNamespaceAsync` all have empty `catch (Exception)` blocks. Background warmup failures are not logged or surfaced, so a user may think startup is fast but connections are broken.

**Recommendations**
- Log all warmup failures at `Warning` level.
- Surface a lightweight "connection health" indicator in the status bar or dashboard based on warmup results.
- Make warmup opt-out more discoverable and include per-area timeout/circuit-breaker state.

### 4.5 Pod log streaming can be chatty

`StreamPodLogsAsync` yields one `string` per log line. Consumers likely call `StateHasChanged` per line. `StreamDeploymentLogsAsync` is better — it uses `Channel<AggregatedLogLine>` and background `Task.Run` per pod.

**Recommendations**
- Standardize on the `Channel` + batch-flush pattern used in `StreamDeploymentLogsAsync` for all streaming views.
- Add a max lines / sliding window cap in the UI so long-running log tails do not grow memory indefinitely.

---

## 5. Security & reliability observations

### 5.1 `kubectl` / shell command construction

`KubernetesAksClient.LogsExec.cs` and `KubernetesAksClient.cs` spawn `Process.Start` with string-interpolated arguments. The code does validate Kubernetes resource names with a regex (`^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$`), which is good, but there are still issues:

- `OpenShellAsync` builds `args = $"exec -it {podName} -n {ns} -c {container}{kubeconfigArgs} -- /bin/sh"` and passes it to `wt.exe` or `cmd.exe` with `UseShellExecute = true`. `--kubeconfig` and `--context` global flags are placed *after* the container argument and before `--`, which is not the standard `kubectl` flag ordering and may be rejected or misinterpreted.
- `RunKubectlProcessAsync` accepts a raw `arguments` string and passes it to `ProcessStartInfo.Arguments`. Namespace and temp-file names are validated/quoted, but this is still a single-string API surface.

**Recommendations**
- Build `kubectl` arguments as an `IList<string>` and use `ProcessStartInfo.ArgumentList` (available in .NET Core 3+). This removes quoting/escaping risk entirely.
- Keep global flags (`--kubeconfig`, `--context`) before the subcommand or immediately after `kubectl`, per `kubectl` CLI conventions.
- Add integration tests that exercise pod names with shell metacharacters (should be rejected) and paths with spaces (should work).

### 5.2 API client allows arbitrary URLs and custom SSL validation bypass

`HttpRequestExecutor` builds `HttpRequestMessage` from user-supplied `request.Url`. `MauiProgram.cs` registers the `ApiClient` `HttpClient` with:

```csharp
ServerCertificateCustomValidationCallback =
    settings.VerifyApiClientSsl ? null : (_, _, _, _) => true
```

This is intentional for self-signed dev APIs, but the combination of arbitrary URL + custom SSL bypass creates an SSRF/trust-boundary risk if a user imports a malicious collection.

**Recommendations**
- Add an allow-list/block-list for URL schemes and hosts in API client settings.
- Default `VerifyApiClientSsl` to `true` and warn strongly when disabled.
- Store `VerifyApiClientSsl` per-environment, not globally, and show a persistent banner when the active environment has it disabled.

### 5.3 `OAuth2TokenManager` uses a TOCTOU port and no HTTPS enforcement

- `GetRandomAvailablePort` opens a `TcpListener`, reads the port, and immediately stops it. Another process could bind the port before the `HttpListener` starts.
- `auth.OAuth2TokenUrl` and `auth.OAuth2AuthUrl` are not validated as HTTPS.

**Recommendations**
- Bind the `HttpListener` directly without releasing the socket, or register the `sweb://oauth` URI scheme and use `WebAuthenticator` on Windows too.
- Validate OAuth URLs are HTTPS (with an explicit opt-in for localhost/http dev endpoints).
- Consider pinning redirect URI to `127.0.0.1` to avoid DNS rebinding.

### 5.4 Markdown rendering in AgentChat is sanitized but Mermaid JS path is custom

`AgentChatPanel.razor` uses `Markdig` with `.DisableHtml()` and renders Mermaid diagrams by base64-encoding diagram text into a `data-mermaid-b64` attribute. This is a thoughtful design. The JS-side `renderMermaidDiagrams` should decode the attribute and pass it to Mermaid, not `innerHTML`, to avoid HTML injection. Verify this in `wwwroot/js`.

**Recommendations**
- Audit `renderMermaidDiagrams` in `wwwroot/js` to confirm it does not set `innerHTML` with decoded content.
- Add a unit test that feeds markdown containing `<script>` and asserts no script tag survives rendering.

### 5.5 Process exit does not await cleanup

`App.xaml.cs` `OnProcessExit` calls `_ = sessions.StopAllAsync();` fire-and-forget. If the process exits before the task completes, `kubectl port-forward` child processes may remain alive.

**Recommendations**
- Block `OnProcessExit` with a short timeout (e.g., `StopAllAsync().WaitAsync(TimeSpan.FromSeconds(5))`).
- Or move cleanup to `App.UnhandledException` / `Window.Closed` where a graceful async path is available.

### 5.6 `WindowsCredentialStore` swallows all exceptions

`Save`/`Get`/`Delete`/`ListKeys` all have `catch { return null; }`. A corrupted credential vault or permission issue becomes a silent feature failure.

**Recommendations**
- Log credential-store exceptions at `Warning` level (without the secret).
- Distinguish "not found" (expected) from "access denied" / "vault error" (unexpected).

---

## 6. Dependencies & build tooling

### 6.1 Pin .NET 10 SDK and stabilize package versions

All projects target `net10.0` and reference `Microsoft.*` packages with `10.0.x` / `10.6.x` versions. .NET 10 is the latest version and is intentionally chosen. The MAUI app targets `net10.0-windows10.0.19041.0` with `MauiVersion` `10.0.70`.

**Recommendations**
- Add a `global.json` to pin the .NET 10 SDK version for reproducible builds.
- Add a `NuGet.config` with trusted feeds and disable floating package restores from public wildcard sources.
- Do **not** downgrade to .NET 8 — .NET 10 is the target and provides the latest MAUI features.
- Consider adding `Directory.Build.props` with `<EnableNETAnalyzers>true</EnableNETAnalyzers>` and `<AnalysisLevel>latest-Recommended</AnalysisLevel>`.

### 6.2 Floating package versions

`SwebKit.Core.csproj` and `SwebKit.Azure.csproj` reference:

- `<PackageReference Include="JsonPath.Net" Version="0.*" />`
- `<PackageReference Include="Bogus" Version="35.*" />`
- `<PackageReference Include="Azure.Storage.Blobs" Version="12.*" />`
- `<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.*" />`

Floating minor/patch versions make builds non-reproducible and supply-chain attacks harder to detect.

**Recommendations**
- Use Central Package Management (`Directory.Packages.props`) with exact versions.
- Run `dotnet list package --vulnerable` in CI and block builds on high-severity findings.

### 6.3 No continuous integration

`.github/workflows` is empty. There is no automated build, test, static analysis, or security scanning.

**Recommendations**
- Add a GitHub Actions workflow that:
  - Restores workloads and builds all projects.
  - Runs `dotnet test`.
  - Runs `dotnet format --verify-no-changes`.
  - Runs `dotnet list package --vulnerable`.
  - Runs an Aikido / CodeQL / Dependabot scan.
- Use a Windows runner for the MAUI project and Linux runners for core tests.

### 6.4 No static analysis or architecture tests

There is no `.editorconfig`, no `stylecop`, no `NetArchTest`, no `Meziantou.Analyzer`, and no `SonarCloud` integration.

**Recommendations**
- Enable built-in .NET analyzers (`<EnableNETAnalyzers>true</EnableNETAnalyzers>`) and set `AnalysisLevel`.
- Add `NetArchTest` rules to enforce layering (e.g., `SwebKit.App` cannot reference `SwebKit.Kubernetes.AksClient` directly).
- Consider `CSharpier` or `dotnet format` in CI to keep style consistent.

---

## 7. Testing observations

### 7.1 Test count and coverage

- **1,368 `[Fact]`/`[Theory]` methods** across `tests/`.
- `SwebKit.App.Tests` is 15,535 LOC and uses `bUnit` for component testing.
- `SwebKit.E2E.Tests` is only 795 LOC; likely under-developed.

### 7.2 Gaps

- The largest UI pages (`DashboardPage`, `AksPage`, `ApiClientPage`) are hard to unit test because they contain too much logic. Decomposing them will make `bUnit` tests smaller and faster.
- No architecture or dependency-rule tests.
- No integration tests for `kubectl` / Helm command building.
- No benchmark or startup performance tests.

**Recommendations**
- Add `NetArchTest` architecture tests.
- Add command-line integration tests for `KubectlProcessRunner` (see 5.1).
- Add E2E coverage for the "happy path" of at least one major feature (e.g., open dashboard → navigate to AKS → list pods).
- Add a startup-timing test that asserts `MauiProgram.CreateMauiApp` completes within a budget.

---

## 8. Prioritized action plan

### Immediate (this sprint)

1. **Add engineering tooling: `.editorconfig`, `Directory.Build.props`, `global.json`, `Directory.Packages.props`**
   - Pin .NET 10 SDK version; enable analyzers; centralize package versions.
   - Convert all floating `*` versions to exact pins.
2. **Add CI pipeline**
   - Build, test, vulnerability scan, format check. See §6.3.
   - This should be early — it locks in quality for all subsequent work.
3. **Harden `kubectl` / shell invocation**
   - Convert `KubernetesAksClient` to use `ArgumentList` and fix flag ordering.
   - Add resource-name validation tests and shell-metacharacter rejection tests.
4. **Replace direct `DefaultAzureCredential` in Kubernetes**
   - Use `AzureCredentialFactory` (or extend it) at the two call sites (lines 255, 1238).
5. **Stop swallowing exceptions silently in critical paths**
   - `WindowsCredentialStore`, `ConnectionWarmupService`, `HttpRequestExecutor`, `DevOpsClient`.

### Short term (next 2-4 weeks)

6. **Decompose `AksPage.razor` (2,653 LOC)**
   - Extract **child components** with `[Parameter]` binding, not more partial classes.
   - Move state logic into a page-specific `AksPageState` + `AksPageOrchestrator` service.
   - Target: `AksPage.razor` below 800 LOC.
7. **Continue `SwebKitComponentBase` adoption**
   - Migrate remaining components incrementally per feature area.
   - Replace direct `StateHasChanged()` with `RequestCoalescedRender()`.
8. **Coalesce high-frequency renders in streaming components**
   - `PodLogView`, `MultiPodLogView`, `MessageListView`, `ObservabilityLogs`.
   - Use `Channel<T>` + `PeriodicTimer` batch-flush pattern.
9. **Split `MauiProgram` into feature registration modules**
   - Extension methods under `src/SwebKit.App/Hosting/` (no new project).
10. **Fix `App.OnProcessExit` fire-and-forget cleanup**
    - Block with `StopAllAsync().Wait(TimeSpan.FromSeconds(5))`.

### Medium term (1-3 months)

11. **Refactor `KubernetesAksClient` into focused services**
    - `KubernetesApiClient` (SDK), `KubectlProcessRunner` (CLI), `HelmRunner`, `AksTokenProvider`.
12. **Decompose remaining large pages**
    - `MessageListView` (1,613), `RedisPage` (1,262), `ObservabilityLogs` (1,120), `AksDetailPanels` (1,036).
13. **Improve API client security boundaries**
    - URL allow-lists, per-environment SSL settings, HTTPS enforcement for OAuth.
14. **Add architecture tests and broader static analysis**
    - `NetArchTest`, `EnableNETAnalyzers`, CodeQL/Dependabot integration.
15. **Move demo clients out of `Core`**
    - Relocate to integration projects or replace with data-driven fakes.

### Strategic / ongoing

16. **Reduce `SwebKit.App` LOC share from 63% to <50%**
    - Through componentization, ViewModels, and moving logic into `Core`/integration services.
17. **Investigate and remove dead/legacy code**
    - `PodHealthMonitorService`, `SwebKit.Agent.PocConsole`, duplicated alert signal sources.
18. **Performance baselining**
    - Startup time, render time on large grids, memory usage during log streaming, port-forward cleanup.

### Not recommended (rejected items)

- ~~Downgrade to .NET 8~~ — .NET 10 is the latest version and is intentionally chosen.
- ~~Create `SwebKit.Composition` project~~ — adds unnecessary indirection. Use extension methods in `SwebKit.App/Hosting/` instead.
- ~~Use partial classes as primary decomposition for `AksPage`~~ — partials don't reduce coupling. Use child components + state services instead.

---

## 9. Caveats

- This review was originally generated by Devin via static code inspection. It has been revised and verified against the current `main` branch (as of 2026-07-18, PR #27 merged).
- LOC counts and file references are accurate as of that commit. Cross-check before starting work.
- The project is under active development; some issues may already be addressed by the time you read this. Cross-check against active feature docs in `docs/features/active/` before picking up work.

---

*Originally generated by Devin on 2026-07-18. Revised by Cascade on 2026-07-18.*
