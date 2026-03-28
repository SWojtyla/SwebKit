# Decisions — Performance v2: Blazing Fast UI

---

title: "Decisions — Performance v2: Blazing Fast UI"
owner: ""
status: "Planned"

---

## Decision 001 — Render-batching timer for log streaming

**Status:** Accepted

**Date:** 2026-03-27

### Context

AKS log streaming (PERF2-1, PERF2-2) calls `StateHasChanged` per incoming line or every 20 lines. At high log volume (50+ lines/sec), this causes render flooding that freezes the UI.

Two approaches were considered:

1. **Per-line throttle:** Keep the current per-line model but add `Task.Delay` between calls — already partially in place, insufficient
2. **Render-batching timer:** Accumulate lines in a buffer; a 100ms timer flushes the buffer and calls `StateHasChanged` once per tick

### Decision

Use the **render-batching timer** (100ms window). Lines accumulate in a `ConcurrentQueue<T>` or `List<T>` buffer. A `PeriodicTimer(TimeSpan.FromMilliseconds(100))` drains the buffer into the display list and triggers a single `InvokeAsync(StateHasChanged)`.

### Consequences

- Renders capped at ~10/sec regardless of log input rate
- Maximum visual latency of 100ms for new log lines (imperceptible for humans)
- Timer must be disposed in `DisposeAsync` to prevent orphaned callbacks
- If the pattern proves stable, extract a reusable `RenderBatcher` helper

### Alternatives considered

- **Per-line throttle with longer delay (500ms):** Too visible — users would notice delayed log output
- **Reactive (System.Reactive) debounce:** Adds a dependency; overkill for this use case

---

## Decision 002 — Virtualize log lines vs capped visible window

**Status:** Accepted

**Date:** 2026-03-27

### Context

Log views (PERF2-3) render all lines as DOM elements. With 500+ lines, every render reconciles the full list. Two approaches:

1. **Blazor `Virtualize<T>`:** Only renders visible items (~30–50), supports full scroll-back
2. **Capped visible window (last-N):** Keep only the last 200 lines in the display list, full log stored separately

### Decision

Use **Blazor `Virtualize<T>`** for log line rendering. This preserves scroll-back capability (users can scroll up to see earlier logs) while keeping DOM node count low.

### Consequences

- Requires fixed item height — enforce via monospace font and consistent line-height CSS
- `Virtualize` works with `ICollection<T>` — the log line list must be indexable
- Scroll-to-bottom behavior needs explicit implementation: detect when user is at bottom, auto-scroll on new items; if user scrolled up, suppress auto-scroll
- Must validate that `Virtualize` works correctly in the MAUI Blazor Hybrid WebView (no known issues on .NET 10, but MAUI WebView has historically had quirks)

### Alternatives considered

- **Capped window (last-N):** Simpler but loses scroll-back; users frequently want to search earlier log output
- **Canvas-based rendering:** Too complex, breaks text selection and accessibility

---

## Decision 003 — Interlocked CTS swap vs lock-based synchronization

**Status:** Accepted

**Date:** 2026-03-27

### Context

CancellationTokenSource replacement (PERF2-4, PERF2-10) has race conditions when `_cts` is accessed from multiple contexts (UI thread, background streaming tasks, dispose). Two approaches:

1. **`Interlocked.Exchange`:** Lock-free atomic swap of the CTS reference
2. **`lock` statement:** Traditional synchronization around CTS access

### Decision

Use **`Interlocked.Exchange`** for CTS replacement:

```csharp
var newCts = new CancellationTokenSource();
var oldCts = Interlocked.Exchange(ref _cts, newCts);
oldCts?.Cancel();
oldCts?.Dispose();
// Use newCts.Token for the new operation
```

In `DisposeAsync`:

```csharp
var cts = Interlocked.Exchange(ref _cts, null);
cts?.Cancel();
cts?.Dispose();
```

### Consequences

- No lock contention — safe from deadlocks in async contexts
- Pattern is well-established in .NET for CTS lifecycle management
- `_cts` field must be declared as `CancellationTokenSource?` (nullable)
- All reads of `_cts` must go through a local variable to avoid TOCTOU races:
  ```csharp
  var token = _cts?.Token ?? CancellationToken.None;
  ```

### Alternatives considered

- **Lock-based:** Works but introduces deadlock risk when mixing with async/await and Blazor's synchronization context
- **SemaphoreSlim:** Overkill — we only need atomic reference swap, not mutual exclusion of a code region
