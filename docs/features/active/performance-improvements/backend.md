# Backend — Performance Improvements

---

title: "Backend — Performance Improvements"
owner: ""
status: "Not started"

---

## Goal

Optimize the core service layer to eliminate UI-blocking initialization, replace the synchronous event bus with an async-capable alternative, and add state caching to prevent redundant data loading on navigation.

## Impacted areas

- `src/SwebKit.App/Services/AppStateService.cs` — startup init flow
- `src/SwebKit.Core/Services/AppEventBus.cs` — synchronous pub/sub
- `src/SwebKit.Core/Abstractions/IAppEventBus.cs` — event bus contract
- `src/SwebKit.App/Components/Layout/MainLayout.razor` — blocking init call
- `src/SwebKit.App/MauiProgram.cs` — DI registration for caching services

---

## Wave 0 — App Startup & MainLayout (🔴 HIGH)

### PERF-1 — Non-blocking AppState initialization in MainLayout

**Problem:** `MainLayout.razor` `OnInitializedAsync` calls `AppState.InitializeAsync()` which performs disk I/O (reads `profiles.json` + `ui-state.json`). This blocks ALL rendering — no navigation shell, no sidebar, no loading indicator — until both files are read and parsed.

**Files to change:**

- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Services/AppStateService.cs`

**Approach:**

1. Split `AppState.InitializeAsync()` into two phases:
   - **Phase 1 (fast):** Load essential startup data synchronously from memory defaults, then yield to render the shell.
   - **Phase 2 (background):** Load `profiles.json` and `ui-state.json` from disk asynchronously, then notify the UI via `StateHasChanged`.
2. In `MainLayout.OnInitializedAsync`, call Phase 1 only. Fire Phase 2 as a background task.
3. The layout renders immediately with default/empty state, then updates when disk data arrives.

**Expected impact:** Shell renders in <50ms instead of waiting for disk I/O (100-500ms+).

**Pitfall guard:** Use `await InvokeAsync(StateHasChanged)` after the background load completes (BL-2).

### PERF-2 — Lazy profile loading

**Problem:** `AppState.InitializeAsync()` loads ALL profile data upfront, including profiles the user may not navigate to this session.

**Files to change:**

- `src/SwebKit.App/Services/AppStateService.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs` (if applicable)

**Approach:**

1. Load only the active profile and profile index at startup.
2. Defer loading of inactive profile data until the user switches to that profile.
3. Add `LoadProfileAsync(string profileId)` as a targeted load method.

**Expected impact:** Startup disk I/O reduced proportionally to number of saved profiles.

### PERF-3 — Startup state readiness signal

**Problem:** After making init non-blocking (PERF-1), pages need a way to know when `AppState` has finished loading rather than assuming it's ready after `OnInitializedAsync`.

**Files to change:**

- `src/SwebKit.App/Services/AppStateService.cs`
- `src/SwebKit.Core/Abstractions/IAppStateService.cs` (interface)

**Approach:**

1. Add `bool IsInitialized` property and `Task WhenInitializedAsync()` method to `AppStateService`.
2. Pages that depend on full state (e.g., PipelinesPage needing saved release configs) can `await AppState.WhenInitializedAsync()` at the start of their own init, or react to `IsInitialized` changing via event bus.
3. Pages that only need connection info (e.g., AksPage) can start loading immediately without waiting.

**Expected impact:** Enables per-page decisions about whether to wait for full state or start with partial state.

### PERF-4 — MainLayout renders shell immediately with loading placeholder

**Problem:** Even after PERF-1, the `@Body` content area needs a visual state while pages load their data.

**Files to change:**

- `src/SwebKit.App/Components/Layout/MainLayout.razor`

**Approach:**

1. After PERF-1, the layout shell (sidebar, top bar) renders immediately.
2. Add a lightweight loading placeholder in the `@Body` area that shows while `!AppState.IsInitialized`.
3. Once initialized, render the actual page content.
4. This is distinct from per-page loading (Wave 2) — this covers the brief window before any page-level init runs.

**Expected impact:** Users see the familiar app shell immediately; no blank white screen.

---

## Wave 1 — Async Event Bus (🔴 HIGH)

### PERF-5 — Add async handler support to IAppEventBus

**Problem:** `AppEventBus.cs` uses `Action<T>` handlers. All subscribers execute synchronously on the publisher's thread. If a subscriber does heavy work (e.g., `StateHasChanged`, UI recomposition, file I/O), it blocks the publishing thread, which in Blazor is often the render thread.

**Files to change:**

- `src/SwebKit.Core/Abstractions/IAppEventBus.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`

**Approach:**

1. Add `Func<T, Task>` overload alongside existing `Action<T>`:
   ```csharp
   IDisposable Subscribe<T>(Func<T, Task> asyncHandler);
   ```
2. In `PublishAsync<T>`, invoke sync handlers first (preserving current behavior), then `await Task.WhenAll(asyncHandlers)`.
3. Keep `Action<T>` subscribe as the default — no breaking changes to existing subscribers.
4. Add `PublishAsync<T>` as a new method; keep `Publish<T>` for callers that don't need to await.

**Expected impact:** Heavy subscribers (like UI components needing `InvokeAsync(StateHasChanged)`) no longer block the publisher's thread.

**Pitfall guard:** Async handlers must catch `OperationCanceledException` and re-throw (CS-2). Wrap handler invocation in try/catch with proper propagation.

### PERF-6 — Migrate critical subscribers to async handlers

**Problem:** Even after PERF-5 adds async support, existing subscribers are still sync. The highest-impact subscribers need migration.

**Files to change:**

- `src/SwebKit.App/Components/Pages/AksPage.razor` — event subscriptions
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` — event subscriptions
- `src/SwebKit.App/Components/Layout/MainLayout.razor` — theme/profile change handlers
- Other pages as needed (identify by grep for `EventBus.Subscribe`)

**Approach:**

1. Identify all `EventBus.Subscribe<T>(Action<T>)` calls across the codebase.
2. For each subscriber that calls `StateHasChanged` or performs I/O inside the handler, migrate to the async overload with `await InvokeAsync(StateHasChanged)`.
3. For lightweight subscribers (simple field assignment), keep sync.

**Expected impact:** Event publishing no longer blocks the render thread; multiple UI components can update concurrently after a single event.

---

## Wave 4 — Navigation State Caching (🟡 MEDIUM)

### PERF-17 — Page-level data cache service

**Problem:** Every page navigation triggers a full re-init and re-fetch of data. Navigating AKS → Service Bus → AKS causes the AKS page to re-fetch all 11 datasets, even if they were loaded seconds ago.

**Files to change:**

- New: `src/SwebKit.App/Services/PageDataCache.cs`
- `src/SwebKit.App/MauiProgram.cs` — DI registration

**Approach:**

1. Create a `PageDataCache` singleton service with a simple time-based expiration:
   ```csharp
   public class PageDataCache
   {
       public T? Get<T>(string key);
       public void Set<T>(string key, T value, TimeSpan? ttl = null);
       public void Invalidate(string key);
       public void InvalidateAll();
   }
   ```
2. Default TTL: 60 seconds (configurable).
3. Pages check cache before fetching. On cache hit, render immediately and optionally refresh in background.
4. Invalidate on: profile switch, explicit refresh button click, config change events.

**Expected impact:** Back-navigation to previously visited pages renders instantly from cache.

### PERF-18 — Integrate cache into high-traffic pages

**Problem:** Cache service exists (PERF-17) but pages need to adopt it.

**Files to change:**

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`

**Approach:**

1. In each page's init method:
   ```csharp
   var cached = PageDataCache.Get<AksPageData>("aks");
   if (cached is not null)
   {
       // Render immediately from cache
       _data = cached;
       StateHasChanged();
       // Optionally refresh in background
       _ = RefreshInBackgroundAsync();
   }
   else
   {
       await LoadFullAsync();
   }
   ```
2. On successful load, store to cache.
3. On explicit refresh (toolbar button), bypass cache and invalidate.

**Expected impact:** Repeat navigation to AKS/ServiceBus/Pipelines pages: <100ms perceived load (from cache) vs 1-5s (from API).

**Pitfall guard:** Set guard state before `await` (BL-3) to prevent double-loads during background refresh.

---

## Contracts

### AppStateService changes

```csharp
// New members on AppStateService
bool IsInitialized { get; }
Task WhenInitializedAsync();
Task InitializeEssentialsAsync();  // fast, sync-safe subset
Task InitializeFullAsync();         // disk I/O, background
```

### IAppEventBus additions

```csharp
// New overloads (non-breaking)
IDisposable Subscribe<T>(Func<T, Task> asyncHandler);
Task PublishAsync<T>(T message);
```

### PageDataCache contract

```csharp
public class PageDataCache
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan? ttl = null);
    void Invalidate(string key);
    void InvalidateAll();
}
```

---

## Tasks

- [ ] **PERF-1** Split `AppState.InitializeAsync` into two phases `[dotnet-expert]`
- [ ] **PERF-2** Lazy profile loading `[dotnet-expert]`
- [ ] **PERF-3** Add `IsInitialized` / `WhenInitializedAsync` to AppStateService `[dotnet-expert]`
- [ ] **PERF-4** MainLayout shell-first rendering `[blazor-expert]`
- [ ] **PERF-5** Add async handlers to IAppEventBus `[dotnet-expert]`
- [ ] **PERF-6** Migrate critical subscribers to async `[blazor-expert]`
- [ ] **PERF-17** Implement PageDataCache service `[dotnet-expert]`
- [ ] **PERF-18** Integrate cache into AKS, ServiceBus, Pipelines pages `[blazor-expert]`

## Validation

- Tests: Not started
- Manual checks:
  - [ ] App launches and renders shell in <100ms (no blank screen)
  - [ ] Event publishing does not block UI thread
  - [ ] Back-navigation renders cached data immediately
  - [ ] Cache invalidates on profile switch

## Notes

- PERF-4 bridges backend (init flow) and frontend (layout rendering). Listed here because the core change is in `AppStateService`.
- PERF-17/18 are Wave 4 but specified here since the service is a backend concern.
- See [decisions.md](./decisions.md) Decision 001 for the two-phase init rationale.
