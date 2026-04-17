# Backend Implementation - startup-connection-warmup

---

title: "Backend Implementation - startup-connection-warmup"
updated: "2026-04-17"

---

## Overview

Three changes compose the full feature:

1. **Warm-client cache services** — lightweight singleton holders per integration area (AKS, Redis, later Service Bus + Observability)
2. **`ConnectionWarmupService`** — coordinates the fan-out after AppState is initialized, writes results into the caches, swallows all failures silently
3. **Page bootstrap integration** — pages check the cache first; on cache hit they skip their own reconnect call

---

## Wave 1 — AKS + Redis warmup

### 1. `UserSettings` opt-out toggle

**File:** `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`

Add one property to the `UserSettings` class:

```csharp
public sealed class UserSettings
{
    public string Theme { get; set; } = string.Empty;
    public bool WarmupConnectionsOnStartup { get; set; } = true; // opt-out, default on
}
```

No migration needed — JSON deserialization already defaults missing fields through `NormalizeSettings`.

---

### 2. AKS warm-client cache

**New file:** `src/SwebKit.App/Services/AksWarmupCache.cs`

```csharp
using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public interface IAksWarmupCache
{
    void Store(AksClientBootstrapResult result);
    AksClientBootstrapResult? TryGet();
    void Invalidate();
}

public sealed class AksWarmupCache : IAksWarmupCache
{
    private AksClientBootstrapResult? _result;

    public void Store(AksClientBootstrapResult result) => _result = result;
    public AksClientBootstrapResult? TryGet() => _result;
    public void Invalidate() => _result = null;
}
```

Registered as `services.AddSingleton<IAksWarmupCache, AksWarmupCache>()` in `MauiProgram.cs`.

**Design note:** No key needed. The cache stores the single most-recently-warmed result. `AksPage` verifies the result matches its active config signature before consuming it (see Page Integration below).

---

### 3. Redis warm-client cache

**New file:** `src/SwebKit.App/Services/RedisWarmupCache.cs`

```csharp
using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public interface IRedisWarmupCache
{
    void Store(string cacheId, IRedisClient client);
    IRedisClient? TryGet(string cacheId);
    void Invalidate();
}

public sealed class RedisWarmupCache : IRedisWarmupCache
{
    private readonly Dictionary<string, IRedisClient> _clients = [];

    public void Store(string cacheId, IRedisClient client) => _clients[cacheId] = client;

    public IRedisClient? TryGet(string cacheId) =>
        _clients.TryGetValue(cacheId, out var c) ? c : null;

    public void Invalidate()
    {
        foreach (var c in _clients.Values)
            (c as IDisposable)?.Dispose();
        _clients.Clear();
    }
}
```

Registered as `services.AddSingleton<IRedisWarmupCache, RedisWarmupCache>()`.

**Design note:** Keyed by `CacheId` (string) matching the `RedisPage._loadedCacheId` guard so the page can look up the right client.

---

### 4. `ConnectionWarmupService`

**New file:** `src/SwebKit.App/Services/ConnectionWarmupService.cs`

```csharp
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public interface IConnectionWarmupService
{
    Task WarmAsync(IReadOnlyList<string> priorityAreas, CancellationToken ct = default);
    void InvalidateCaches();
}

public sealed class ConnectionWarmupService(
    AppStateService appState,
    UserSettingsRepository userSettings,
    IAksClientBootstrapper aksBootstrapper,
    IAksWarmupCache aksCache,
    IRedisWarmupCache redisCache) : IConnectionWarmupService
{
    private const int PerAreaTimeoutSeconds = 10;

    public async Task WarmAsync(IReadOnlyList<string> priorityAreas, CancellationToken ct = default)
    {
        if (!userSettings.Settings.WarmupConnectionsOnStartup)
            return;

        var tasks = BuildWarmupTasks(priorityAreas, ct);
        if (tasks.Count == 0)
            return;

        await Task.WhenAll(tasks);
    }

    public void InvalidateCaches()
    {
        aksCache.Invalidate();
        redisCache.Invalidate();
    }

    private List<Task> BuildWarmupTasks(IReadOnlyList<string> priorityAreas, CancellationToken ct)
    {
        var tasks = new List<Task>();

        // Warm AKS if configured and either it is an open tab area or no priority filter applies
        var aksConfig = appState.Config.AksConfig;
        if (aksConfig is not null && (priorityAreas.Count == 0 || priorityAreas.Contains("aks")))
            tasks.Add(WarmAksAsync(aksConfig, ct));

        // Warm each Redis cache entry
        var redisCaches = appState.Config.RedisConfig?.Caches;
        if (redisCaches is { Count: > 0 } && (priorityAreas.Count == 0 || priorityAreas.Contains("redis")))
            tasks.Add(WarmRedisAsync(redisCaches, ct));

        return tasks;
    }

    private async Task WarmAksAsync(AksConfig config, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerAreaTimeoutSeconds));
        try
        {
            var result = await aksBootstrapper.BootstrapAsync(
                new AksClientBootstrapRequest(
                    ClientOverride: null,
                    UseDemoData: false,
                    Config: config,
                    RequestedContext: config.KubeconfigContext,
                    RequestedNamespace: string.IsNullOrWhiteSpace(config.DefaultNamespace) ? "default" : config.DefaultNamespace),
                timeoutCts.Token);

            if (result.Status == AksClientBootstrapStatus.Connected && result.Client is not null)
                aksCache.Store(result);
        }
        catch (OperationCanceledException)
        {
            // Timeout or app-level cancellation — silently discard
        }
        catch (Exception)
        {
            // Network, auth, or config error — silently discard
        }
    }

    private async Task WarmRedisAsync(IReadOnlyList<RedisCacheEntry> entries, CancellationToken ct)
    {
        // Warm all entries concurrently, each with its own timeout
        var perEntry = entries.Select(entry => WarmRedisEntryAsync(entry, ct));
        await Task.WhenAll(perEntry);
    }

    private async Task WarmRedisEntryAsync(RedisCacheEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.ConnectionString))
            return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerAreaTimeoutSeconds));
        try
        {
            var client = await SwebKit.Redis.RedisClient.CreateAsync(entry);
            await client.TestConnectionAsync(timeoutCts.Token);
            redisCache.Store(entry.Id, client);
        }
        catch (OperationCanceledException)
        {
            // Silently discard
        }
        catch (Exception)
        {
            // Silently discard
        }
    }
}
```

Registered as:

```csharp
services.AddSingleton<IConnectionWarmupService, ConnectionWarmupService>();
```

**CS-2 note:** `OperationCanceledException` is caught separately in each warmup task and silently discarded. The cancellation is intentional (timeout or app shutdown) — it must not propagate or surface as an error.

---

### 5. DI registration in `MauiProgram.cs`

**File:** `src/SwebKit.App/MauiProgram.cs`

Add after the existing service registrations:

```csharp
builder.Services.AddSingleton<IAksWarmupCache, AksWarmupCache>();
builder.Services.AddSingleton<IRedisWarmupCache, RedisWarmupCache>();
builder.Services.AddSingleton<IConnectionWarmupService, ConnectionWarmupService>();
```

---

### 6. `MainLayout` trigger

**File:** `src/SwebKit.App/Components/Layout/MainLayout.razor`

Inject `IConnectionWarmupService` and call warmup after `AppState.InitializeAsync()` and tab restore:

```razor
@inject IConnectionWarmupService ConnectionWarmup
```

In `InitializeInBackgroundAsync()`:

```csharp
private async Task InitializeInBackgroundAsync()
{
    try
    {
        await AppState.InitializeAsync();
        Tabs.RestoreTabs(UiState.State.OpenTabs);
        await InvokeAsync(StateHasChanged); // BL-2

        // Fire warmup in background — non-blocking, all failures are silent
        var openAreas = UiState.State.OpenTabs
            .Select(t => t.Area?.ToLowerInvariant())
            .Where(a => a is not null)
            .Distinct()
            .ToList()!;
        _ = ConnectionWarmup.WarmAsync(openAreas!);
    }
    catch (OperationCanceledException)
    {
        throw; // CS-2
    }
    catch (Exception ex)
    {
        ShellErrors.PresentBackgroundInitializationFailure(ex);
    }
}
```

**Design note:** `WarmAsync` is fired as its own fire-and-forget (`_ = ...`). It must never block the shell render or propagate exceptions upward. The `openAreas` list is the tab-priority filter — only areas with open tabs are warmed.

Cache invalidation on profile reload:

```csharp
private void OnAppStateInitialized()
{
    // Invalidate warm clients whenever the profile is reloaded — credentials may have changed
    ConnectionWarmup.InvalidateCaches();
    InvokeAsync(StateHasChanged);
}
```

---

### 7. AKS page — cache-first bootstrap

**File:** `src/SwebKit.App/Components/Pages/AksPage.razor`

Inject the cache:

```razor
@inject IAksWarmupCache AksWarmupCache
```

In `BootstrapAndLoadAsync`, before calling `AksBootstrapper.BootstrapAsync`:

```csharp
// Check warm-client cache first
var warm = AksWarmupCache.TryGet();
if (warm is not null
    && warm.Status == AksClientBootstrapStatus.Connected
    && warm.Client is not null)
{
    Client = warm.Client;
    Contexts = warm.Contexts.ToList();
    Namespaces = warm.Namespaces.Count > 0 ? warm.Namespaces.ToList() : ["default"];
    ActiveContext = warm.ActiveContext;
    CurrentNamespace = warm.CurrentNamespace;
    ConnectionState.SetConnected("aks");
    IsLoading = false;
    await LoadResourcesAsync(ct); // proceed directly to loading resources
    await InvokeAsync(StateHasChanged);
    return;
}

// Cache miss — fall through to existing bootstrapper call
var result = await AksBootstrapper.BootstrapAsync(...);
```

**BL-3 note:** The `_lastBootstrapSignature` guard fires before `BootstrapAndLoadAsync` is called, so there is no double-execution risk. The cache check is purely an early-return optimization inside the existing bootstrap task.

---

### 8. Redis page — cache-first connect

**File:** `src/SwebKit.App/Components/Pages/RedisPage.razor`

Inject the cache:

```razor
@inject IRedisWarmupCache RedisWarmupCache
```

In `ConnectAsync`, before `RedisClient.CreateAsync`:

```csharp
// Check warm-client cache first
var warm = RedisWarmupCache.TryGet(SelectedCacheId);
if (warm is not null)
{
    Client = warm;
    ConnectionState.SetConnected("redis");
    return;
}

// Cache miss — fall through to existing create path
nextClient = await SwebKit.Redis.RedisClient.CreateAsync(entry);
```

**Liveness note:** If the warm client subsequently fails on its first real operation (e.g., `SCAN` or `PING`), the existing error-recovery path in `ConnectAndScanAsync` catches the exception, sets `ErrorMessage`, and calls `StartReconnectAndScanAsync`. No new handling needed — the page already handles connection errors post-connect.

---

## Wave 2 additions (scope placeholder)

Wave 2 extends the same pattern:

| Area          | New cache interface         | New warmup task in service        | Page integration point                               |
| ------------- | --------------------------- | --------------------------------- | ---------------------------------------------------- |
| Service Bus   | `IServiceBusWarmupCache`    | `WarmServiceBusAsync(namespaces)` | `ServiceBusPage` before `BuildInitialStates` fan-out |
| Observability | `IObservabilityWarmupCache` | `WarmObservabilityAsync(config)`  | `ObservabilityPage` before ARM discovery call        |

Wave 2 also adds the opt-out toggle to the Settings page UI (`src/SwebKit.App/Components/Pages/SettingsPage.razor`), bound to `UserSettings.WarmupConnectionsOnStartup` via the existing settings save path in `UserSettingsRepository`.

---

## Architecture impact

The App Bootstrap Flow in `design.md` must be updated to show the warmup step:

```
MainLayout → AppState.InitializeAsync() → Tabs.RestoreTabs()
           → ConnectionWarmupService.WarmAsync(openAreas) [fire-and-forget, background]
               → AksBootstrapper.BootstrapAsync() → IAksWarmupCache.Store()
               → RedisClient.CreateAsync() per entry → IRedisWarmupCache.Store()
```

No changes to `architecture.md` — no new external dependencies or top-level boundary changes.
