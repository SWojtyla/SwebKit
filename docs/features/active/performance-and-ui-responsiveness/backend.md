# Backend Module — Async, Threading, Startup & Cleanliness

Scope: service and integration-client code under `src/SwebKit.App/Services/`,
`src/SwebKit.Kubernetes/`, `src/SwebKit.Observability/`, `src/SwebKit.DevOps/`, and the shared
library projects. Every Phase 1-3 change is **behaviour-preserving**.

## Startup verdict (no action required)

The audit confirmed startup is clean and should **not** be changed:

- Repositories (`ProfileRepository`, `UiStateRepository`, `UserSettingsRepository`,
  `CollectionRepository`, `EnvironmentRepository`) are singletons but load **async and deferred**
  via `AppStateService.InitializeAsync()` from `MainLayout` — no synchronous I/O on the startup path.
- `ConnectionWarmupService.WarmAsync` is opt-in, fire-and-forget, threadpool-offloaded with a
  per-area timeout, invoked after first render.
- `MauiProgram` DI constructors are lightweight; `FileLoggerProvider` + crash handlers wire early.
- Two-phase init (fast essentials → render → deferred I/O) is the correct pattern.

One nuance to document, not fix now: the `IKeyVaultSecretResolver` factory reads
`AppStateService.Config` at build time — safe because the resolver is only exercised lazily, but
worth a comment noting it runs after `InitializeAsync`.

## Phase 1 — Async / UI-thread stalls

### 1.1 Remove the synchronous lock in a property getter (highest priority)

- **Where:** `PodHealthMonitorService.cs` (~line 87), `RecentEvents` getter uses `_lock.Wait()`.
- **Problem:** A property getter that blocks the UI thread whenever the poll loop holds `_lock`.
- **Fix direction:** Make reads lock-free. Keep the recent-events buffer in an immutable snapshot
  (`volatile IReadOnlyList<...>` swapped under the write lock), so the getter returns the current
  snapshot with no `Wait()`. The writer builds a new list and publishes it atomically.

### 1.2 Non-blocking shutdown

- **Where:** `App.xaml.cs` (~line 66): `Task.Run(() => sessions.StopAllAsync()).GetAwaiter().GetResult()`.
- **Fix direction:** Replace with a bounded wait — e.g. run `StopAllAsync` with a short timeout and
  do not block indefinitely on the exit thread; acceptable to fire-and-forget on `ProcessExit`.

### 1.3 Drop pointless `Task.Run` around async loops

- **Where:** `AlertMonitorService.cs` (~85), `PodHealthMonitorService.cs` (~169),
  `FileLoggerProvider.cs` (~41).
- **Problem:** The wrapped methods are 100% async I/O (timer waits, channel reads); `Task.Run`
  just burns a threadpool thread.
- **Fix direction:** Start directly: `_loopTask = LoopAsync(_cts.Token);` (assign the Task; don't
  block). Confirm the loop still starts and cancels cleanly on dispose.

### 1.4 Honor cancellation in baseline capture

- **Where:** `PodHealthMonitorService.cs` (~line 479): `GetPodsAsync(ns, null, CancellationToken.None)`.
- **Fix direction:** Pass the loop/class token so baseline capture cancels on shutdown.

## Phase 4 — Structural cleanliness (deferrable)

### 4.1 Split the `KubernetesAksClient` god class

- **Where:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (~4,400 lines, 62 public
  methods spanning pods, workloads, networking, Helm, log/exec, quotas, metrics, YAML).
- **Fix direction:** Start with a **behaviour-preserving `partial class` split** by concern
  (e.g. `KubernetesAksClient.Workloads.cs`, `.Networking.cs`, `.Helm.cs`, `.LogsExec.cs`,
  `.Quotas.cs`). No public signature changes; `IAksClient` stays stable. Only after that consider
  extracting collaborator classes. This is the largest item and can slip without blocking Phases 1-3.

### 4.2 `ConfigureAwait(false)` sweep in library projects

- **Where:** `SwebKit.Azure`, `.Kubernetes`, `.Redis`, `.DevOps`, `.Observability`, `.Core`
  (only ~8 `ConfigureAwait` uses repo-wide today).
- **Fix direction:** Add `ConfigureAwait(false)` to awaits in **library** code only (never in
  `SwebKit.App` UI components). Do it per-project with a build + test after each to keep the diff
  reviewable.

### 4.3 Replace fragile `.Result`-after-`WhenAll`

- **Where:** `KubernetesAksClient.cs` (~468-571), `AzureAppInsightsProvider.cs` (~59-74),
  `ContainerDetailPanel.razor` (~176-177), `NamespaceQuotaPanel.razor` (~150-151).
- **Note:** These are **currently safe** (tasks complete after `WhenAll`) — this is a
  fragility/readability fix, not a bug fix.
- **Fix direction:** Capture each task into a local before `WhenAll`, then `await` each (or await
  the individual tasks after `WhenAll`) rather than reading `.Result`.

### 4.4 Log the swallowed DevOps fallback

- **Where:** `DevOpsClient.cs` (~line 506) `catch { }`.
- **Fix direction:** Log at debug/trace that the stage-resolution fallback path was taken; keep the
  fallback behaviour.

### 4.5 Extract a base for copy-paste signal sources

- **Where:** `AksPodHealthSignalSource`, `AksPodRestartRateSignalSource`,
  `AksNamespaceHealthScoreSignalSource` share identical pod-fetch + null-guard code.
- **Fix direction:** Introduce `PodSignalSourceBase` with a `GetPodsAsync` helper; keep each
  source's evaluation logic distinct.

## Explicitly safe — do NOT "fix" (audit-confirmed)

- `async void` methods are all event handlers wrapping `await InvokeAsync(...)` — correct.
- `OperationCanceledException` handling (re-throw in UI, swallow-with-log in background) — correct.
- `CancellationToken` plumbing through integration clients — thorough.
- Polling intervals (AlertMonitor 10s, PodHealthMonitor 120s, Dashboard 60s) — reasonable.
- `NullPodHealthMonitorService` / `NullTrayLifecycleService` — intentional DI seams, not dead code.
- `RebuildClient` `TryEnter` throttle in `KubernetesAksClient` — deliberately non-blocking.

## Validation

- Build the changed project(s); run the matching test project(s)
  (`SwebKit.App.Tests`, `SwebKit.Kubernetes.Tests`, `SwebKit.DevOps.Tests`, etc.).
- For Phase 1, add/confirm focused tests: `RecentEvents` snapshot read under concurrent writes;
  loops start and cancel on dispose.
- Run an Aikido full scan on changed first-party files before merge.
