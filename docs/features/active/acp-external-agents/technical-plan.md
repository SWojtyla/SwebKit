# ACP External Agents — Technical Plan

## Architecture

```
React UI ──SSE──▶ AgentEndpoints ──▶ SidecarAgentChatService ──▶ AgentModelClientRouter
                                                              ├─▶ OpenAiCompatibleAgentClient (today)
                                                              └─▶ AcpAgentModelClient
                                                                     │ owns
                                                                     ▼
                                                            AcpAgentHost (1 process per profile)
                                                                     │ NDJSON JSON-RPC stdin/stdout
                                                                     ▼
                                                            claude-agent-acp / gemini --acp / …
                                                                     │ session/new.mcpServers
                                                                     ▼
                                                   MCP streamable-HTTP endpoint /mcp on the sidecar
                                                   → filtered tool provider → AgentToolRegistry
                                                                     │ (propose_* tools still enqueue
                                                                     ▼  pending actions — unchanged)
                                                       IAgentActionCoordinator + existing approval UI
```

## Protocol surface used (ACP v1)

- Client→agent: `initialize`, `session/new`, `session/prompt`, `session/cancel` (notification);
  optionally `session/close`, `session/load`/`session/resume`, `session/set_mode` when advertised.
- Agent→client notifications: `session/update` (`agent_message_chunk`, `agent_thought_chunk`,
  `tool_call`, `tool_call_update`, `plan`, `usage_update`, `available_commands_update`,
  `current_mode_update`).
- Agent→client requests: `session/request_permission` (handled); `fs/*`, `terminal/*`,
  `elicitation/*` (unsupported → `-32601`).
- Transport: stdio only — newline-delimited JSON-RPC 2.0, UTF-8, no embedded newlines.
- `session/new` params: `cwd` + `mcpServers` (we pass one entry: SwebKit's streamable-HTTP MCP
  endpoint; requires the agent to advertise `mcpCapabilities.http`).

## Current-state anchors

- `IAgentModelClient` (`src/SwebKit.Agents/IAgentModelClient.cs`) — the seam: `ChatAsync`,
  `CompleteAsync`, `ChatStreamAsync` (yields `AgentStreamEvent`: token / toolCallStarted /
  toolCallResult / done / error; `Steps`, `Summarized`, `ContextUsagePercent` on terminal events).
- `SidecarAgentChatService` (`src-sidecar/Services/SidecarAgentChatService.cs`) — per-session
  history, system prompt, tool resolution, budget planner, SSE source.
- `AgentToolCallOrchestrator.ResolveTools` (`src-sidecar/Services/AgentToolCallOrchestrator.cs`) —
  capability → mode (`ask`/`ask_and_do`) → feature-area/`workspace` scope gating.
- `AgentEndpoints` (`src-sidecar/Endpoints/AgentEndpoints.cs`) — `/api/agent/chat`,
  `/chat/stream` (SSE), `/clear`, `/status`, `/profiles/{id}/test`, `/pending-approvals/*`.
- `ProviderKind` (`src/SwebKit.Core/Domain/ProviderKind.cs`) = LmStudio | OpenAiCompatible |
  Mistral; `AgentProfile` + `AgentProfilePresets` + `AgentConfig.Migrate()`.
- DI: `AddHttpClient<IAgentModelClient, OpenAiCompatibleAgentClient>()` in
  `src-sidecar/Program.cs`.
- Frontend: `useAgentChatStream`/`streamAgentChat` (`web/src/lib/hooks/useAgent.ts`,
  `web/src/lib/api.ts`), `AgentStreamEventKind` in `web/src/lib/types.ts`, settings form
  `web/src/components/settings/AgentSettings.tsx`.
- Sidecar port is dynamic in production (`--urls http://127.0.0.1:0` in
  `src-tauri/src/sidecar.rs`) — the MCP URL must be resolved from bound addresses at runtime
  (`IServerAddressesFeature`), not config.

## Session mapping

- One agent _process_ per ACP profile: lazy-spawned on first use, kept alive, killed on profile
  switch, idle timeout, or sidecar shutdown. Crash → fail pending requests, respawn on next turn.
- One ACP _session_ per SwebKit `sessionId` (global page, each contextual panel, proactive-insight
  seeds), created lazily on the first turn — `mcpServers` are fixed at `session/new`, so the
  mode/area/scope gates are baked into the MCP URL query
  (`/mcp?mode=ask&area=Aks&scope=feature`). If the gates change for an existing session, the ACP
  session is recreated (agent-side transcript resets; the SwebKit history mirror in
  `AgentSessionStore` is unaffected).
- `session/clear` drops the ACP session mapping (`session/close` if advertised, else forget).

## Prompt / context / history

- ACP has no system-prompt channel: `AgentSystemPromptBuilder` output (including the contextual
  panel's "current focus") is prepended to the first `session/prompt` of each new ACP session;
  later turns send the raw user message (the agent owns its transcript — `request.History` is
  ignored on this path).
- `AgentSessionStore` still records user+assistant messages (status endpoint and history count
  keep working).
- `AgentContextBudgetPlanner` skips rolling summarization for ACP profiles — the agent manages
  its own context; `usage_update` feeds `ContextUsagePercent` when reported.

## Streaming map (`session/update` → `AgentStreamEvent`)

| ACP update                             | Stream event                                                                                         |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `agent_message_chunk`                  | `token`                                                                                              |
| `agent_thought_chunk`                  | new `thought` kind (optional; may be dropped in phase 1)                                             |
| `tool_call`                            | `toolCallStarted` (`toolName` = ACP title)                                                           |
| `tool_call_update` (completed/failed)  | `toolCallResult`; also `tool_call`/`tool_result` step pairs so `AgentReasoningTrace` works unchanged |
| `plan`                                 | optional `plan` event (or folded into a step)                                                        |
| `usage_update`                         | carried on `done` → `ContextUsagePercent`                                                            |
| `session/prompt` response `stopReason` | `done` (`end_turn`/`max_tokens`/`max_turn_requests`), `error` (`refusal`), `done`+note (`cancelled`) |

AbortController / CancellationToken → `session/cancel` notification.

## Permissions

`session/request_permission` arrives as an agent→client request _while_ `session/prompt` is in
flight. Default (`RequireToolApproval=false`): respond immediately with the request's
`allow_once` option (fall back to first `allow_*`). Toggle on: park the request in
`AcpPermissionStore` (id → TaskCompletionSource), emit a `permissionRequired` SSE event, resolve
via `POST /api/agent/acp/permissions/{id}/respond {optionId}` — same poll + card pattern as
`/pending-approvals`, 5-minute expiry.

## fs/terminal

`clientCapabilities` advertises no `fs`/`terminal`/`elicitation`. Agent calls to those methods
get `-32601 Method not found`. `EnableFileSystem`/`EnableTerminal` profile fields ship disabled —
designed in, not implemented.

## Auth

`initialize` returns `authMethods`; surface them in the capability-test diagnostic (e.g.
"requires `claude` login"). The agent inherits the user's environment, so existing CLI auth just
works. An ACP auth-required error from `session/new`/`session/prompt` maps to a clear chat error.
Interactive `authenticate` is a follow-up.

## Windows spawning

`npx`/`gemini` are `.cmd` shims on Windows — resolve via PATHEXT (`npx.cmd`) or wrap in
`cmd.exe /c`. `CreateNoWindow=true`, `UseShellExecute=false`, stdin/stdout/stderr redirected;
stderr lines → sidecar `ILogger`.

## Implementation steps

### Phase 0 — docs scaffolding (done with this commit)

- This folder; blurb in `docs/features/README.md`.

### Phase 1 — ACP transport & chat (no SwebKit tools yet)

1. `src/SwebKit.Core/Domain/ProviderKind.cs`: add `Acp`.
2. `src/SwebKit.Core/Domain/AgentProfile.cs`: add `Command`, `Arguments` (single string,
   shell-style split), `WorkingDirectory`, `EnvironmentVariables` (`Dictionary<string,string>`),
   `CredentialEnvVar` (env var name the resolved `CredentialKey` secret is injected as — e.g.
   `ANTHROPIC_API_KEY`), `RequireToolApproval` (default false), `EnableFileSystem`/`EnableTerminal`
   (default false, UI-hidden). `RequiresApiKey` → false for Acp.
3. `src/SwebKit.Core/Domain/AgentProfilePresets.cs`: `ClaudeAcp()` (command `npx`, args
   `-y @agentclientprotocol/claude-agent-acp`) and one native-ACP preset (Gemini CLI or Mistral Vibe —
   verify the current flag/package name at implementation time).
4. `src-sidecar/Services/Acp/AcpJsonRpcPeer.cs`: NDJSON JSON-RPC peer — line-reader task,
   id→TaskCompletionSource correlation, `SendRequestAsync`/`SendNotificationAsync`, inbound
   dispatch (`session/request_permission`, `session/update`; unknown agent→client methods →
   `-32601`), fail-all-pending on process exit/EOF.
5. `src-sidecar/Services/Acp/AcpProcessLauncher.cs`: executable resolution (PATHEXT/`.cmd`),
   spawn with redirected pipes, `CreateNoWindow`, env merge, stderr→logger.
6. `src-sidecar/Services/Acp/AcpAgentHost.cs`: per-profile process lifetime —
   `EnsureStartedAsync` (spawn + `initialize` with `protocolVersion: 1`, clientInfo, empty
   capabilities), cached `AgentCapabilities`/`authMethods`,
   `EnsureSessionAsync(swebKitSessionId, mcpUrl)` → `session/new`, `PromptAsync` streaming
   `session/update`, `CancelAsync` → `session/cancel`, crash detection + respawn, dispose kills
   the process. Serialize prompts per ACP session.
7. `src-sidecar/Services/Acp/AcpAgentModelClient.cs`: `IAgentModelClient` impl —
   `ChatStreamAsync` maps the update stream; `ChatAsync` drains it; `CompleteAsync` runs a
   one-shot throwaway session (keeps `ProactiveInsightService` working).
8. `src-sidecar/Services/AgentModelClientRouter.cs`: `IAgentModelClient` delegating to
   `OpenAiCompatibleAgentClient` or `AcpAgentModelClient` by active profile `Provider`; update the
   `Program.cs` registration.
9. `AgentContextBudgetPlanner`: skip summarization when the active profile is Acp.
10. `AgentEndpoints.TestProfileAsync`: ACP branch — spawn + `initialize`, report
    `agentInfo`/protocol/`mcpCapabilities`/`authMethods` as the diagnostic; capability =
    `ToolCalling` on successful handshake.
11. `web/src/lib/types.ts`: extend `AgentProfile` (`provider: "Acp"` + new fields).
12. `web/src/components/settings/AgentSettings.tsx`: provider option "External agent (ACP)";
    conditional fields (command, args, cwd, env, credential env var, require-approval checkbox);
    hide baseUrl/model/credential key for Acp; update `isConfigured` and the new-profile factory.

### Phase 2 — MCP tool bridge + permissions

13. Add `ModelContextProtocol.AspNetCore` (pin a version ≥7 days old) to
    `Directory.Packages.props` + sidecar csproj; `AddMcpServer().WithHttpTransport()` +
    `app.MapMcp("/mcp")` (localhost-bound like everything else; CORS already restricts browser
    origins).
14. `src-sidecar/Services/Acp/SwebKitMcpToolProvider.cs`: wraps every `IAgentTool` as a dynamic
    `McpServerTool` (name/description/`ParametersSchema` pass through; invocation →
    `AgentToolRegistry.ExecuteAsync`). Reads `mode`/`area`/`scope` from the MCP URL query via
    `IHttpContextAccessor` and applies the same gates as
    `AgentToolCallOrchestrator.ResolveTools` — extract the filter predicate into a shared helper,
    no duplicated gating logic. `propose_*` tools need zero changes.
15. `AcpAgentHost`: build the `mcpServers` entry from the sidecar's bound port at runtime. If
    `initialize` shows no `mcpCapabilities.http`, record a diagnostic and continue without tools
    (stdio→HTTP shim mode `--mcp-stdio <url>` on the same binary = documented follow-up, not this
    phase).
16. `src-sidecar/Services/Acp/AcpPermissionStore.cs` + endpoints
    `GET /api/agent/acp/permissions`, `POST /api/agent/acp/permissions/{id}/respond`.
17. `web`: `useAcpPermissions` poll hook + `AcpPermissionCard` (clone `PendingActionCard` shape),
    mounted wherever `usePendingActionsFeed` is used; `permissionRequired` stream event kind →
    surfaces in the "Thinking…" line.

### Phase 3 — polish & docs

18. `thought`/`plan`/`usage` stream kinds rendered (`AgentReasoningTrace` / context indicator);
    `session/set_mode` mapping for `ask`/`ask_and_do` when the agent advertises modes;
    `session/list`-backed resume when `loadSession`/`sessionCapabilities.resume` is advertised.
19. Update `docs/architecture/functionalities/agent.md` (still describes the MAUI/Mistral
    implementation — refresh the provider section), `docs/features/README.md`,
    `docs/architecture/index.md` routing.
20. Aikido scan per `docs/security/aikido-mcp-scan.md` (new NuGet dep + spawned-process surface).

## Files to create/modify (by layer)

- **Core/domain:** `ProviderKind.cs`, `AgentProfile.cs`, `AgentProfilePresets.cs` (modify)
- **Sidecar:** `Program.cs` (router + MCP + hosted service),
  `Services/Acp/{AcpJsonRpcPeer,AcpProcessLauncher,AcpAgentHost,AcpAgentModelClient,AcpPermissionStore,SwebKitMcpToolProvider}.cs`
  (new), `Services/AgentModelClientRouter.cs` (new), `Services/AgentContextBudgetPlanner.cs`
  (skip-summarize guard), `Services/AgentToolCallOrchestrator.cs` (extract shared gate
  predicate), `Endpoints/AgentEndpoints.cs` (test-profile branch + permission endpoints) (modify)
- **Web:** `lib/types.ts`, `lib/hooks/useAgent.ts` (+`useAcpPermissions`),
  `components/settings/AgentSettings.tsx`, `components/agent/AcpPermissionCard.tsx` (new), mount
  points `AgentPage`/`GlobalAgentPanel`/`ContextualAssistant`
- **Tests:** see `test-plan.md`

## Risks / caveats

- **Semantic mismatch:** ACP models coding agents in editors; SwebKit is an ops console. It works
  because the copilot only needs prompt→text+tools, but agents may emit editor-centric updates
  (diffs, file refs) that are meaningless here — render as plain tool-call text.
- **Node dependency:** `claude-agent-acp` needs `npx` on PATH; detect at capability-test time and
  produce an actionable diagnostic. Claude auth relies on the user's existing `claude` login —
  document it.
- **Protocol drift:** v2 is a draft with breaking changes; pin to v1 and negotiate via
  `initialize`.
- **Process security:** arbitrary `command`/`args` from settings is local-user-equivalent (same
  trust level as the app itself) — no remote input reaches it, but call it out in the threat
  note anyway.
- **Prompt-only context:** injected context is prompt-stuffed once per session; agents that
  aggressively compact may lose it (re-inject per prompt if cheap — decide during
  implementation).
