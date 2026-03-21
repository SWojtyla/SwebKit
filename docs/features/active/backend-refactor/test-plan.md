# Backend Refactor — Test Plan

## Scope

Add missing unit test coverage for all Azure, Kubernetes, Redis, and DevOps client implementations. Verify bug fixes do not regress existing behavior.

---

## 1. Existing tests — must stay green

Run full test suite (`dotnet test`) after every phase. No test may be removed or skipped.

---

## 2. New unit tests — by service

### 2.1 `AzureServiceBusClient`

**Project:** `SwebKit.Azure.Tests`

| Method | Scenario | Assert |
|--------|----------|--------|
| `PeekMessagesAsync` | Messages returned | Body, MessageId, EnqueuedTime mapped correctly |
| `PeekMessagesAsync` | Empty queue | Returns empty list |
| `PeekDeadLetterAsync` | DLQ messages returned | `DeadLetterReason` mapped |
| `SendMessageAsync` | No schedule | `ScheduledEnqueueTime` not set |
| `SendMessageAsync` | Scheduled | `ScheduledEnqueueTime` matches input |
| `ResubmitDeadLetterAsync` | Happy path | DLQ receiver receives + completes; sender sends |
| Entity path `"myqueue"` | No slash | Treated as queue, no split |
| Entity path `"topic/sub"` | One slash | `topicName="topic"`, `subName="sub"` |
| Entity path `"a/b/c"` | Two slashes | Throws `ArgumentException` |

### 2.2 `AppInsightsObservabilityProvider`

**Project:** `SwebKit.Azure.Tests`

| Method | Scenario | Assert |
|--------|----------|--------|
| `QueryLogsAsync` | Default (no custom KQL) | Query contains `union traces, exceptions` |
| `QueryLogsAsync` | Custom KQL appended | User KQL appears in final query |
| `QueryLogsAsync` | TimeRange last 1h | Start/end timestamps correct |
| `GetTraceAsync` | Two spans, one root | Root identified by no parent span ID |
| `GetTraceAsync` | Child span links parent | `ParentSpanId` set correctly |
| `TestConnectionAsync` | API succeeds | Returns `true`, `IsConnected = true` |
| `TestConnectionAsync` | API throws | Returns `false`, `IsConnected = false`, no throw |
| Thread-safety | `IsConnected` read concurrently | No torn read |

### 2.3 `KubernetesAksClient`

**Project:** `SwebKit.Kubernetes.Tests`

| Method | Scenario | Assert |
|--------|----------|--------|
| Helm parsing | Valid base64+gzip secret | Release, chart, version parsed |
| Helm parsing | Malformed data | Returns `null`, no throw |
| Instance process registry | Register + dispose client | Process killed |
| Instance process registry | Two clients | Separate dictionaries, no cross-contamination |
| Instance process registry | Kill already-exited process | No exception thrown |

### 2.4 `RedisClient`

**Project:** `SwebKit.Redis.Tests` (new)

| Method | Scenario | Assert |
|--------|----------|--------|
| `ScanKeysAsync` | Pattern `"user:*"` | Matching keys returned |
| `GetStringAsync` | Existing key | Correct value |
| `GetStringAsync` | Missing key | Returns `null` |
| `GetHashAsync` | Hash type | Dictionary of fields |
| `SetStringAsync` | No TTL | Key exists, no expiry |
| `SetStringAsync` | With TTL | Key has expected TTL |
| `TryGetMemoryUsageAsync` | Command fails | Returns `null`, no throw, warning logged |
| `DeleteAsync` | Multiple keys | All deleted |

### 2.5 `DevOpsClient`

**Project:** `SwebKit.DevOps.Tests` (new)

| Method | Scenario | Assert |
|--------|----------|--------|
| `GetWaitingStagesAsync` | Stage in `waiting` state | Returned in list |
| `GetWaitingStagesAsync` | No waiting stages | Returns empty list |
| `GetWaitingStagesAsync` | Approvals API 404 | Returns partial result, no throw |
| `GetWaitingStagesAsync` | HTTP error | Returns empty list, warning logged |
| `ApproveStageAsync` | Success | PATCH called with correct approval body |
| `GetRunStagesAsync` | Exception | Returns empty list, no throw |

### 2.6 `AppEventBus`

**Project:** `SwebKit.Core.Tests`

| Scenario | Assert |
|----------|--------|
| Subscriber throws | Remaining subscribers still invoked |
| Throwing subscriber | Error logged at `Error` level |
| No subscribers | No exception |

### 2.7 `TaskQueueService`

**Project:** `SwebKit.Core.Tests`

| Scenario | Assert |
|----------|--------|
| Task completes | Removed from list after 5s delay |
| `TasksChanged` fires | Invoked once after removal |
| Running task | Not removed by cleanup |

---

## 3. Shared test infrastructure

### `FakeCredentialStore`

Moved to `tests/SwebKit.TestUtilities/`. Referenced by all test projects. Eliminates 4+ duplications.

### `MockHttpMessageHandler`

Used by `DevOpsClient` tests. Returns pre-canned JSON responses for specific request URLs.

---

## 4. Manual / integration checks (per phase)

### Phase 1 — Bug fixes

- [ ] Port-forward a pod in AKS — verify session starts and stops cleanly
- [ ] Start two simultaneous port-forwards from two environments — verify separate process tracking
- [ ] Test connection to App Insights from two threads (unlikely in practice, but no crash)

### Phase 2 — Error handling

- [ ] Disconnect Redis mid-session — verify warning in Serilog log, no crash
- [ ] Use invalid DevOps PAT — verify warning logged, empty pipeline list shown
- [ ] Publish event with throwing subscriber — verify other subscribers still fire

### Phase 3 — Tests

- [ ] `dotnet test` passes all new tests

### Phase 4 — Config validation

- [ ] Save empty `WorkspaceId` in Observability config — verify immediate error message, not deferred crash

---

## 5. Acceptance criteria

- All existing tests pass
- All new unit tests pass (`dotnet test`)
- No port-forward process leaks after client disposal
- Swallowed exceptions appear in Serilog output at `Warning` or `Error` level
- `dotnet build` with no warnings (no new warnings introduced)
