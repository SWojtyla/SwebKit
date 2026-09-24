# Test Plan — AKS Multi-Context Alert Rules

## Scenarios

- New AKS rule: context picker lists kubeconfig contexts plus the empty
  "Configured context" fallback; namespaces load for the selected context.
- Switching context clears the namespace and disables Save until a namespace is re-picked.
- A rule pinned to a context reopens with that context selected; the row shows
  `context/namespace`.
- A rule pinned to a context absent from the kubeconfig keeps the value selectable
  ("not in kubeconfig") instead of silently retargeting.
- Namespace list failure falls back to a free-text input so a rule can still be saved
  (e.g. PRD cluster unreachable over VPN but the namespace name is known).
- Existing rules with empty/absent `kubeconfigContext` remain valid and evaluate against
  the configured context (backwards compatibility).
- Demo mode: contexts come from `DemoAksClient`; namespace listing ignores the context
  param and returns demo namespaces.
- Agent path: `aks_context` maps onto `AksPodAlertParams.KubeconfigContext`.

## Coverage

- `AksEndpointsTests`: `GetNamespacesAsync` forwards explicit context to the pool;
  omitted/blank context requests the default.
- `MonitoringActionExecutorTests`: `aks_context` persists onto the created rule.
- `web/e2e/monitoring.spec.ts`: pin `aks-ecommerce-prod`, verify row label, reopen
  persistence, and the namespace-clear-on-context-switch failure path.
- Validation unchanged: namespace required, context optional (`""` = follow configured).
