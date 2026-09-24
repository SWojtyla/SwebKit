# AKS Multi-Context Alert Rules

## Scope

Monitoring alert rules for AKS sources (`AksPodHealth`, `AksPodRestartRate`,
`AksNamespaceHealthScore`) can be pinned to an explicit kubeconfig context, so rules for
different clusters (e.g. a throwaway demo rule in dev alongside real PRD alerts) coexist —
previously every rule evaluated against the globally configured context only.

## Outcomes

- Alert rule dialog exposes a "Cluster context" picker fed by `GET /api/aks/contexts`.
- `""` (default) keeps the legacy semantic: follow the globally configured context.
- The namespace picker lists the selected context's namespaces
  (`GET /api/aks/namespaces?context=`), and changing context clears a stale namespace pick.
- Rule rows show `context/namespace` so rules targeting different clusters are
  distinguishable at a glance.
- `propose_create_alert_rule` accepts `aks_context` for agent-proposed rules.

## Traceability

- Backend evaluation already resolved per-rule contexts:
  `PodSignalSourceBase` → `IMonitoringConnectionPool.GetAksClient(context)` →
  `SidecarMonitoringConnectionPool` (context-keyed client cache).
- `AksPodAlertParams.KubeconfigContext` existed in the core model and the TS API type; this
  feature wires the missing UI/endpoint/tool surface.
- MAUI parity: `AlertRuleDrawer.razor` already offered a context picker.
