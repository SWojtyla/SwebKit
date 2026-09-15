# Agent Correlation — Backend modules

Modules 2, 3 (signal half), 5, 6. All in `src-sidecar` / `src/SwebKit.Agents` unless noted.

## Module 2 — Scope-fence transparency

Two channels, one goal: the agent should know other configured areas exist but are
unreachable this turn, and be able to tell the user how to unlock them.

### 2a. Prompt section — `AgentSystemPromptBuilder`

`Build(AgentChatContext?, string normalizedMode, bool hasToolCalling)` gains a
`normalizedScope` parameter (callers: `SidecarAgentChatService.SendAsync` — single
call site). When **all** of these hold:

- `hasToolCalling` is true,
- `normalizedScope != "workspace"`,
- `context.FeatureArea` parses to a real `FeatureArea`,

append a `## Other configured areas` section listing each configured area NOT in the
visible set (i.e. configured areas minus `context.FeatureArea` minus `Observability`,
which is exempt), e.g.:

```
## Other configured areas
Service Bus (prod-bus), Redis (2 cache(s)) are configured but their tools are not
available in this turn's scope. If evidence points to one of them, tell the user to
enable "Search across my whole workspace" and ask again — do not guess.
```

Configuration sources already enumerated in `Build`: `config.AksConfig`,
`data.ServiceBusNamespaces`, `config.RedisConfig.Caches`, `config.StorageAccounts`,
`config.DevOpsConfig`, `config.ObservabilityConfig`. ApiClient is also a FeatureArea —
include it only if collections are configured (`data` exposes collections via the
same profile data — check `ProfileData` shape).

Keep it compact — this is prompt-stuffed into the first user message of every ACP
session and re-sent whenever it changes.

### 2b. Bridge error distinction — `SwebKitToolsMcpBridge.CallToolAsync`

Split the current single error:

- Unknown tool (not in registry at all): keep a generic "unknown tool" error.
- Known but disallowed (in registry, outside `?tools=` allowlist): return a
  distinguishable payload, e.g.
  `{"error": "tool_out_of_scope", "tool": "analyze_queue_health", "area": "ServiceBus",
  "message": "This tool belongs to a different area than this turn's scope. Tell the
  user to enable \"Search across my whole workspace\" to reach it."}`

`"tool_out_of_scope"` as the error code doubles as the Module 3 signal — the tracker
can key off it without parsing free text. Check `AgentToolCallOrchestrator.IsErrorResult`
still classifies it as an error (it checks for a top-level `error` property — any
string value works).

Tests: `SwebKitToolsMcpBridgeTests` — disallowed-known vs unknown produce different
error codes; both `IsError` true.

## Module 3 — Scope-escalation signal (backend half)

Goal: when an ACP agent calls a known-but-disallowed tool, the chat reply carries a
flag the UI turns into a "retry with workspace scope" action.

Mechanics (all in-process, no new endpoints):

- New singleton `OutOfScopeCallTracker` (src-sidecar/Services or Services/Acp):
  `ConcurrentDictionary<string, int>` keyed by the raw `?tools=` allowlist string
  (the exact query value the model client baked into the URL — it already uniquely
  identifies the resolved tool set of that session). Bridge records a hit in
  `CallToolAsync` when returning `tool_out_of_scope`, keyed by the request's
  `tools` query param.
- `AcpAgentModelClient.ChatStreamAsync`: snapshot `tracker.Count(allowlistKey)`
  before `PromptAsync`, re-read after; if it grew, set
  `AgentChatResult.SuggestedScope = "workspace"` on the `Done` result.
- `AgentChatResult` (check where it lives — `SwebKit.Agents` or sidecar): add
  `SuggestedScope` string? — serialized through `AgentEndpoints.ToWireEvent` into the
  SSE `done` payload. Local path never sets it.

Caveat to handle: the MCP `?tools=` value must be recoverable in `CallToolAsync` —
it already is (`Request.Query["tools"]` via `AllowedSet()`); use the raw string as
the key, not the parsed set, so key shape matches what the client built.

Tests: bridge rejection increments the tracker under the right key; a chat-level
test can construct the client with a tracker pre-seeded and assert
`SuggestedScope` on the result.

## Module 5 — `analyze_storage_health`

New tool `src/SwebKit.Agents/Tools/Storage/AnalyzeStorageHealthTool.cs`,
`FeatureArea.Storage`, `ToolKind.Read` (default). Shape follows
`AnalyzeQueueHealthTool`/`AnalyzeCacheHealthTool`: per-account (or `account_id`
param matching `StorageToolContext`'s resolution), report reachability, container
count, and any cheap health signals available from `IStorageClient` — check what the
client interface exposes before designing the payload (list containers, account
properties; likely no deeper metrics without Monitor).

Then wire it into `InvestigateWorkspaceIssueTool.InvestigateNodeAsync`: replace the
`default:` Storage skip with a real call — `analyze_storage_health` with the node's
`ResourceKey` (account id — check `WorkspaceResourceNode.ResourceKey` convention for
Storage nodes; ServiceBus keys may carry `/queue`, Storage may carry `/container` —
split like the ServiceBus case does).

Tests: `SwebKit.Agents.Tests` tool-level test with a fake storage client
(follow `AnalyzeCacheHealthTool` tests if they exist); `SwebKit.Sidecar.Tests`
`InvestigateWorkspaceIssueTool` path — a Storage node now produces a real report
instead of `skipped`.

## Module 6 — Log/trace-derived relationship suggestions

Extend `WorkspaceRelationshipSuggestionService.CollectHaystackAsync`:

- Add the matching pod's recent log lines to the haystack — tail ~100 lines via
  `IAksClient.StreamPodLogsAsync` (same call `InvestigatePodIssueTool` uses, higher
  cap is fine since matching is substring, not tokenized). Guard with the existing
  best-effort try/catch so a pod that can't stream logs just contributes less haystack.
- Distinct `Reason` wording for log-derived matches: "...appears in recent pod logs —
  weaker evidence than config; confirm before accepting". Track which source matched
  per suggestion (config haystack vs log haystack) — simplest: two passes, or tag
  each haystack entry with its source.
- Optional/stretch: if `config.ObservabilityConfig` has a selected resource, also
  query recent App Insights exceptions for resource names. Only if the provider
  abstraction makes this cheap (`IObservabilityProviderFactory` + `query_logs` with a
  small KQL `exceptions | top 50 by timestamp`) — if it turns invasive, drop it;
  pod logs alone already cover the motivating case (crashloop → queue name in
  stack trace).

Non-goal stands: suggestions remain user-confirmed; nothing is auto-added to the
topology.

Tests: `WorkspaceTopologyEndpointsTests` (suggestions endpoint) — a node whose pod
logs mention a queue name produces a suggestion with the log-sourced reason; config
matches still report the config-sourced reason; existing pair suppression unchanged.
