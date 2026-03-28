# Test Plan — AKS Pod Health Monitor

---

title: "Test Plan — AKS Pod Health Monitor"
owner: ""
status: "Not started"
created: "2026-03-26"
updated: "2026-03-26"

---

## Goal

Validate that pod health monitoring correctly detects pod failures, triggers notifications without spamming, and that UI components display monitoring state and alerts accurately.

## Scope

- **In scope:** pod state diffing logic, notification deduplication, monitoring service lifecycle, UI component rendering and interaction, end-to-end mock flows
- **Out of scope:** actual Windows toast rendering (WinRT API is platform-specific; mock at interface boundary), real AKS cluster integration, performance/load testing

## Main scenarios (priority)

### P0 — Critical

1. **Pod phase transition detection** — Pod transitions from Running → Failed → service emits `PodFailed` event
2. **CrashLoopBackOff detection** — Pod restart count increases → service emits `PodCrashLoop` event
3. **Pod termination detection** — Pod disappears from namespace → service emits `PodTerminated` event
4. **Notification deduplication** — Same pod failure within cooldown window → second alert suppressed
5. **Monitoring survives navigation** — Start monitoring on AKS page → navigate to Service Bus → return to AKS → monitoring still active

### P1 — Important

6. **Container not-ready detection** — Pod was fully ready → container becomes not-ready → `ContainerNotReady` event
7. **Service start/stop lifecycle** — `StartAsync` begins polling; `StopAsync` cancels cleanly; no exceptions leak
8. **Config persistence** — Add namespaces → save → restart → namespaces restored
9. **Namespace selector displays correctly** — Shows available namespaces, reflects current monitoring state
10. **Alert history panel shows events** — Events appear in order, color-coded, clearable

### P2 — Nice to have

11. **Transient error resilience** — Network error during poll → logged, polling continues next tick
12. **Auth failure handling** — Expired token → warning notification, monitoring pauses gracefully
13. **Multiple namespaces** — Monitor 3 namespaces → failure in namespace B → correctly attributed alert

## Automated coverage

### Unit tests — Service layer

**Location:** `tests/SwebKit.App.Tests/` (or `tests/SwebKit.Core.Tests/` for pure model/logic tests)

| Test                                            | What it validates                                                            |
| ----------------------------------------------- | ---------------------------------------------------------------------------- |
| `PodStateDiffing_RunningToFailed_EmitsEvent`    | Phase transition Running → Failed produces `PodFailed`                       |
| `PodStateDiffing_RunningToCrashLoop_EmitsEvent` | Restart count increase produces `PodCrashLoop`                               |
| `PodStateDiffing_PodDisappears_EmitsTerminated` | Pod in previous snapshot missing from current → `PodTerminated`              |
| `PodStateDiffing_ContainerNotReady_EmitsEvent`  | Ready < Total (was previously equal) → `ContainerNotReady`                   |
| `PodStateDiffing_AlreadyFailed_NoEvent`         | Pod already Failed at first snapshot → no alert on initial poll              |
| `PodStateDiffing_NewPodRunning_NoEvent`         | New pod appears in Running state → no alert                                  |
| `PodStateDiffing_PodUnknown_EmitsEvent`         | Phase → Unknown → `PodUnknown`                                               |
| `Cooldown_SamePodSameEvent_Suppressed`          | Duplicate event within 10 min window → suppressed                            |
| `Cooldown_SamePodDiffEvent_NotSuppressed`       | Same pod, different event type → not suppressed                              |
| `Cooldown_Expired_AlertsAgain`                  | Same event after cooldown expires → fires again                              |
| `Monitor_StartStop_Lifecycle`                   | `StartAsync` sets `IsMonitoring=true`; `StopAsync` sets false; no exceptions |
| `Monitor_DoubleStart_NoOp`                      | Calling `StartAsync` twice doesn't create duplicate timers                   |
| `Monitor_AddRemoveNamespace`                    | Add/remove correctly updates `MonitoredNamespaces`                           |
| `Monitor_TransientError_ContinuesPolling`       | `GetPodsAsync` throws → next tick still fires                                |

### Component tests — UI

**Location:** `tests/SwebKit.App.Tests/`

| Test                                | What it validates                                          |
| ----------------------------------- | ---------------------------------------------------------- |
| `NamespaceSelector_LoadsNamespaces` | Component renders namespace list from mock `IAksClient`    |
| `NamespaceSelector_StartMonitoring` | Clicking Start calls `IPodHealthMonitorService.StartAsync` |
| `NamespaceSelector_StopMonitoring`  | Clicking Stop calls `StopAsync`                            |
| `StatusIndicator_ShowsActive`       | When `IsMonitoring == true` → green indicator renders      |
| `StatusIndicator_ShowsInactive`     | When `IsMonitoring == false` → gray indicator renders      |
| `AlertHistory_ShowsEvents`          | Events pushed via bus → appear in list                     |
| `AlertHistory_ClearHistory`         | Clear button removes all events from display               |

### Integration test scenarios

| Test                                        | What it validates                                                                                                  |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `FullFlow_PodFails_NotificationTriggered`   | Mock `IAksClient` returns healthy pods → then unhealthy → verify `IWindowsNotificationService.ShowPodAlert` called |
| `FullFlow_MultiplePodFailures_Deduplicated` | Two consecutive polls with same failure → only one notification                                                    |
| `FullFlow_NamespacePersistence_Roundtrip`   | Add namespaces → save config → new service instance → namespaces restored                                          |

## Test data and setup

### Mock services

All tests use mock implementations registered in bUnit / test DI:

- `MockAksClient` — returns configurable `List<PodInfo>` per namespace
- `MockWindowsNotificationService` — records `ShowPodAlert` calls for assertion
- `MockNotificationService` — existing test mock
- `MockAppEventBus` — existing test mock; capture published events
- `MockAppStateService` — for config access

### Test fixtures

- **Healthy pod set:** 3 pods in `Running` phase, all containers ready
- **Mixed pod set:** 2 Running, 1 Failed
- **CrashLoop pod set:** 1 pod with increasing restart count
- **Empty namespace:** 0 pods

### Pod factory

```csharp
static PodInfo CreatePod(string name, string ns = "default",
    string phase = "Running", int ready = 1, int total = 1,
    int restarts = 0)
    => new(name, ns, phase, ready, total, restarts, ...);
```

## Manual checks

1. **Toast notification appearance** — Kill a pod via `kubectl delete pod` in a monitored namespace → verify Windows toast appears with correct pod name, namespace, and cluster info within ~2 minutes
2. **Toast click navigation** — Click the toast → app foregrounds to AKS page
3. **Monitoring persistence** — Enable monitoring for 2 namespaces → close and reopen app → verify monitoring resumes automatically
4. **Background behavior** — Navigate to Service Bus page → kill a pod → verify toast still appears
5. **Cooldown behavior** — Kill the same pod twice quickly → verify only one toast within 10 minutes

## Regression risks & mitigations

| Risk                                                          | Mitigation                                                                    |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| New singleton service affects app startup time                | Test startup with monitoring disabled; measure init overhead                  |
| Event bus events from timer thread cause UI thread issues     | Test all UI components subscribe via `InvokeAsync` pattern                    |
| Config model extension breaks existing config deserialization | Test round-trip with configs that lack the new field (default value handling) |

## Acceptance criteria

- All P0 and P1 automated tests pass
- Windows toast notifications appear for pod failures within 2-minute window
- Monitoring continues across page navigation
- No notification spam — deduplication works for same pod/event within cooldown
- Config persists across app restarts
- Zero regressions in existing AKS page functionality

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
