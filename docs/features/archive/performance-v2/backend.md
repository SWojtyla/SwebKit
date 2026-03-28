# Backend Plan — Performance v2: Blazing Fast UI

---

title: "Backend Plan — Performance v2: Blazing Fast UI"
owner: ""
status: "Not started"

---

## Goal

Fix channel completion and stream failure handling in `KubernetesAksClient` to eliminate UI freezes caused by backend streaming issues. These are the only backend changes in this feature.

## Impacted areas

- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`

## Design

The multi-pod log streaming implementation uses `System.Threading.Channels.Channel<T>` with fan-out tasks (one per pod). Two bugs exist:

1. **PERF2-5 — Channel writer never completes on failure:** If any fan-out task throws, the `ContinueWith` that calls `writer.TryComplete()` may not fire. The reader side (`ReadAllAsync`) blocks indefinitely.

2. **PERF2-12 — OperationCanceledException silently consumed:** When a pod stream is cancelled (e.g., pod terminated or user navigated away), the exception is caught but the channel is not signaled. The reader cannot distinguish "stream ended" from "stream failed".

### Target architecture

```
Fan-out task per pod:
  try {
    stream pod logs → write to channel
  } catch (OperationCanceledException) when not overall-cancelled {
    log warning (pod stream ended)
  } catch (Exception ex) {
    log error
  } finally {
    if (Interlocked.Decrement(ref remainingCount) == 0)
      writer.TryComplete()
  }
```

Key properties:

- Every code path reaches the `finally` block
- Channel writer is completed exactly once, when the last task finishes
- OperationCanceledException is re-thrown only if the overall CancellationToken is cancelled (per pitfall CS-2)
- Individual pod failures do not block other pods

## Contracts

No API or contract changes. The `Channel<LogLine>` reader interface remains unchanged. The fix is internal to the streaming implementation.

## Tasks

### PERF2-5: Fix channel completion hang `[dotnet-expert]`

**File:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` lines 947–955

- [ ] Replace fire-and-forget `ContinueWith` with structured fan-out:
  - Initialize `int remainingCount = fanOutTasks.Count`
  - Wrap each fan-out task body in try/finally
  - In finally: `if (Interlocked.Decrement(ref remainingCount) == 0) writer.TryComplete()`
- [ ] Ensure `writer.TryComplete()` is called even when all tasks are cancelled
- [ ] Add structured logging for each task completion/failure
- [ ] Add unit test: simulate one pod throwing — verify channel completes and reader finishes

### PERF2-12: Fix silent pod stream failures `[dotnet-expert]`

**File:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` lines 948–950

- [ ] In the catch block for `OperationCanceledException`:
  - Check if the overall `CancellationToken.IsCancellationRequested` — if yes, re-throw (CS-2)
  - If only the pod-level stream ended, log a warning and continue (decrement count)
- [ ] In the catch block for generic `Exception`:
  - Log the error with pod name context
  - Decrement count (do not re-throw — one pod failure should not kill all streams)
- [ ] Add unit test: cancel one pod stream → verify other pods continue streaming
- [ ] Add unit test: cancel overall token → verify all tasks exit cleanly

## Validation

- Unit tests: Not started
- Integration tests: Not started (requires Kubernetes test cluster; manual verification acceptable)
- Manual checks:
  - Open multi-pod log view → kill one pod → verify remaining pods continue streaming
  - Open multi-pod log view → navigate away → verify no orphaned tasks or channel hangs

## Notes

- PERF2-5 and PERF2-12 are tightly coupled — implement together
- The Interlocked countdown pattern avoids the need for locks and is safe across async contexts
- These changes are part of Wave 0 because channel hangs directly cause the most severe UI freezes
