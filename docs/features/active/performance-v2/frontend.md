# Frontend Plan — Performance v2: Blazing Fast UI

---

title: "Frontend Plan — Performance v2: Blazing Fast UI"
owner: ""
status: "Not started"

---

## Goal

Eliminate UI freezes, render flooding, and cancellation races in the Blazor Hybrid frontend. Make AKS log viewing smooth at 10k+ lines, migrate all StateHasChanged calls to safe InvokeAsync dispatch, and add virtualization where large DOM node counts cause jank.

## Impacted areas

- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- `src/SwebKit.App/Components/Redis/RedisKeyList.razor`
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor`
- `src/SwebKit.App/Components/Aks/ContainerDetailPanel.razor`

## Referenced pitfalls

- **BL-2** (`docs/pitfalls/blazor-maui.md`): StateHasChanged must use InvokeAsync after await
- **BL-3** (`docs/pitfalls/blazor-maui.md`): Set guard state before await
- **CS-2** (`docs/pitfalls/dotnet-csharp.md`): catch(Exception) swallows OperationCanceledException

---

## Wave 0 — AKS Log Freeze Fix (CRITICAL)

The AKS pod log viewers are the primary source of user-reported UI freezes. These 5 items are the highest-priority changes.

### PERF2-1: Batch StateHasChanged in MultiPodLogView `[blazor-expert]`

**Problem:** `MultiPodLogView.razor` lines 135–165 call `await InvokeAsync(StateHasChanged)` on every incoming log line. High-volume pods emit 50+ lines/second, triggering 50+ renders/second. The existing `Task.Delay(50)` only throttles output; the input queue still grows unboundedly.

**Files:** `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` lines 135–165

**Approach:**

1. Replace per-line StateHasChanged with a render-batching timer (100ms window)
2. Accumulate incoming lines in a buffer; on timer tick, flush buffer to the display list and call `InvokeAsync(StateHasChanged)` once
3. Use `System.Threading.Timer` or `PeriodicTimer` with a 100ms interval
4. On dispose, flush remaining buffer and stop the timer
5. Remove the per-line `Task.Delay(50)` — the timer replaces it

**Expected impact:** Reduces renders from 50+/sec to ~10/sec. Eliminates the primary cause of UI freezing during log streaming.

---

### PERF2-2: Batch StateHasChanged in PodLogView `[blazor-expert]`

**Problem:** `PodLogView.razor` lines 126–137 call StateHasChanged every 20 lines and invoke JS `scrollToBottom` per render cycle. At high log volume this is still too frequent, and each render triggers expensive DOM reconciliation across all log line elements.

**Files:** `src/SwebKit.App/Components/Aks/PodLogView.razor` lines 126–137

**Approach:**

1. Same render-batching timer pattern as PERF2-1 (100ms window)
2. Move JS `scrollToBottom` call into the timer callback — call it once per batch, not per render
3. Track whether user has scrolled up manually; if so, suppress auto-scroll until they scroll back to bottom

**Expected impact:** Reduces render frequency and eliminates JS interop overhead during fast streaming.

---

### PERF2-3: Virtualize log display `[blazor-expert]`

**Problem:** Both `PodLogView.razor` (lines 46–54) and `MultiPodLogView.razor` (line 47) use `@foreach` to render ALL log lines as individual `<div>` elements. With 500+ lines, every StateHasChanged reconciles 500+ DOM nodes. `GetLineClass()` runs per line per render doing O(n) string searches.

**Files:**

- `src/SwebKit.App/Components/Aks/PodLogView.razor` lines 46–54
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` line 47

**Approach — Option A (Virtualize\<T>):**

1. Replace `@foreach` with Blazor's built-in `<Virtualize Items="@FilteredLines" Context="line">` component
2. Only visible lines (~30–50) are rendered in the DOM at any time
3. Requires a fixed item height (use monospace font, consistent line height)
4. Move `GetLineClass()` result into a precomputed property on the log line model to avoid per-render string searches

**Approach — Option B (Capped visible window):**

1. Keep a `_visibleLines` list capped at last N lines (e.g., 200)
2. Full log stored separately for search/export
3. Simpler but loses scroll-back capability

**Decision:** See `decisions.md` Decision 002. Recommend Option A (Virtualize) for full scroll-back support.

**Expected impact:** Reduces DOM node count from 500+ to ~40. Eliminates O(n) reconciliation on each render.

---

### PERF2-4: Fix CTS null-reference race in PodLogView `[blazor-expert]`

**Problem:** `PodLogView.razor` lines 96–111 — the CancellationTokenSource is cancelled and disposed without null-safety. Rapid navigation (open logs → navigate → open again) can cause:

- `_cts!.Token` accessed after dispose → ObjectDisposedException
- `_cts` set to null between check and use → NullReferenceException
- Thread race: cancel → dispose → new CTS → use old disposed token

**Files:** `src/SwebKit.App/Components/Aks/PodLogView.razor` lines 96–111

**Approach:**

1. Use the Interlocked CTS swap pattern (see Decision 003):
   ```csharp
   var newCts = new CancellationTokenSource();
   var oldCts = Interlocked.Exchange(ref _cts, newCts);
   oldCts?.Cancel();
   oldCts?.Dispose();
   ```
2. Always read `_cts` into a local variable before accessing `.Token`
3. In `DisposeAsync`, use `Interlocked.Exchange(ref _cts, null)` to prevent post-dispose access

**Expected impact:** Eliminates NullReferenceException and ObjectDisposedException during rapid navigation.

---

### PERF2-5: Fix channel completion hang in KubernetesAksClient `[dotnet-expert]`

**Problem:** `KubernetesAksClient.cs` lines 947–955 — multi-pod log streaming uses `Task.WhenAll(fanOutTasks).ContinueWith(...)` as fire-and-forget. If any fanOutTask throws before completing, the channel writer is never completed. The reader blocks indefinitely on `ReadAllAsync(ct)` → UI freeze.

**Files:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` lines 947–955

**Approach:**

1. Wrap each fan-out task in try/finally that signals completion
2. Use a countdown (e.g., `Interlocked.Decrement` on remaining count) — when all tasks finish (success or failure), call `writer.TryComplete()`
3. Catch exceptions in each task and log them; do not let one failed pod block the channel
4. Ensure writer.TryComplete() is called even on cancellation (CS-2 pitfall)

**Expected impact:** Eliminates indefinite UI freeze when any single pod stream fails.

---

## Wave 1 — Async Correctness & Safety

Fixes that prevent crashes, silent failures, and undefined behavior.

### PERF2-6: Fix async void in RedisPage `[blazor-expert]`

**Problem:** `RedisPage.razor` line 933 — `private async void OnCacheNameChanged(...)`. Unhandled exceptions in async void crash the entire MAUI app with no error handling opportunity.

**Files:** `src/SwebKit.App/Components/Pages/RedisPage.razor` line 933

**Approach:**

1. Change signature from `async void` to `async Task`
2. If the method is used as an event handler delegate that requires `void`, wrap the body in try/catch and log errors
3. Verify all callers — if it's bound to an `EventCallback<string>`, Blazor natively supports `async Task` return

**Expected impact:** Prevents silent app crashes from Redis cache name changes.

---

### PERF2-7: Migrate StateHasChanged to InvokeAsync across all pages `[blazor-expert]`

**Problem:** Multiple pages call bare `StateHasChanged()` after async operations, violating pitfall BL-2. This causes silent render failures — the UI appears stuck even though data has loaded.

**Files and locations:**

- `ServiceBusPage.razor`: lines 292, 298, 432, 542, 593, 596, 618, 625, 634, 648, 656, 670, 684, 690, 698, 770
- `PipelinesPage.razor`: lines 343, 350, 358
- `AksPage.razor`: lines 69, 79, 152, 160 (and inline onclick handlers)

**Approach:**

1. Grep all `.razor` files for bare `StateHasChanged()` calls
2. For each occurrence: determine if it follows an `await` — if yes, wrap in `await InvokeAsync(StateHasChanged)`
3. For synchronous event handlers (onclick without await), bare `StateHasChanged()` is safe — leave as-is
4. Validate each page's loading/error/data states after migration

**Expected impact:** Fixes silent UI stalls where data loaded but the render was not dispatched to the correct thread.

---

### PERF2-10: Fix rapid CTS replacement race in AksPage `[blazor-expert]`

**Problem:** `AksPage.razor` lines 1259–1262 — `_cts` is replaced without synchronization. If `LoadDataAsync` is called rapidly (e.g., switching between AKS clusters), the old CTS may be disposed while still in use, causing `ObjectDisposedException`.

**Files:** `src/SwebKit.App/Components/Pages/AksPage.razor` lines 1259–1262

**Approach:** Same Interlocked CTS swap pattern as PERF2-4.

**Expected impact:** Eliminates ObjectDisposedException during rapid AKS cluster switching.

---

### PERF2-12: Fix silent pod stream failures in KubernetesAksClient `[dotnet-expert]`

**Problem:** `KubernetesAksClient.cs` lines 948–950 — `OperationCanceledException` is caught in the fan-out loop but the channel is not properly signaled. This violates pitfall CS-2. A cancelled pod stream silently stops without the reader knowing.

**Files:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` lines 948–950

**Approach:**

1. In the catch block for `OperationCanceledException`, re-throw if the overall cancellation token is cancelled (propagate cancellation correctly)
2. If only the individual pod's stream was cancelled (e.g., pod terminated), log a warning and decrement the fan-out count
3. Ensure the channel writer is completed when all tasks finish regardless of outcome (ties into PERF2-5)

**Expected impact:** Prevents silent stream failures from leaving the UI in an indeterminate state.

---

## Wave 2 — Render Optimization

Performance polish — measurable improvements but lower severity than Waves 0–1.

### PERF2-8: Batch AksPage incremental loading renders `[blazor-expert]`

**Problem:** `AksPage.razor` lines 1268–1277 — each of 11 datasets fires `InvokeAsync(StateHasChanged)` individually in the `LoadDataset<T>` pattern. Each call triggers a full page re-render. 11 sequential renders on page load.

**Files:** `src/SwebKit.App/Components/Pages/AksPage.razor` lines 1268–1277

**Approach:**

1. Collect dataset results and batch StateHasChanged — call once after all 11 datasets finish, or at most once per 100ms during incremental loading
2. Use a simple debounce: set a `_renderPending` flag in each `LoadDataset` callback; a single timer checks the flag and renders

**Expected impact:** Reduces 11 sequential re-renders to 2–3 batched renders.

---

### PERF2-9: Cache FilteredLines computation `[blazor-expert]`

**Problem:** `MultiPodLogView.razor` lines 95–105 and `PodLogView.razor` lines 79–81 — `FilteredLines` property runs `OrderBy(...).TakeLast(500).ToList()` on every render access. This is O(n log n) per render with a new list allocation each time.

**Files:**

- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` lines 95–105
- `src/SwebKit.App/Components/Aks/PodLogView.razor` lines 79–81

**Approach:**

1. Cache the filtered result in a `_cachedFilteredLines` field
2. Invalidate the cache only when new lines are added or the filter text changes (set a `_filterDirty` flag)
3. On render, return cached result if not dirty; recompute only when dirty

**Expected impact:** Eliminates O(n log n) sort + allocation from every render cycle.

---

### PERF2-11: Add @key directives to repeated elements `[blazor-expert]`

**Problem:** Multiple components use `@foreach` without `@key`, forcing Blazor to diff the entire list on every render instead of tracking identity.

**Files:**

- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor` lines 29, 59, 67 (queues, topics, subscriptions)
- `src/SwebKit.App/Components/Redis/RedisKeyList.razor` line 23
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor` line 23
- `src/SwebKit.App/Components/Aks/PodLogView.razor` — log line elements
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` — log line elements

**Approach:**

1. Add `@key="item.Name"` (or equivalent unique identifier) to each `@foreach` loop element
2. For log lines, use a line index or a unique line ID if available
3. If Virtualize is adopted in PERF2-3, @key is handled natively — only apply to non-virtualized lists

**Expected impact:** Faster list diffing — Blazor can match by key instead of index, reducing unnecessary DOM mutations.

---

### PERF2-13: Show loading state immediately `[blazor-expert]`

**Problem:** In `AksPage`, `MultiPodLogView`, `PodLogView`, and `ContainerDetailPanel`, `_loading = true` is set but `StateHasChanged` is not called before the first `await`. The UI doesn't show a spinner until the next render cycle — it appears frozen.

**Files:**

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor`
- `src/SwebKit.App/Components/Aks/PodLogView.razor`
- `src/SwebKit.App/Components/Aks/ContainerDetailPanel.razor`

**Approach:**

1. After setting `_loading = true`, call `await InvokeAsync(StateHasChanged)` before the first async operation
2. Follow BL-3 pitfall: set guard state before await
3. Pattern:
   ```csharp
   _loading = true;
   await InvokeAsync(StateHasChanged); // show spinner immediately
   await LoadDataAsync(ct);
   _loading = false;
   await InvokeAsync(StateHasChanged);
   ```

**Expected impact:** Loading spinners appear immediately instead of after a visible delay.

---

### PERF2-14: Virtualize EntityTree for large namespaces `[blazor-expert]`

**Problem:** `EntityTree.razor` line 29 — `@foreach` renders all queues/topics when a Service Bus namespace has 500+ entities. Full DOM render and reconciliation on every expand/collapse.

**Files:** `src/SwebKit.App/Components/ServiceBus/EntityTree.razor` line 29

**Approach:**

1. Replace `@foreach` with `<Virtualize Items="@Queues">` for the queue list
2. Apply similarly to topics and subscriptions lists
3. Requires a fixed-height container — adjust CSS for the tree panel

**Expected impact:** Reduces DOM nodes from 500+ to ~30 visible items for large namespaces.

---

### PERF2-15: Bound and cleanup \_podColorIndex in MultiPodLogView `[blazor-expert]`

**Problem:** `MultiPodLogView.razor` lines 85, 104–106 — `_podColorIndex` dictionary grows unboundedly. On every render it adds new pod names but never removes stale ones. Over long sessions with pod churn, this dictionary grows without limit.

**Files:** `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` lines 85, 104–106

**Approach:**

1. Reset `_podColorIndex` when the pod selection changes (new streaming session)
2. Cap dictionary size — if it exceeds a threshold (e.g., 100), remove the oldest entries
3. Clean up on `DisposeAsync`

**Expected impact:** Prevents unbounded memory growth during long log viewing sessions.

---

## Tasks (summary)

### Wave 0 `[blazor-expert]` + `[dotnet-expert]` for PERF2-5

- [ ] PERF2-1: Implement render-batching timer in MultiPodLogView
- [ ] PERF2-2: Implement render-batching timer in PodLogView
- [ ] PERF2-3: Replace @foreach with Virtualize (or capped window) in log views
- [ ] PERF2-4: Interlocked CTS swap in PodLogView
- [ ] PERF2-5: Fix channel writer completion in KubernetesAksClient

### Wave 1 `[blazor-expert]` + `[dotnet-expert]` for PERF2-12

- [ ] PERF2-6: Convert async void to async Task in RedisPage
- [ ] PERF2-7: Migrate all bare StateHasChanged to InvokeAsync
- [ ] PERF2-10: Interlocked CTS swap in AksPage
- [ ] PERF2-12: Fix OperationCanceledException handling in fan-out tasks

### Wave 2 `[blazor-expert]`

- [ ] PERF2-8: Batch AksPage dataset loading renders
- [ ] PERF2-9: Cache FilteredLines with dirty-flag invalidation
- [ ] PERF2-11: Add @key directives to all @foreach loops
- [ ] PERF2-13: Immediate loading state rendering
- [ ] PERF2-14: Virtualize EntityTree
- [ ] PERF2-15: Bound \_podColorIndex dictionary

## Validation

- Component tests: Not started
- Manual UX checks: Not started

## Notes

- The render-batching timer pattern (100ms window) should be extracted to a reusable helper if it proves stable in Wave 0 — consider a `RenderBatcher` utility class
- PERF2-3 and PERF2-14 both use Virtualize — validate that it works correctly inside the MAUI Blazor Hybrid WebView (no known issues as of .NET 10)
- Wave 0 can be implemented and validated independently before starting Wave 1
