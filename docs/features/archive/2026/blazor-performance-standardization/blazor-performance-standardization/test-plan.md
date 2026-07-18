# Blazor Performance Standardization - Test Plan

## Test Scope

This feature requires comprehensive testing to ensure performance optimizations are preserved while standardizing render coalescing patterns. Testing spans unit, integration, performance regression, and manual validation levels.

## Test Levels

### Level 1: Unit Tests (SwebKitComponentBase)

**File:** `tests/SwebKit.App.Tests/Components/Shared/SwebKitComponentBaseTests.cs`

#### Test Cases

**TC1: RequestCoalescedRender_WithDefaultDebounce_DelaysRender**
- Arrange: Create test component inheriting from `SwebKitComponentBase`
- Act: Call `RequestCoalescedRender()` twice rapidly
- Assert: Only one `StateHasChanged` occurs after debounce window
- Assert: `ShouldRender()` returns true only once

**TC2: RequestCoalescedRender_WithImmediateFlag_RendersImmediately**
- Arrange: Create test component
- Act: Call `RequestCoalescedRender()` with immediate flag
- Assert: `StateHasChanged` occurs immediately without delay
- Assert: `ShouldRender()` returns true

**TC3: ShouldRender_WithoutRequest_ReturnsFalse**
- Arrange: Create test component
- Act: Call `ShouldRender()` without any render request
- Assert: Returns false
- Assert: `_needsRender` flag remains false

**TC4: ShouldRender_WithRequest_ReturnsTrueOnce**
- Arrange: Create test component, call `RequestRender()`
- Act: Call `ShouldRender()` twice
- Assert: First call returns true
- Assert: Second call returns false (flag reset)

**TC5: Dispose_CancelsPendingRenders**
- Arrange: Create test component, call `RequestCoalescedRender()`
- Act: Call `Dispose()` before debounce completes
- Assert: Pending render is cancelled
- Assert: No `StateHasChanged` occurs after disposal

**TC6: Metrics_AccumulateCorrectly**
- Arrange: Create test component with metrics enabled
- Act: Call `RequestCoalescedRender()` 5 times rapidly
- Assert: `RequestedCount` = 5
- Assert: `ExecutedCount` = 1 (coalesced)
- Assert: `CoalescedCount` = 4

**TC7: GetCoalescingDebounce_DefaultValue_Returns75ms**
- Arrange: Create test component without override
- Act: Call `GetCoalescingDebounce()`
- Assert: Returns `TimeSpan.FromMilliseconds(75)`

**TC8: GetCoalescingDebounce_CustomOverride_ReturnsCustomValue**
- Arrange: Create test component with `GetCoalescingDebounce()` override returning 150ms
- Act: Call `GetCoalescingDebounce()`
- Assert: Returns `TimeSpan.FromMilliseconds(150)`

---

### Level 2: Integration Tests (DashboardPage)

**File:** `tests/SwebKit.App.Tests/Components/Pages/DashboardPageRenderingTests.cs`

#### Test Cases

**TC9: DashboardPage_RenderCoalescing_WorksAfterMigration**
- Arrange: Create DashboardPage with test data
- Act: Trigger 10 rapid activity events
- Assert: Renders are coalesced into ≤ 2 actual UI updates
- Assert: Final render state matches all 10 events

**TC10: DashboardPage_RenderStateCache_Preserved**
- Arrange: Create DashboardPage with cached render state
- Act: Call `GetRenderState()` twice without invalidation
- Assert: First call builds state
- Assert: Second call returns cached state (no rebuild)
- Assert: Double-check locking works correctly

**TC11: DashboardPage_ImmediateRender_BypassesCoalescing**
- Arrange: Create DashboardPage
- Act: Call `RequestShellRender(immediate: true)`
- Assert: Render occurs immediately without debounce
- Assert: `StateHasChanged` called synchronously

**TC12: DashboardPage_InvalidateRenderState_TriggersRebuild**
- Arrange: Create DashboardPage with cached state
- Act: Call `InvalidateRenderState()` then `GetRenderState()`
- Assert: State is rebuilt (not cached)
- Assert: Dirty flag is reset after rebuild

**TC13: DashboardPage_MultipleRapidRequests_CoalesceCorrectly**
- Arrange: Create DashboardPage
- Act: Call `RequestTileRender()` 20 times in 100ms
- Assert: ≤ 3 actual renders occur (75ms debounce)
- Assert: Final state includes all changes

---

### Level 3: Integration Tests (CollectionTree)

**File:** `tests/SwebKit.App.Tests/Components/ApiClient/CollectionTreeTests.cs`

#### Test Cases

**TC14: CollectionTree_RequestRender_TriggersShouldRender**
- Arrange: Create CollectionTree with test collections
- Act: Call `InvalidateRender()` (now uses `RequestRender()`)
- Assert: `ShouldRender()` returns true on next cycle
- Assert: Component re-renders

**TC15: CollectionTree_NoRenderRequest_SuppressesRender**
- Arrange: Create CollectionTree
- Act: Trigger parameter change without calling `InvalidateRender()`
- Assert: `ShouldRender()` returns false
- Assert: Component does not re-render

**TC16: CollectionTree_VirtualizationStillWorks**
- Arrange: Create CollectionTree with 1000 nodes
- Act: Scroll through tree
- Assert: Only visible nodes are rendered (virtualization active)
- Assert: No performance regression vs baseline

**TC17: CollectionTree_IconCacheStillPreventsRerenders**
- Arrange: Create CollectionTree with static icons
- Act: Trigger parent re-render
- Assert: Icon instances are reused (not recreated)
- Assert: No unnecessary icon re-renders

---

### Level 4: Integration Tests (AksPage)

**File:** `tests/SwebKit.App.Tests/Components/Pages/AksPageRenderingTests.cs`

#### Test Cases

**TC18: AksPage_IncrementalRendering_StillWorks**
- Arrange: Create AksPage with mock client returning slow datasets
- Act: Call `LoadAsync()` with 5 datasets
- Assert: Each dataset renders as it completes (not waiting for all)
- Assert: Dirty flag batching still works

**TC19: AksPage_FlushLoopDebounce_BatchesUIUpdates**
- Arrange: Create AksPage with rapid dataset completion
- Act: Load datasets completing within 150ms window
- Assert: UI updates are batched (not per-dataset)
- Assert: Final flush renders remaining state

**TC20: AksPage_TabSwitch_DoesNotRecreateVirtualization**
- Arrange: Create AksPage with multiple tabs
- Act: Switch between Pods and Deployments tabs
- Assert: Virtualization observers are not recreated
- Assert: Grids remain mounted (visibility toggle only)

---

### Level 5: Performance Regression Tests

**File:** `tests/SwebKit.App.Tests/Performance/RenderPerformanceTests.cs`

#### Test Cases

**TC21: DashboardPage_RenderTime_NoRegression**
- Arrange: Create performance benchmark for DashboardPage
- Act: Measure render time with 100 rapid activity events
- Assert: Render time ≤ baseline + 10%
- Assert: Memory allocation ≤ baseline + 10%

**TC22: CollectionTree_VirtualizationPerformance_NoRegression**
- Arrange: Create CollectionTree with 10,000 nodes
- Act: Measure scroll performance and render time
- Assert: Scroll FPS ≥ 60
- Assert: Render time per 100 nodes ≤ baseline + 10%

**TC23: AksPage_IncrementalRendering_NoRegression**
- Arrange: Create AksPage with 10 datasets
- Act: Measure time to first visible render
- Assert: First render time ≤ baseline + 10%
- Assert: Total load time ≤ baseline + 10%

**TC24: CoalescingEffectiveness_MeetsThreshold**
- Arrange: Enable metrics on all components
- Act: Simulate normal usage patterns for 5 minutes
- Assert: Coalescing ratio (executed/requested) ≥ 0.3 (70% coalescing)
- Assert: No component has coalescing ratio < 0.1

---

### Level 6: Configuration Tests

**File:** `tests/SwebKit.Core.Tests/Configuration/RenderCoalescingOptionsTests.cs`

#### Test Cases

**TC25: Configuration_DefaultValues_LoadCorrectly**
- Arrange: Load configuration from appsettings
- Act: Read `RenderCoalescingOptions`
- Assert: `DefaultDebounceMs` = 75
- Assert: `ComponentOverrides` is empty or has expected values

**TC26: Configuration_ComponentOverrides_AppliedCorrectly**
- Arrange: Configure DashboardPage debounce to 100ms
- Act: Create DashboardPage and call `GetCoalescingDebounce()`
- Assert: Returns 100ms (not default 75ms)

**TC27: Configuration_EnvironmentPresets_AppliedCorrectly**
- Arrange: Set environment to "Development" with 50ms default
- Act: Load configuration and read default debounce
- Assert: Returns 50ms (not production 75ms)

**TC28: Configuration_MissingValues_UseFallbacks**
- Arrange: Remove configuration file or set invalid values
- Act: Create component and call `GetCoalescingDebounce()`
- Assert: Returns hardcoded fallback (75ms)
- Assert: No exception thrown

---

## Manual Validation Scenarios

### MV1: Dashboard Rapid Updates
**Steps:**
1. Open SwebKit dashboard
2. Generate 20 rapid activity events (via demo mode or real activity)
3. Observe dashboard rendering behavior
4. Check that UI updates smoothly without flickering
5. Verify final state includes all 20 events

**Expected:** Smooth rendering with coalesced updates, no flicker, correct final state

### MV2: AKS Tab Switching
**Steps:**
1. Open AKS page with multiple resource tabs
2. Load data for Pods, Deployments, Services
3. Switch between tabs rapidly
4. Observe grid behavior and scroll position
5. Check browser DevTools for virtualization observer recreation

**Expected:** Grids remain mounted, no observer recreation, smooth tab switches, scroll position preserved

### MV3: CollectionTree Filtering
**Steps:**
1. Open API client with large collection tree (100+ collections)
2. Type rapidly in search filter
3. Observe tree rendering behavior
4. Check that tree updates responsively without lag
5. Verify virtualization still works during filtering

**Expected:** Responsive filtering, smooth scrolling, no performance degradation

### MV4: Production Metrics Review
**Steps:**
1. Deploy to staging environment
2. Enable render metrics collection
3. Simulate normal user load for 1 hour
4. Review coalescing effectiveness metrics
5. Check for components with low coalescing ratios

**Expected:** Most components show ≥ 30% coalescing, no components with < 10% coalescing, no performance warnings

---

## Test Traceability

| Test ID | Wave | Component | Test Type | Priority |
| ------- | ---- | --------- | --------- | -------- |
| TC1-TC8 | A | SwebKitComponentBase | Unit | High |
| TC9-TC13 | B | DashboardPage | Integration | High |
| TC14-TC17 | C | CollectionTree | Integration | High |
| TC18-TC20 | - | AksPage | Integration | Medium |
| TC21-TC24 | F | All Components | Performance | High |
| TC25-TC28 | D | Configuration | Unit | Medium |
| MV1-MV4 | F | All Components | Manual | High |

---

## Success Criteria

- All unit tests pass (TC1-TC8, TC25-TC28)
- All integration tests pass (TC9-TC20)
- Performance regression tests show no degradation (TC21-TC23)
- Coalescing effectiveness meets threshold (TC24)
- Manual validation scenarios pass (MV1-MV4)
- No regressions in existing functionality
- Render coalescing ratio ≥ 30% across all components
