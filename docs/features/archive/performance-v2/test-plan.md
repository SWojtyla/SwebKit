# Test Plan — Performance v2: Blazing Fast UI

---

title: "Test Plan — Performance v2: Blazing Fast UI"
owner: ""
status: "Not started"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Validate that all 15 performance items are correctly implemented: no UI freezes, no render flooding, no cancellation races, no silent failures. Ensure no regressions in existing page behavior.

## Scope

**In scope:**

- Log viewer performance under high-volume streaming (1000+ lines)
- Cancellation correctness on navigation and rapid re-invocation
- StateHasChanged migration correctness across all pages
- Virtualization behavior for large lists
- Channel completion in KubernetesAksClient

**Out of scope:**

- Backend API latency testing
- Infrastructure or deployment validation
- Cross-platform testing (Windows desktop only)

---

## Main scenarios (priority)

### Wave 0 — AKS Log Freeze

1. **Rapid log streaming renders at bounded rate** — Stream 1000+ log lines in <5 seconds → UI remains responsive, renders at ≤10/sec (PERF2-1, PERF2-2)
2. **Virtualized log view handles large logs** — Display 5000 lines → DOM contains ≤50 elements, scrolling is smooth (PERF2-3)
3. **CTS race doesn't crash** — Open pod logs → navigate away → open again rapidly 10 times → no NullReferenceException, no ObjectDisposedException (PERF2-4)
4. **Channel completes on pod failure** — One pod stream throws → channel reader finishes, UI shows available logs from other pods (PERF2-5)

### Wave 1 — Async Correctness

5. **Async void eliminated** — Change Redis cache name → no app crash, error logged if operation fails (PERF2-6)
6. **StateHasChanged dispatched correctly** — Load ServiceBusPage after await → spinner hides, data appears (PERF2-7)
7. **Rapid AKS cluster switch** — Switch between 3 clusters rapidly → no ObjectDisposedException, final cluster's data loads correctly (PERF2-10)
8. **Cancelled pod streams reported** — Cancel one pod → remaining pods continue, warning logged (PERF2-12)

### Wave 2 — Render Optimization

9. **Batched AksPage renders** — Load AksPage with 11 datasets → ≤3 StateHasChanged calls total during loading (PERF2-8)
10. **Cached FilteredLines** — Render log view 10 times without new lines → O(1) property access, no sort/allocation (PERF2-9)
11. **@key on repeated elements** — Update one queue name in EntityTree → only that element re-renders (PERF2-11)
12. **Immediate loading spinner** — Click load → spinner visible within one frame, before async operation starts (PERF2-13)
13. **Virtualized EntityTree** — Namespace with 500 queues → DOM contains ≤40 queue elements (PERF2-14)
14. **Bounded pod color dictionary** — View 200 different pods across sessions → dictionary size capped (PERF2-15)

---

## Automated coverage

### Unit tests

| Item     | Test file                                        | What to test                                                                  |
| -------- | ------------------------------------------------ | ----------------------------------------------------------------------------- |
| PERF2-1  | `MultiPodLogViewTests.cs`                        | Render count ≤10/sec during rapid line additions                              |
| PERF2-2  | `PodLogViewTests.cs`                             | Render count bounded; JS scrollToBottom called once per batch                 |
| PERF2-4  | `PodLogViewTests.cs`                             | Rapid CTS swap: no exceptions after 100 rapid cancel/recreate cycles          |
| PERF2-5  | `KubernetesAksClientTests.cs`                    | Channel completes when fan-out task throws; reader enumerates available items |
| PERF2-6  | `RedisPageTests.cs`                              | OnCacheNameChanged returns Task; exception is caught and logged               |
| PERF2-9  | `MultiPodLogViewTests.cs` / `PodLogViewTests.cs` | FilteredLines returns cached result on repeated access without new lines      |
| PERF2-10 | `AksPageTests.cs`                                | Rapid LoadDataAsync calls: no ObjectDisposedException                         |
| PERF2-12 | `KubernetesAksClientTests.cs`                    | Cancelled pod stream: remaining pods continue; overall cancel: all exit       |
| PERF2-15 | `MultiPodLogViewTests.cs`                        | Pod color dictionary size stays bounded after many distinct pod names         |

### Component tests (bUnit)

| Item     | Test file                                         | What to test                                                          |
| -------- | ------------------------------------------------- | --------------------------------------------------------------------- |
| PERF2-3  | `PodLogViewTests.cs`                              | Virtualized log view: rendered DOM element count ≤ viewport size      |
| PERF2-7  | `ServiceBusPageTests.cs`, `PipelinesPageTests.cs` | After async load, UI updates correctly (data visible, spinner hidden) |
| PERF2-11 | `EntityTreeTests.cs`                              | List update with @key: only changed element re-renders                |
| PERF2-13 | `AksPageTests.cs`                                 | Loading state: spinner visible before first async yield               |
| PERF2-14 | `EntityTreeTests.cs`                              | 500 queues: DOM contains ≤40 queue elements                           |

### Integration tests

| Item               | Test file                     | What to test                                                                 |
| ------------------ | ----------------------------- | ---------------------------------------------------------------------------- |
| PERF2-5 + PERF2-12 | `KubernetesAksClientTests.cs` | End-to-end multi-pod stream: kill pod mid-stream, verify graceful completion |

---

## Test data and setup

- **Log streaming:** Mock `IKubernetesAksClient` to emit log lines at configurable rates (10/sec, 100/sec, 500/sec)
- **Large lists:** Seed EntityTree with 500+ mock queue entities
- **CTS race:** Use `Task.Run` loops to invoke cancel/recreate at maximum throughput
- **Channel completion:** Mock individual pod streams to throw after N lines

## Manual checks

1. **AKS log freeze (primary):** Open multi-pod log view for a high-traffic deployment (10+ pods, heavy logging) → verify UI remains interactive (can click tabs, scroll, navigate)
2. **Navigation cancellation:** Open AKS pod logs → immediately click to ServiceBus page → click back to AKS → no errors in output, no frozen UI
3. **Large EntityTree:** Connect to a Service Bus namespace with 200+ queues → expand tree → verify smooth scrolling and no jank
4. **Redis page:** Change cache name repeatedly → no crash, no error dialog
5. **ServiceBus page:** Load page with 5+ namespaces → verify all data loads, spinners hide correctly

## Regression risks & mitigations

- **Risk:** StateHasChanged migration (PERF2-7) misses a call site → UI appears stuck on one page — **Mitigation:** Grep for bare `StateHasChanged()` after async context and validate each hit; bUnit tests for all affected pages
- **Risk:** Virtualize component behaves differently in MAUI Blazor Hybrid WebView — **Mitigation:** Manual testing before merging; fallback to capped-window approach if issues found
- **Risk:** Render-batching timer introduces visible lag in log updates — **Mitigation:** Use 100ms window (imperceptible); tunable via constant

## Acceptance criteria

- All Wave 0 items pass automated + manual tests (hard gate)
- All Wave 1 items pass automated tests
- All Wave 2 items pass automated tests
- No regressions in existing test suites (`SwebKit.App.Tests`, `SwebKit.Kubernetes.Tests`)
- No new compiler warnings related to async patterns

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
