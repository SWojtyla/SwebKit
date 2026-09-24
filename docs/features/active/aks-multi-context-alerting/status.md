# Status — AKS Multi-Context Alert Rules

**Status:** In Progress

## Changes

- `src-sidecar/Endpoints/AksEndpoints.cs` — extracted `GetNamespacesAsync`; optional
  `?context=` query param resolves the pool client for that kubeconfig context.
- `src/SwebKit.Agents/Tools/Monitoring/ProposeCreateAlertRuleTool.cs` — added
  `aks_context` schema property.
- `src-sidecar/Services/MonitoringActionExecutor.cs` — maps `aks_context` onto
  `AksPodAlertParams.KubeconfigContext`.
- `web/src/lib/hooks/useAks.ts` — `useAksNamespaces(enabled, context?)` accepts an explicit
  context (query key + `?context=` param).
- `web/src/components/monitoring/AlertRuleDialog.tsx` — "Cluster context" picker,
  per-context namespace loading, loading/error/stale-value handling.
- `web/src/components/monitoring/AlertRuleRow.tsx` — AKS rows show `context/namespace`.

## Validation

- Sidecar + agents unit tests, web unit tests, typecheck/build, e2e — see test-plan.md.
