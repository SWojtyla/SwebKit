# Agent Resource Discovery

State: Review

## Goal

The agent dead-ends whenever a question targets a resource that isn't the single configured
default: `query_logs` is pinned to `ObservabilityConfig.SelectedResourceId`, AKS tools take a
`context` argument the model can't enumerate, and Log Analytics workspaces (WAF, App Gateway
diagnostics) are unreachable by design. Real example: asked about prd sign-webhook traffic, the
agent reported "observability not configured", "no log tool for that workspace", and "the
connected cluster is dev" — three dead ends that all had a discoverable path.

## Scope

- `list_observability_resources` tool over `IObservabilityResourceDiscovery` (the existing
  demo-aware selector), so the model can enumerate App Insights components itself.
- Optional `resource` parameter on `query_logs` and `get_metrics` — ARM resource id verbatim,
  or a name/substring resolved through discovery; ambiguity returns the candidate list.
- `list_aks_contexts` tool over `IAksClient.GetContextsAsync` — the model learns which
  kubeconfig contexts exist and then passes `context` to the existing AKS tools.
- `query_workspace_logs` tool + `ILogAnalyticsWorkspaceService` (`AzureLogAnalyticsWorkspaceService`
  over ARM generic resources + `LogsQueryClient.QueryWorkspaceAsync`); omitting `workspace`
  lists discoverable workspaces. Demo mode gets a canned WAF-style result.
- System-prompt guidance: run discovery before declaring a resource unreachable; "not
  configured" / "no tool for X" are starting points, not terminal answers.

## Non-goals

- Selecting a different default observability resource in settings (UI concern).
- Workspace-scoped metric queries or alerting on Log Analytics.
- Changing the access-report capability model (discovery/workspace tools stay unprobed;
  denials still classify through `AccessAdvisor`).

## Implementation tasks

- [x] `ILogAnalyticsWorkspaceService` + `LogAnalyticsWorkspaceInfo` (Core abstractions)
- [x] `AzureLogAnalyticsWorkspaceService` (Observability) — ARM `GetGenericResources` for
      workspace discovery, `QueryWorkspaceAsync` for KQL
- [x] `DemoLogAnalyticsWorkspaceService` + `LogAnalyticsWorkspaceSelector` (demo parity)
- [x] `ListObservabilityResourcesTool`, `ListAksContextsTool`, `QueryWorkspaceLogsTool`
- [x] `resource` param on `QueryLogsTool`/`GetMetricsTool` via `ObservabilityResourceResolver`
- [x] DI registrations in `Program.cs`
- [x] System-prompt discovery guidance (interactive + background)
- [x] Unit tests (`AgentDiscoveryToolsTests`, resource-param cases in `ObservabilityToolsTests`,
      prompt assertion in `AgentSystemPromptBuilderTests`)
- [x] Architecture docs touch-up (`ai-and-mcp.md`, `functionalities/observability.md`,
      `functionalities/AGENT.md`)

## Test plan

- `list_aks_contexts`: fake `IAksClient` returning contexts; asserts configured-context flag
  and shape.
- `list_observability_resources`: fake discovery stream; asserts resources + configured id.
- `query_logs`/`get_metrics` with `resource`: id passthrough, name resolution, no-match and
  ambiguous-match errors, unconfigured + explicit resource still works.
- `query_workspace_logs`: name → unique workspace → query routed by customerId; ambiguous and
  zero-match errors; omitted workspace returns the list.
