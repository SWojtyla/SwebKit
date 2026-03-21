# Test Plan — Demo Mode Overhaul

## Validation strategy

Unit tests for demo client method coverage. Manual full-walkthrough of every feature area in demo mode.

## Unit tests

- `DemoServiceBusClient` implements every method on `IServiceBusClient` without throwing
- `DemoStorageClient` implements every method on `IStorageClient` without throwing
- `DemoReleasesClient` implements every method on `IReleasesClient` without throwing
- `DemoAksClient` implements every method on `IAksClient` without throwing (audit for gaps)
- `DemoRedisClient` implements every method on `IRedisClient` without throwing (audit for gaps)

## Main scenarios

### Demo mode activation UX

| Scenario | Expected |
|---|---|
| User clicks "Demo" button | Confirmation popover shown |
| User confirms | Demo mode activates; amber banner shown |
| User clicks "Disable" on banner | Demo mode deactivates; banner hidden |
| App restarted with demo mode on | Demo mode restored from `UiStateRepository` |

### Demo banner

| Scenario | Expected |
|---|---|
| Demo mode active | Full-width amber banner visible below top bar on all pages |
| Demo mode inactive | No banner; top bar is normal |

### Feature coverage — Service Bus

| Scenario | Expected |
|---|---|
| Navigate to Service Bus in demo mode | Synthetic namespaces and entities shown |
| Peek messages | Demo messages returned (varied bodies, properties) |
| DLQ view | Pre-populated with demo dead-letter messages |
| Scheduled messages | 2–3 demo scheduled messages shown |

### Feature coverage — AKS

| Scenario | Expected |
|---|---|
| Navigate to AKS in demo mode | Demo cluster, namespaces, deployments, pods shown |
| All resource tabs | Data present (StatefulSets, ConfigMaps, Secrets, Ingresses, CronJobs, Helm) |

### Feature coverage — Redis

| Scenario | Expected |
|---|---|
| Navigate to Redis in demo mode | Demo keys with hierarchy shown |
| Key detail | Values, TTL, and type shown for demo keys |

### Feature coverage — Storage

| Scenario | Expected |
|---|---|
| Navigate to Storage in demo mode | Demo accounts, containers, blobs shown |
| Blob detail | Demo blob content and properties shown |

### Feature coverage — Releases

| Scenario | Expected |
|---|---|
| Navigate to Releases in demo mode | Demo pipeline board with mixed statuses |
| Approval center | 1 demo pending approval shown |

### DI switch while page loaded

| Scenario | Expected |
|---|---|
| User enables demo mode while on AKS page | Page resets or shows "Reload required" message; no crash |

## Regression risks

- Any missing method on a demo client will cause `NotImplementedException` at runtime — thorough audit is essential
- DI resolution switch must not leave stale real-client references in singleton services

## Acceptance criteria

- Every feature area fully usable with no live connections in demo mode
- Amber banner visible on all pages when demo is active
- Demo mode persists across app restart
- All demo clients implement full interface without throwing `NotImplementedException`
