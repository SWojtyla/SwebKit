# ACP External Agents — Test Plan

## Scope

The ACP transport layer, process/session lifecycle, stream-event mapping, permission flow, MCP
tool bridge, profile/settings model, and the settings UI — plus regression coverage that the
existing LM Studio / OpenAI-compatible / Mistral path is untouched by the router.

## Unit tests — `tests/SwebKit.Sidecar.Tests`

| Area | Tests |
| --- | --- |
| `AcpJsonRpcPeer` | NDJSON framing (one message per line, no embedded newlines); request→response correlation by id; concurrent outbound requests; inbound request dispatch; unknown agent→client method → `-32601`; all pending requests fail on EOF/process exit; malformed line tolerance |
| `AcpProcessLauncher` | `.cmd`/`PATHEXT` resolution on Windows; env merge; `CreateNoWindow`/redirected pipes; spawn failure surfaces a usable diagnostic |
| `AcpAgentHost` | lazy spawn on first turn; `initialize` result cached; one ACP session per SwebKit sessionId; session recreate when mode/area/scope gates change; `session/cancel` on CT; process crash → pending fail + respawn; dispose kills process |
| `AcpAgentModelClient` | `session/update` → `AgentStreamEvent` mapping table (every row in technical-plan.md); `stopReason` → done/error mapping; permission auto-approve picks `allow_once` |
| `AcpPermissionStore` | park/resolve/expire lifecycle; respond to unknown id → not found |
| MCP tool provider | query-param gating matches `AgentToolCallOrchestrator.ResolveTools` (mode/area/scope + Observability exemption); tool invocation forwards to `AgentToolRegistry.ExecuteAsync`; `propose_*` results still enqueue pending actions |
| `AgentModelClientRouter` | delegates by active profile `Provider`; non-ACP profiles behave exactly as before |
| `AgentContextBudgetPlanner` | no summarization when active profile is Acp |
| `TestProfileAsync` (ACP branch) | handshake success → `ToolCalling` + agentInfo diagnostic; spawn failure / missing `npx` → actionable diagnostic; `authMethods` surfaced |

Test double: a scripted fake ACP agent — either a duplex-pipe in-memory peer for the transport
tests, or a tiny helper process (inline script) that answers `initialize`/`session/new`/
`session/prompt` with canned `session/update` sequences for host-level tests.

## Unit tests — `tests/SwebKit.Agents.Tests`

- `ProviderKind.Acp` serializes/deserializes (string-enum converter) and survives
  `AgentConfig.Migrate()` untouched.
- `AgentProfilePresets.ClaudeAcp()` shape; `RequiresApiKey` false for Acp.

## Frontend — `npm --prefix web run test:unit`

- Stream event mapping: new kinds (`thought`/`plan`/`permissionRequired`) tolerated; unknown
  kinds don't break `useAgentChatStream`.
- `AgentSettings`: provider select shows the ACP option; ACP-conditional fields render/hide
  correctly (testids per existing convention).
- `useAcpPermissions` + `AcpPermissionCard`: list renders, respond posts optionId, expired
  entries handled like `reconcilePendingActionsFeed`.

## e2e — Playwright (stretch)

- Spec against the fake ACP agent script: send a message, see streamed tokens + tool steps,
  cancel mid-turn.
- Not gating for phase 1; real-agent (Claude) verification is a manual step on the user's
  machine since it needs `npx` + a `claude` login.

## Regression / manual verification

- `dotnet test`, `npm --prefix web run test:unit`, `cargo test --lib` in `src-tauri`, `tsc` +
  lint per repo conventions.
- LM Studio profile still chats/tools/streams identically (router regression).
- Demo mode: ACP profile + demo data — agent answers "check my pods" through the MCP bridge
  against `DemoAksClient`.
- Manual: Claude profile → streamed chat, tool calls visible in reasoning trace, a mutation
  question produces a pending-action card (not a direct mutation), Stop sends `session/cancel`,
  Clear drops the ACP session.
