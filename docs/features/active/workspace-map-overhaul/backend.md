# Backend — Workspace Map in Agent Context

## Problem

`AgentSystemPromptBuilder.Build` (src-sidecar/Services/AgentSystemPromptBuilder.cs)
builds `## Current workspace context` from configured services only — cluster
context, Service Bus aliases, cache/account counts, DevOps org, observability
resource. The user-curated `config.Topology` (nodes + relationships) is never
included. The only way the model learns the map exists is the
`investigate_workspace_issue` tool, which `ResolveTools` only offers when a turn
requests `scope: "workspace"` (or on the context-free global `/agent` page). So the
declared relationships — the whole point of the feature — are invisible to the
agent on almost every turn.

## Change: `BuildWorkspaceMapSection`

New private method on `AgentSystemPromptBuilder`, called from `Build` after
`workspaceContext`, emitting (when `config.Topology.Nodes.Count > 0`):

```
## Workspace map (user-declared)
Resources: AKS: api (prod/api), worker (prod/worker) | Service Bus: orders (…)
Relationships: api → orders (consumes) · worker → orders (consumes)
```

Format decisions:

- Nodes grouped by area label (`Aks → "AKS"`, etc. — same labels the UI uses),
  rendered as `displayLabel (resourceKey)` so the model can match either surface
  when a user names a resource loosely.
- Edges rendered `fromLabel → toLabel (label)`, label omitted when null.
- **Caps**: `MaxMapNodes = 30`, `MaxMapEdges = 40`. Overflow renders as
  `… (+N more nodes)` / `… (+N more relationships)` per area/section — a dense map
  must not blow the per-turn context budget (`AgentContextBudgetPlanner` already
  estimates system-prompt chars into the window).
- Empty topology → section omitted entirely (no "none configured" noise).
- One trailing line of guidance, consistent with existing prompt style:
  relationships are user-declared, so the model can cite them directly without
  re-verifying; `investigate_workspace_issue` (workspace scope) walks them live.

Works for both provider paths unchanged: `OpenAiCompatibleAgentClient` sends it as
the system message; `AcpAgentModelClient.ComposePrompt` prepends it into the user
message on new/changed sessions.

## Tool description tweak

`InvestigateWorkspaceIssueTool.Description` currently reads like the map is only
reachable through it. Adjust wording: the map is already in the system prompt;
this tool *executes* the cross-area investigation along declared edges (up to
`MaxHops = 2`, unchanged).

## Tests — `tests/SwebKit.Sidecar.Tests/AgentSystemPromptBuilderTests.cs`

Existing file already covers the builder; add:

- Section present with nodes + relationships when topology populated.
- Section absent when topology is null/empty.
- Node cap: > `MaxMapNodes` nodes renders the overflow marker and stays bounded.
- Edge cap: same for relationships.
- Relationship renders label; unlabeled renders `→` without trailing parens.
- ACP parity is free (same `Build` path) — no separate test needed.

## No other backend changes

- Endpoints (`/api/workspace/topology/candidates|suggestions`) unchanged.
- Persisted model unchanged — no migration.
- `WorkspaceRelationshipSuggestionService` unchanged.
