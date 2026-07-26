# Demo Mode — Restore Parity with MAUI

## Summary

The sidecar's demo mode core (`src-sidecar/Endpoints/DemoModeService.cs`, gating every AKS/Service
Bus/Redis/Storage endpoint via `GetClient()`) is solid and correctly ported — no dataset drift, no
endpoint bypassing demo mode. What's actually missing is **two entire domains that were never
ported at all** (Observability/logs-traces and DevOps/pipelines), plus a scripted "pod goes
unhealthy" demo moment that no longer has anything to react to it, and no UI indication anywhere
that demo mode is active outside the Dashboard toggle itself. This — not broken plumbing — is why
demo mode "doesn't feel the same anymore" per the user's report.

**Jira:** not linked

## Scope

Decide what demo-mode coverage this release actually needs, then either port the two missing
domains or explicitly descope them, and restore the "demo mode is active" affordance across
Settings.

## Non-Goals

- Not changing the demo dataset itself (namespaces, pods, blobs, etc.) — review found no drift here.
- Not touching the AKS/Service Bus/Redis/Storage demo gating logic — already correct.

## Tasks

### 1. Observability and DevOps demo mode: dropped (decision made)

**Decision (2026-07-26, user):** Observability (App Insights logs/traces) and DevOps
(pipelines/releases) are **out of scope, permanently** — not a deferral, a drop. The user was never
satisfied with the MAUI versions of these and does not want them ported.

Cleanup actions:
- Remove `src/SwebKit.Core/Services/DemoObservabilityProvider.cs`, `DemoDevOpsClient.cs`, and
  `DemoApiCollectionFactory.cs`'s Observability/DevOps-only bits if any (check for shared usage
  first) — dead code with no sidecar consumer and no planned one.
- Remove the `SwebKit.Observability` and `SwebKit.DevOps` project references from
  `src-sidecar/SwebKit.Sidecar.csproj` if they're only pulled in for these dead services (verify
  nothing else in the sidecar depends on them first).
- Update `docs/features/README.md`'s canonical feature order to drop `observability` from the list
  (or annotate it as dropped) so it doesn't look like a pending TODO to future readers.
- No sidecar endpoints or React pages to build — there's nothing to port.

### 2. Reconnect the scripted "pod failure" demo moment, or drop it (Major)

**File:** `src/SwebKit.Core/Services/DemoAksClient.cs` (`BuildDemoPods`, ~line 162-167) still
contains the tick==2 → `search-indexer` pod goes `Failed` scripted event, mirroring the original
`PodHealthMonitorService`-driven notification demo. But `PodHealthMonitorService.cs`/
`MonitoringConnectionPool.cs` (the pieces that polled this and fired the notification) only exist
under `src/SwebKit.App/` and were never ported — no references anywhere in `src-sidecar` or
`web/src`. `web/src/lib/tauri-bridge.ts:200` has a `showNotification` bridge, but nothing calls it
from AKS polling.

**Fix:** in the React AKS page, poll pod status (it likely already does for the pods tab) and when
a demo-mode pod transitions to `Failed`/unhealthy, fire a native notification via the existing
`showNotification` bridge — this restores the "the demo comes alive" moment without needing to port
the full MAUI health-monitor architecture. If this feels like scope creep given everything else in
this plan, it's fine to drop instead: delete the scripted event from `DemoAksClient.cs` so dead code
doesn't linger, and note it as a deliberate cut.

### 3. Add demo-mode UI affordance to Settings (Minor)

Grep confirms none of `AksSettings.tsx`, `ServiceBusSettings.tsx`, `RedisSettings.tsx`,
`StorageSettings.tsx`, `DevOpsSettings.tsx`, `GeneralSettings.tsx` reference demo mode — only
`DashboardPage.tsx` has the toggle. MAUI's `ProjectPicker.razor`/`ReleaseList.razor` explicitly
showed demo-specific messaging via an `IsDemoMode` parameter threaded through the UI. **Fix:** when
demo mode is active, show a small banner/badge in each relevant Settings tab noting that connection
fields are inert while demo mode is on (this also naturally covers Task 1's "not available yet"
messaging if Observability/DevOps stay out of scope).

### 4. Note: API Client and Blob Recovery don't have backend routes regardless of demo mode (Informational, not actionable here)

`ApiClientEndpoints.cs` doesn't reference `DemoModeService` at all, so demo users get an empty API
client instead of the populated example collection `DemoApiCollectionFactory.cs` (still present,
unused) provided in MAUI. Separately, `BlobRecoveryPanel.tsx` has no backend route at all
(`StorageEndpoints.cs` has no `RestoreBlobVersionAsync`/`UndeleteBlobAsync`/`ListBlobVersionsAsync`
routes, even though `DemoStorageClient.cs` implements the demo data for them) — this is a real-mode
gap too, not demo-mode-specific, so it's out of scope for *this* plan. Flagging here so it isn't
lost: worth its own small feature (`docs/features/active/blob-recovery-endpoints/`) wiring
`StorageEndpoints.cs` to the existing `DemoStorageClient`/real storage client methods.

## Dependencies

- Depends on the scope decision in Task 1 before work starts — this changes the size of the plan
  substantially (a UI-messaging tweak vs. two full feature ports).
- Independent of the security-hardening and AKS/Service-Bus fix plans — can run in parallel with
  those.

## Risks

| Risk | Mitigation |
|---|---|
| Task 1 turns into an unplanned full Observability/DevOps port | Get an explicit scope decision before starting; split into separate feature plans if in scope |
| Task 2's notification wiring surfaces unrelated AKS polling bugs | Test in isolation against demo mode only; don't couple to real-cluster polling changes |
