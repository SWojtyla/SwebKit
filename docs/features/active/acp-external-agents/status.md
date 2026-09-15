# ACP External Agents — Status

## State

`In Progress` — implemented on `sw/feature/acp-external-agents`. Transport, host, model client,
MCP bridge, permission path, and settings UI are all in place and building; unit coverage lands
in `tests/SwebKit.Sidecar.Tests` (40 ACP tests) and `web` (env-var editor helpers).

## What was decided

Planning session 2026-09-15 (user-confirmed choices):

1. **Goal:** stronger copilot brain — an ACP agent acts as the model backend for the existing
   SwebKit assistant, not an embedded coding agent.
2. **Tool access:** yes, via an MCP bridge. ACP agents cannot consume client-side function
   tools; `session/new` → `mcpServers` is the supported channel, so the sidecar exposes
   `AgentToolRegistry` as a streamable-HTTP MCP endpoint.
3. **fs/terminal:** off initially — `initialize` advertises no client capabilities.
4. **Host:** the .NET sidecar, not the Tauri/Rust layer.
5. **Permissions:** `session/request_permission` auto-approved by default (mutations are
   self-gating through `propose_*` → pending actions), per-profile toggle to require approval.
6. **Agents:** generic `command`/`args` config + presets.

## What landed

- `src-sidecar/Services/Acp/`: `AcpJsonRpcPeer` (newline-delimited JSON-RPC over stdio),
  `AcpProcessLauncher` (Windows `.cmd`/PATHEXT resolution, quoting-aware arg split, stderr pump),
  `AcpAgentHost` (process + session lifecycle, `session/update` routing, `session/cancel`,
  permission dispatch), `AcpAgentModelClient` (`session/update` → existing `AgentStreamEvent`s,
  system-prompt stuffing per session), `AcpPermissionStore`, `SwebKitToolsMcpBridge`.
- `AgentModelClientRouter` dispatches `IAgentModelClient` calls per active profile.
- `ProviderKind.Acp`, `AgentProfile` ACP fields, `AgentProfilePresets.ClaudeAcp`/`GeminiCli`.
- MCP bridge at `POST /mcp/swebkit-tools` (stateless streamable HTTP, `ModelContextProtocol.AspNetCore`
  2.2.0); the per-session `?tools=` allowlist carries the mode/area/scope gates.
- Settings UI: "External agent (ACP)" provider with preset picker, command/args/cwd/env fields,
  credential-key → env-var injection, and the approval toggle.
- Permission cards (`AcpPermissionCard`) in all three chat surfaces; `permissionRequired` SSE
  event invalidates the permission poll immediately.
- `session/clear` also drops the ACP session so "Clear" really resets the conversation.

## Remaining

- Manual end-to-end verification against a real agent (`npx -y @agentclientprotocol/claude-agent-acp`
  requires Node + a `claude` login).
- Thought/plan update kinds are parsed (`agent_thought_chunk` → `thought` event) but not yet
  rendered in the chat UI; `usage_update` feeds `contextUsagePercent` already.
- `session/set_mode` is not wired (ACP modes don't map cleanly onto ask/ask_and_do).
- fs/terminal capability handlers remain designed-in but unimplemented.

## Notes for reviewers

- The plan doc referenced `@zed-industries/claude-agent-acp`; the published package is
  `@agentclientprotocol/claude-agent-acp` — presets use the latter.
- Aikido flags `Process.Start` in `AcpProcessLauncher` (command injection class): assessed as
  by-design — the command is the user's own local config, spawned with `UseShellExecute=false`
  and `ArgumentList` so nothing is shell-interpreted. See the comment at the `Process.Start` call.
