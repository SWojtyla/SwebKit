# Technical Plan — AKS Multi-Context Alert Rules

## Approach

The evaluation path already honored `AksPodAlertParams.KubeconfigContext`
(`PodSignalSourceBase` → `IMonitoringConnectionPool.GetAksClient(context)`); the missing
surface was discovery and editing. Implemented as:

1. `GET /api/aks/namespaces?context=` — optional context forwarded to the pool so the
   namespace picker lists the pinned cluster, not the configured one. Extracted as a
   testable handler per the file's convention.
2. `useAksNamespaces(enabled, context?)` — optional explicit context; the query key uses
   `context ?? configuredContext` so each cluster's list caches under its own key.
3. `AlertRuleDialog` — "Cluster context" `<select>` above Namespace; `""` = configured
   context (back-compat), stale pinned contexts render as "(not in kubeconfig)" options,
   context change clears the namespace, and a namespace-list failure degrades to a
   free-text input.
4. `AlertRuleRow` — AKS rows append `context/namespace` to the subtitle.
5. Agent path — `aks_context` in `propose_create_alert_rule` maps onto
   `KubeconfigContext` (optional; empty keeps configured-context semantics).

## Notes

- Context stays optional — `""` means "follow Settings", preserving every existing rule.
- Demo mode: `GetClient` returns the demo client for any context; demo contexts come from
  `DemoAksClient.GetContextsAsync`.
