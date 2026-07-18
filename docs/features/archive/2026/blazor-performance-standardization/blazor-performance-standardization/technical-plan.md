# Blazor Performance Standardization - Technical Plan

## Wave A: Enhance SwebKitComponentBase

### A1. Add Configurable Coalescing Support

**File:** `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

**Changes:**
- Add `RenderCoalescingOptions` record with `DefaultDebounceMs` and `PerComponentOverrides`
- Add virtual `GetCoalescingDebounce()` method returning `TimeSpan.FromMilliseconds(75)` by default
- Enhance `RequestCoalescedRender()` to use configurable debounce instead of immediate execution
- Add `CancellationTokenSource` field for render cancellation
- Implement `IDisposable` pattern for cleanup

**Code Pattern:**
```csharp
protected virtual TimeSpan GetCoalescingDebounce() => TimeSpan.FromMilliseconds(75);

protected void RequestCoalescedRender()
{
    _needsRender = true;
    if (_renderPending) return;
    _renderPending = true;
    _ = InvokeAsync(async () =>
    {
        try
        {
            await Task.Delay(GetCoalescingDebounce(), _renderCts.Token);
            if (!_renderCts.Token.IsCancellationRequested)
            {
                _renderPending = false;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) { }
    });
}
```

### A2. Add Performance Metrics

**File:** `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

**Changes:**
- Add `RenderMetrics` record with `RequestedCount`, `ExecutedCount`, `CoalescedCount`
- Add protected `Metrics` field
- Increment counters in `RequestCoalescedRender()` and `ShouldRender()`
- Add virtual `LogMetrics()` method for telemetry integration

### A3. Add Lifecycle Management

**File:** `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

**Changes:**
- Implement `IDisposable` interface
- Cancel pending renders in `Dispose()`
- Add `OnAfterRender` override to reset metrics if needed
- Ensure thread-safe cancellation token disposal

---

## Wave B: Migrate DashboardPage

### B1. Replace Custom Coalescing with Base Class

**File:** `src/SwebKit.App/Components/Pages/DashboardPage.Rendering.cs`

**Changes:**
- Remove `RenderCoalescingWindow` constant
- Remove `_renderStateLock`, `_renderQueued` fields
- Remove `QueueRenderAsync()` method
- Override `GetCoalescingDebounce()` to return `TimeSpan.FromMilliseconds(75)`
- Replace `QueueRenderAsync()` calls with `RequestCoalescedRender()`
- Keep `InvalidateRenderState()` and render state caching logic

**Migration Pattern:**
```csharp
// OLD:
private void RequestRender(bool immediate = false, bool invalidateRenderState = true)
{
    if (invalidateRenderState) InvalidateRenderState();
    if (immediate) { _ = InvokeAsync(StateHasChanged); return; }
    _ = QueueRenderAsync();
}

// NEW:
private void RequestRender(bool immediate = false, bool invalidateRenderState = true)
{
    if (invalidateRenderState) InvalidateRenderState();
    if (immediate) { _needsRender = true; _ = InvokeAsync(StateHasChanged); return; }
    RequestCoalescedRender();
}
```

### B2. Validate Render State Caching

**File:** `src/SwebKit.App/Components/Pages/DashboardPage.Rendering.cs`

**Changes:**
- Ensure `InvalidateRenderState()` still works with base class pattern
- Test that render state caching (double-check locking) is preserved
- Verify no performance regression in `GetRenderState()`

---

## Wave C: Migrate CollectionTree

### C1. Remove ShouldRender Override

**File:** `src/SwebKit.App/Components/ApiClient/CollectionTree.razor`

**Changes:**
- Remove `_shouldRender` field
- Remove `ShouldRender()` override
- Replace `InvalidateRender()` calls with `RequestRender()`
- Ensure render invalidation still triggers on parameter changes

**Migration Pattern:**
```csharp
// OLD:
private bool _shouldRender = true;
protected override bool ShouldRender()
{
    if (_shouldRender) { _shouldRender = false; return true; }
    return false;
}
private void InvalidateRender() => _shouldRender = true;

// NEW:
private void InvalidateRender() => RequestRender();
```

### C2. Validate Virtualization Performance

**File:** `src/SwebKit.App/Components/ApiClient/CollectionTree.razor`

**Changes:**
- Test that virtualization still works correctly with base class pattern
- Verify no performance regression in tree rendering
- Ensure icon caching still prevents unnecessary re-renders

---

## Wave D: Configurable Debounce Windows

### D1. Create Configuration Model

**File:** `src/SwebKit.Core/Configuration/RenderCoalescingOptions.cs`

**Changes:**
- Create `RenderCoalescingOptions` record
- Add `DefaultDebounceMs` property (default: 75ms)
- Add `ComponentDebounceOverrides` dictionary for per-component tuning
- Add `EnvironmentPresets` for dev/staging/prod configurations

### D2. Integrate with App Settings

**File:** `src/SwebKit.Core/Configuration/UiStateRepository.cs` or new config provider

**Changes:**
- Add render coalescing configuration to `appsettings.json`
- Load configuration at startup
- Provide configuration to components via DI or base class

**Configuration Pattern:**
```json
{
  "RenderCoalescing": {
    "DefaultDebounceMs": 75,
    "ComponentOverrides": {
      "DashboardPage": 75,
      "AksPage": 150,
      "CollectionTree": 50
    },
    "EnvironmentPresets": {
      "Development": {
        "DefaultDebounceMs": 50
      },
      "Production": {
        "DefaultDebounceMs": 75
      }
    }
  }
}
```

### D3. Update Component Overrides

**Files:**
- `src/SwebKit.App/Components/Pages/DashboardPage.Rendering.cs`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/ApiClient/CollectionTree.razor`

**Changes:**
- Override `GetCoalescingDebounce()` in each component
- Read from configuration instead of hardcoded values
- Provide fallback defaults if configuration unavailable

---

## Wave E: Performance Telemetry

### E1. Add Metrics Collection

**File:** `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

**Changes:**
- Implement `RenderMetrics` tracking in base class
- Add `GetRenderMetrics()` method for telemetry extraction
- Make metrics collection conditional via configuration

### E2. Add Diagnostic Endpoints

**File:** New diagnostic component or logging integration

**Changes:**
- Create `RenderPerformanceDiagnostics` component
- Add endpoint to display metrics per component
- Integrate with existing logging/telemetry systems

### E3. Add Coalescing Effectiveness Logging

**File:** `src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`

**Changes:**
- Log when renders are coalesced (requested but not executed)
- Log coalescing ratio (executed/requested)
- Add performance warnings if coalescing ratio is low

---

## Wave F: Validation and Testing

### F1. Unit Tests

**File:** `tests/SwebKit.App.Tests/Components/Shared/SwebKitComponentBaseTests.cs`

**Test Cases:**
- `RequestCoalescedRender_WithDefaultDebounce_DelaysRender`
- `RequestCoalescedRender_WithImmediateFlag_RendersImmediately`
- `ShouldRender_WithoutRequest_ReturnsFalse`
- `ShouldRender_WithRequest_ReturnsTrueOnce`
- `Dispose_CancelsPendingRenders`
- `Metrics_AccumulateCorrectly`

### F2. Integration Tests

**File:** `tests/SwebKit.App.Tests/Components/Pages/DashboardPageRenderingTests.cs`

**Test Cases:**
- `DashboardPage_RenderCoalescing_WorksAfterMigration`
- `DashboardPage_RenderStateCache_Preserved`
- `DashboardPage_MultipleRapidRequests_CoalesceCorrectly`

### F3. Performance Regression Tests

**File:** `tests/SwebKit.App.Tests/Performance/RenderPerformanceTests.cs`

**Test Cases:**
- `DashboardPage_RenderTime_NoRegression`
- `CollectionTree_VirtualizationPerformance_NoRegression`
- `AksPage_IncrementalRendering_NoRegression`

### F4. Manual Validation

**Validation Steps:**
1. Load dashboard with rapid data updates - verify smooth rendering
2. Switch between AKS tabs - verify no virtualization observer recreation
3. Filter CollectionTree - verify responsive without excessive renders
4. Monitor render metrics in production - verify coalescing effectiveness
