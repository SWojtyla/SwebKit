# Status - windows-tray-aks-monitoring

---

title: "Status - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-03-31"
last_updated: "2026-03-31"

---

## Quick summary

Windows tray lifecycle behavior is implemented for Minimize and Close (hide to tray), with tray Restore and Exit actions and pod-alert unread indicator updates while hidden.

Jira: not linked

Current focus: finish final validation sweep (build/tests/manual tray checks) and close out docs.

## Progress checklist

### Wave 0 - Planning

- [x] Scope clarified (minimize-to-tray, keep configured AKS monitoring, Windows-only)
- [x] Core feature docs created and aligned with architecture/pitfalls
- [x] Upfront technical decisions captured
- [x] Validation strategy defined

### Wave 1 - Tray lifecycle foundation

- [x] Add Windows tray service and single-icon lifecycle ownership
- [x] Intercept Minimize and Close actions to hide window to tray
- [x] Add explicit Restore and Exit actions from tray menu
- [x] Preserve existing AppDomain process-exit cleanup behavior

### Wave 2 - Monitoring continuity integration

- [x] Wire tray indicator state to pod health monitoring events
- [x] Keep configured namespace monitoring active while hidden
- [x] Ensure no monitoring restart/reset occurs on hide/restore cycles
- [x] Reset unread tray alert state on restore

### Wave 3 - UX and hardening

- [x] Add clear user affordance for close-to-tray behavior (tray icon + tooltip + Restore/Exit menu serve as affordance; OS tray is the standard pattern)
- [x] Add logging around tray lifecycle transitions and failures
- [ ] Validate toast + tray indicator behavior together under live alerts (manual smoke test, pending)
- [x] Prevent duplicate tray icon handles across repeated window state changes

### Wave 4 - Validation and docs

- [x] Add/update tests in app/core test projects
- [x] Run build + relevant test suites
- [x] Update functionality docs for AKS and settings/config behavior
- [x] Ready for review

## Completed

- Implemented `ITrayLifecycleService` wiring in app startup and process-exit cleanup.
- Added Windows tray lifecycle service with:
- Minimize and Close interception to hide to tray.
- Tray icon with Restore and Exit menu actions.
- Explicit Exit path that bypasses close interception and performs full app shutdown.
- Wired tray unread-alert indicator to existing `PodHealthMonitorService.PodHealthDetected` events.
- Added tray state unit coverage in app tests (`TrayLifecycleStateTests`).
- Preserved monitor lifecycle ownership in existing `PodHealthMonitorService` (no second loop).

## Remaining

- Execute manual tray smoke checks (Minimize/Close/Restore/Exit, hidden alert increments).
- Decide whether close-to-tray should become a user-toggleable setting in a follow-up.

## Blockers

- Full `SwebKit.App.Tests` suite currently has unrelated pre-existing failures outside tray scope (EntityTree, ServiceBusNamespacePanel, ScheduledMessages, MessageListView).

## Validation

- Test Plan: test-plan.md
- Validation status: Tray-targeted automated checks passed; full app suite has unrelated existing failures; manual tray smoke checks pending
- Automated checks run:
- `MSBuild src/SwebKit.App/SwebKit.App.csproj -t:Build -p:Configuration=Debug -p:Platform=x64` (pass)
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter FullyQualifiedName~TrayLifecycleStateTests` (pass)
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj` (fails: 14 tests outside tray scope)

## Notes

- Existing PodHealthMonitorService already persists monitoring enabled state and namespace selections; implementation should reuse this behavior, not replace it.
- Feature is intentionally Windows-only in this slice. Non-Windows uses `NullTrayLifecycleService` and keeps default behavior unchanged.
