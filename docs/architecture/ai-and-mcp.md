# AI & MCP Architecture

How the SwebKit agent is wired end to end: model providers, the domain-tool model, the MCP bridge that exposes those tools to external agents, and the extension points we are building toward. For the user-facing feature description see `functionalities/agent.md`; this doc is about *how the machinery works*.

## Big picture

```text
┌─────────────────────┐        SSE/HTTP         ┌──────────────────────────────┐
│  React (Tauri web)  │  ────────────────────▶  │        .NET sidecar          │
│  3 chat surfaces:   │                         │  SidecarAgentChatService     │
│  /agent · docked    │                         │   ├─ orchestrator (gates)    │
│  panel · contextual │                         │   ├─ context builder/budget  │
│  panel              │                         │   └─ model client router     │
└─────────────────────┘                         └───────┬──────────┬───────────┘
                                                        │          │
                              in-process tool loop      │          │  stdio JSON-RPC (ACP)
                                                        │          │
                                            ┌───────────▼──┐  ┌────▼───────────────────┐
                                            │ OpenAI-compat│  │ ACP agent subprocess    │
                                            │ (LM Studio,  │  │ (Claude, Gemini, ...)   │
                                            │  Mistral, …) │  │ owns its own tool loop  │
                                            └──────┬───────┘  └────┬───────────────────┘
                                                   │               │ MCP tool calls
                                            ┌──────▼───────────────▼────────────┐
                                            │   IAgentToolRegistry               │
                                            │   ~30 IAgentTool impls             │
                                            │   (AKS/SB/Redis/SQL/Storage/       │
                                            │    Observability/Monitoring/…)     │
                                            └──────────┬─────────────────────────┘
                                                       │ domain services
                                            ┌──────────▼──────────┐
                                            │ SwebKit.Core/Azure/ │
                                            │ Kubernetes/Redis/…  │
                                            └─────────────────────┘
```

Both provider paths execute the *same* registry. The difference is who drives the tool loop:

- **OpenAI-compatible** — the sidecar drives: send tool definitions → model returns `tool_calls` → execute → feed results back.
- **ACP** — the external agent drives: it gets our tools as an **MCP server** at session creation and calls them whenever it wants.

## 1. The tool model (provider-agnostic)

Every tool is an `IAgentTool` (`src/SwebKit.Agents/Tools/`):

```csharp
string Name;                    // "get_pod_logs", "analyze_queue_health", …
string Description;             // shown to the model, plus injected [area; access] hints
JsonElement ParametersSchema;   // JSON Schema for arguments
FeatureArea FeatureArea;        // Aks | ServiceBus | Redis | Sql | Storage | Observability
                                // | ApiClient | Monitoring | Workspace
ToolKind Kind;                  // Read | Mutate
ToolRisk Risk;                  // None | Low | High — drives confirmation UI
AgentCapability RequiredCapability;
Task<string> ExecuteAsync(JsonElement args, CancellationToken ct);
```

Result convention: a JSON **string**; a top-level `"error"` property marks failure (the orchestrator and the MCP bridge both detect it the same way).

Tools are registered as open-type DI (`IEnumerable<IAgentTool>`) into `AgentToolRegistry` — adding a tool is adding one class. There are two categories:

- **Primitive tools** — `list_pods`, `get_queue_stats`, `list_redis_keys`, `query_logs` (KQL), `get_metrics`.
- **Composite investigative tools** — `investigate_pod_issue`, `analyze_queue_health`, `investigate_workspace_issue`. One call fans out to several data sources **sidecar-side** and returns one synthesized projection. These are our main "smart harness" lever: deterministic multi-step gathering without burning model tool calls.

Mutation tools are named `propose_*` and never execute the change — they create a *pending action* the user confirms in the UI (`PendingActionCard` → feature's `ActionExecutor`).

## 2. Per-turn tool visibility gates

`AgentToolCallOrchestrator.ResolveTools` decides which definitions a turn sees, applied in order:

| Gate | Rule | Default |
|---|---|---|
| Capability | No tool-calling support → no tools | — |
| Mode | `"ask"` keeps only `ToolKind.Read` | anything ≠ `ask_and_do` → ask |
| Scope | `"feature"` keeps only the turn's `FeatureArea` | anything ≠ `workspace` → feature |
| Area | Contextual panel's area + **exempt**: `Observability` (cross-cutting telemetry) and `get_screen_state` | global `/agent` → no area filter |

Same gate produces both the definition list for OpenAI-compatible models **and** the `?tools=` allowlist baked into the MCP bridge URL for ACP agents.

## 3. The MCP bridge — our tools as an MCP server

`SwebKitToolsMcpBridge` (`src-sidecar/Services/Acp/`) is a real MCP server: **stateless streamable-HTTP** at `/mcp/swebkit-tools` (`app.MapMcp` in `Program.cs`). Statelessness is what makes per-turn scoping possible — the scoping travels in the URL, not in server state:

```text
http://localhost:{port}/mcp/swebkit-tools?tools=list_pods,get_pod_logs,…&sel=aksPod=kube-system/coredns-abc&sel=cluster=prod
```

- `?tools=` — the resolved allowlist for this turn's mode/area/scope. Absent ⇒ **standalone surface: read-only tools only** (internal ACP sessions always carry an explicit allowlist, so an absent one means an unmanaged client attached directly); empty ⇒ no tools.
- `?mode=full` — opt-in escape hatch for standalone clients: exposes the whole registry including `propose_*` mutations. Only meaningful without `?tools=`.
- `?sel=` — key/value pairs pushed into `AgentExecutionContext` on every call, so tools can default to *the resource the user is looking at* without the model passing identifiers.

Call flow:

```text
MCP tools/call (name, arguments)
  → ?tools= allowlist check ── not allowed ──▶ {"error":"tool_out_of_scope", tool, area, message}
  → standalone check (no ?tools=, no ?mode=full, ToolKind.Mutate)
      ── blocked ──▶ {"error":"tool_read_only", tool, message}
  → AgentExecutionContext.Push(sel)
  → AgentExecutionContext.Push(sel)
  → registry.ExecuteAsync → IAgentTool → domain service → JSON result
```

An out-of-scope call is deliberately a *distinguishable* error, not "unknown tool" — it tells the agent the tool exists but needs wider scope (`OutOfScopeCallTracker` counts these so the UI can offer a scope-widening retry).

## 4. ACP external agents (Claude, Gemini, …)

`AcpAgentHost` owns one child process per active ACP profile and multiplexes sessions over newline-delimited JSON-RPC on stdio:

```text
spawn(Command, Arguments, env+CredentialEnvVar)
  → initialize        { clientCapabilities: {} }        // fs/*, terminal/*, elicitation: all OFF
  → session/new       { cwd, mcpServers: [...] }        // ← where tools get injected
  → session/prompt    { prompt: [{type:"text",text}] }  // streamed as session/update events
  → session/cancel    on user cancellation
```

The `session/new` payload (with one configured external server of each transport):

```json
{
  "cwd": "<profile.WorkingDirectory or sidecar cwd>",
  "mcpServers": [
    { "type": "http", "name": "swebkit", "url": "http://127.0.0.1:5198/mcp/swebkit-tools?tools=…&sel=…", "headers": [] },
    { "type": "http", "name": "azure", "url": "https://mcp.example.com/mcp",
      "headers": [{ "name": "X-Env", "value": "prod" }] },
    { "type": "stdio", "name": "local", "command": "npx",
      "args": ["-y", "@scope/server"], "env": [{ "name": "LOG_LEVEL", "value": "debug" }] }
  ]
}
```

`headers`/`env` are `{name,value}` arrays, and `headers` must be present even when empty — the ACP schema requires it for http/sse servers (verified against claude-agent-acp, which silently drops entries missing it). `swebkit` always comes first. External descriptors come from `AgentProfile.ExtraMcpServers` (`{ id, name, enabled, transport, url, headers, command, arguments, environmentVariables }`), edited per-profile in Settings → Agent. Enabled extras are serialized into the session spec, so editing them drops and recreates the ACP session. http extras are skipped with a warning when the agent doesn't advertise `mcpCapabilities.http`; stdio entries are passed through unconditionally (the agent decides whether it can spawn them).

Because MCP servers are fixed at session creation, changing the allowlist/spec drops the ACP session and creates a fresh one (`EnsureSessionAsync` compares the spec). The external agent owns its transcript; we never see its reasoning except what it streams back as `session/update`.

### Two independent approval layers

| Layer | Mechanism | Applies to |
|---|---|---|
| Domain actions | `propose_*` tools → `PendingActionCard` → `ActionExecutor` | every provider — our mutations are self-gating |
| Provider permissions | `session/request_permission` → `AcpPermissionStore` → `AcpPermissionCard` | ACP only, and only when `RequireToolApproval` is on (default off — redundant second click for our own tools) |

With external MCP servers plugged in (below), the provider layer becomes the gate for tools we don't control — which is the argument for defaulting `RequireToolApproval` ON once a profile has extra MCP servers.

## 5. Context injection (the cheap intelligence)

What the harness does *before/around* the model so it doesn't have to:

- **Selection (`sel=`)** — tools default to the resource open in the UI; "this pod" needs zero discovery calls.
- **Screen state** — pages publish whitelisted snapshots → `ScreenStateStore` (5 min TTL) → `get_screen_state` reads them. Must never include secrets/bodies.
- **Workspace maps** — bounded `from → to (label)` rendering in the system prompt; `investigate_workspace_issue` walks it.
- **Budgeting** — `AgentContextBudgetPlanner` bounds history/screen/tool-result sizes; long chats summarize with a UI-visible boundary.
- **Descriptions** — `[area; access]` hints appended to every tool's description; schema descriptions steer toward cheaper calls first.

## 6. Extension points — where we're going

```text
                        ┌─────────────────────────────┐
   external agents ───▶ │  /mcp/swebkit-tools (bridge)│ ◀── Phase 2a: standalone use
   (Claude Desktop etc) │  read-only default,         │     (docs, ?mode=full opt-in)
                        │  ?mode=full opt-in          │
                        └─────────────────────────────┘
session/new.mcpServers = [
  swebkit (ours),
  azure-mcp,            ◀── Phase 1: external MCP passthrough
  github-mcp, …              (per-profile config, App Insights breadth "for free")
]

non-ACP providers ──▶ ExternalMcpToolSource ◀── Phase 2b: proxy external MCPs
(LM Studio/Mistral)   (mcp_ prefixed defs)     readOnly-only, post-filter
```

### Phase 1 — External MCP passthrough ✅ shipped

Profile-configured `ExtraMcpServers` are appended to `session/new → mcpServers` as described above — e.g. Microsoft's Azure MCP covers the App Insights/Log Analytics breadth beyond our native `query_logs`/`get_metrics`. Caveats: permission requests are the only gate external tools get (Settings auto-enables `requireToolApproval` on first attach); MCP namespacing prevents collisions; external servers only exist for ACP profiles.

### Phase 2a — Standalone MCP exposure ✅ shipped

The bridge is a real MCP server on the sidecar's HTTP port — any client that speaks streamable HTTP can attach while the app runs. Because the sidecar binds **loopback only** (`127.0.0.1:5199` dev; ephemeral port under Tauri), no external auth layer is needed; a non-loopback deployment would have to rethink this.

**Safety default:** without `?tools=` the endpoint advertises and executes **read-only tools only** — `propose_*` mutations are hidden from `tools/list` and blocked with `{"error":"tool_read_only", …}` on call. `?mode=full` opts into the complete surface for clients where registering pending proposals (which still need UI confirmation) is desired. With `?tools=` present, allowlist semantics take over and `mode` is ignored.

**Claude Desktop** — `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "swebkit": { "url": "http://127.0.0.1:5199/mcp/swebkit-tools" }
  }
}
```

**Claude Code** — `claude mcp add --transport http swebkit http://127.0.0.1:5199/mcp/swebkit-tools`

Both get the read-only surface; append `?mode=full` to the URL for the full registry. Requirements and caveats:

- The **sidecar must be running** (app open, or `dotnet run` in `src-sidecar/`) — credentials resolve through its `ICredentialStore`, so tools work headlessly while it lives.
- No `?sel=` ⇒ tools fall back to configured defaults rather than UI selection.
- No per-turn memoization on this path (no turn boundary exists) — identical calls re-execute.
- Proposal tools under `?mode=full` register pending actions that surface in the SwebKit UI — confirm/deny still happens there.

### Phase 2b — MCP client adapter ✅ shipped (read-only)

`ExternalMcpToolSource` resolves a non-ACP profile's `ExtraMcpServers` into proxied `ToolDefinition`s: one cached `McpClient` per server config (stdio subprocesses spawn once, not per turn; keys are the serialized config so edits reconnect), `readOnlyHint` tools only, exposed as `mcp_{server}_{tool}` names appended **after** the per-area filter (area-exempt like Observability). Execution routes through the step-tracking executor's `externalExecutors` map — same steps, same per-turn read memoization.

Safety posture: an **absent `readOnlyHint` is not a promise**, and the in-process path has no permission-request gate — so mutating/unannotated external tools are skipped (logged by name) rather than guessed at. Mutation-capable external tools are an ACP-only feature, where `session/request_permission` gates every call. Caveats: external tools don't consume `sel=`/`AgentExecutionContext` (foreign servers don't know our selection model); proactive investigations don't attach them; no per-call approval exists in-process.

### Phase 3 — Native App Insights: already partly done

`query_logs` (KQL) + `get_metrics` exist via `SwebKit.Observability`'s `AzureAppInsightsProvider`, and `Observability` is exempt from per-area filtering precisely so cross-area investigations can pull telemetry. Native *expansion* (UI surfaces, alert-rule sourcing, more tools) is a product decision, not a prerequisite for the MCP work.

## 7. Security model (non-negotiables)

- Credentials stored as **keys**, resolved via `ICredentialStore` — never in prompts, tool args, screen state, or stream events.
- `clientCapabilities: {}` — ACP agents get **no** filesystem/terminal/elicitation from us.
- Empty allowlist ⇒ zero tools; allowlist baked per session, not per request.
- Standalone MCP access (no `?tools=`) is **read-only by default**; `propose_*` tools need explicit `?mode=full`, and proposals still require UI confirmation.
- `ask` mode and proactive investigations are structurally read-only.
- External MCP servers are user-configured; their tools are outside our propose/confirm safety — hence approval-gating guidance above.

## File map

| Concern | Location |
|---|---|
| Tool contract + metadata | `src/SwebKit.Agents/Tools/IAgentTool.cs` |
| Tool implementations | `src/SwebKit.Agents/Tools/{Aks,Redis,Sql,Storage,ApiClient,Monitoring}/…`, `Tools/*.cs` |
| Registry | `src/SwebKit.Agents/AgentToolRegistry.cs` |
| Turn gates + step tracking | `src-sidecar/Services/AgentToolCallOrchestrator.cs` |
| Chat service / routing | `src-sidecar/Services/SidecarAgentChatService.cs`, `AgentModelClientRouter.cs` |
| Context building/budget | `src/SwebKit.Agents/AgentContextBuilder.cs`, `src-sidecar/Services/AgentContextBudgetPlanner.cs` |
| MCP bridge | `src-sidecar/Services/Acp/SwebKitToolsMcpBridge.cs` (`Program.cs: MapMcp`) |
| ACP host/peer/launch | `src-sidecar/Services/Acp/AcpAgentHost.cs`, `AcpJsonRpcPeer.cs`, `AcpProcessLauncher.cs` |
| Permissions | `src-sidecar/Services/Acp/AcpPermissionStore.cs`, `OutOfScopeCallTracker.cs` |
| Profile model | `src/SwebKit.Core/Domain/AgentProfile.cs` |
| Frontend | `web/src/lib/hooks/useAgent.ts`, `web/src/components/agent/` |
