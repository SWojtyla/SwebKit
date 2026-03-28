# Feature Overview — AKS Pod Health Monitor

---

title: "Feature Overview — AKS Pod Health Monitor"
owner: ""
status: "Planned"
created: "2026-03-26"
updated: "2026-03-26"

---

## Goal

Provide active background monitoring of user-selected Kubernetes namespaces with **Windows desktop toast notifications** when pods go down — even when the AKS page is not in the foreground.

## Value

SwebKit users managing AKS clusters need prompt awareness when pods fail. Currently, the only way to notice a pod going down is to manually navigate to the AKS page and inspect the pod grid. This feature gives users real-time-ish alerting (≤2 min detection) with zero manual effort, surfaced through native Windows toasts that are visible regardless of which app is in focus.

## Scope

### In scope

- Background monitoring service (`IPodHealthMonitorService`) that survives page navigation
- User selection of specific namespaces to monitor per AKS cluster context
- Pod state diffing: detect transitions to Failed, CrashLoopBackOff, Unknown, container not-ready
- Windows system toast notifications via WinRT `Windows.UI.Notifications`
- Notification deduplication / cooldown per pod (avoid spam)
- Persistent config: monitored namespaces stored in `AppConfig`
- UI: namespace monitoring selector, status indicator, alert history panel
- 2-minute polling interval using `PeriodicTimer`

### Out of scope

- Kubernetes watch API / streaming connections (see Decision 001)
- Monitoring of non-pod resources (deployments, services, nodes)
- Multi-cluster simultaneous monitoring (monitor active cluster context only)
- Custom alerting rules or thresholds beyond pod health
- Notification channels other than Windows toast (email, Slack, Teams, etc.)
- Sound customization for notifications
- Mobile platform notifications (Windows only)

## Dependencies

- **`IAksClient`** — existing `GetPodsAsync` and `GetNamespacesAsync` in `SwebKit.Kubernetes`
- **`IAppEventBus`** — for broadcasting pod health events to UI components
- **`AppConfig` / `AksConfig`** — for persisting monitored namespace selections
- **WinRT APIs** — `Windows.UI.Notifications.ToastNotificationManager` (available in WinUI/MAUI Windows)
- **`INotificationService`** — existing in-app notifications (supplements Windows toasts with in-app alerts)

## Risks & mitigations

| Risk                                                                                    | Severity  | Mitigation                                                                                         |
| --------------------------------------------------------------------------------------- | --------- | -------------------------------------------------------------------------------------------------- |
| AKS client is not in DI — created on-demand per `AksConfig`                             | 🟡 MEDIUM | Monitor service manages its own client lifecycle; recreate client when cluster context changes     |
| PeriodicTimer running indefinitely could leak if not disposed                           | 🟡 MEDIUM | Strict disposal pattern; tie to app lifecycle via `IAsyncDisposable`; guard with CTS               |
| Polling every 2 min for multiple namespaces could hit API rate limits on large clusters | 🟢 LOW    | Sequential namespace polling per tick; configurable interval; graceful backoff on transient errors |
| Windows toast notifications require app identity / packaging                            | 🟡 MEDIUM | MAUI Windows apps are packaged by default (MSIX); verify toast capability in manifest              |
| Stale kubeconfig or expired tokens cause silent failures                                | 🟡 MEDIUM | Catch auth errors, surface warning notification, pause monitoring with clear status                |

## Architecture decisions

Key architectural choices are documented in [decisions.md](decisions.md):

1. **PeriodicTimer over Kubernetes watch API** — aligned with existing patterns, simpler, no persistent connections
2. **Windows toast notifications** — user may not have app in foreground; system-level visibility required
3. **Service-level, not component-level** — monitoring must survive page navigation
4. **Polling interval of 2 minutes** — balances responsiveness with API load (~720 calls/day/namespace)

## Related documents

- Architecture: `docs/architecture/architecture.md`
- AKS functionality: `docs/architecture/functionalities/` (update when feature ships)
- Pitfalls: `docs/pitfalls/blazor-maui.md` (BL-2 dispatcher, BL-5 parameter guards)
- Existing AKS client: `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- Existing notification service: `src/SwebKit.App/Services/NotificationService.cs`

## Quick links

- Status: [status.md](status.md)
- Backend plan: [backend.md](backend.md)
- Frontend plan: [frontend.md](frontend.md)
- Test plan: [test-plan.md](test-plan.md)
- Decisions: [decisions.md](decisions.md)
