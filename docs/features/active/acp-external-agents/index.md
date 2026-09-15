# ACP External Agents

## Status

`Planned`

## What this is

Add the [Agent Client Protocol](https://agentclientprotocol.com) (ACP) as a fourth agent provider
kind, so external agents — Claude (via Zed's `claude-agent-acp` adapter), Gemini CLI, Codex,
Mistral Vibe, and other ACP-compatible agents — can drive the existing SwebKit assistant instead
of an OpenAI-compatible chat endpoint.

SwebKit plays the ACP *client* role: it spawns the agent as a child process and speaks JSON-RPC
2.0 (newline-delimited JSON) over its stdin/stdout.

## Goals

- Let the user pick an ACP agent profile in Settings → AI Agent, alongside the existing
  LM Studio / OpenAI-compatible / Mistral profiles.
- Stream agent output into the existing chat UI (token streaming, tool-call steps, cancel).
- Give the agent access to SwebKit's own tools (`list_pods`, `get_queue_stats`, `propose_*`
  mutations, …) via an MCP server exposed by the sidecar and passed through
  `session/new` → `mcpServers` — preserving demo mode, per-area scoping, ask/ask_and_do gating,
  and the pending-approvals mutation flow.
- Keep `fs/*`/`terminal/*` client capabilities off (the agent cannot touch the local filesystem
  or shell outside SwebKit's tool sandbox).
- Auto-approve `session/request_permission` by default (SwebKit tools are already
  self-gating — mutations only create pending-action proposals), with a per-profile
  "require approval" toggle.

## Non-goals

- Embedded coding-agent use (file editing / shell access on the user's machine) — capability
  fields are designed in but ship disabled.
- Interactive `authenticate` / terminal-auth flows — agents are expected to use their own CLI
  login (e.g. `claude`); auth requirements surface as capability-test diagnostics.
- Remote/HTTP-transport agents — ACP over HTTP is still a draft; stdio only.

## Key design decisions (confirmed)

| Decision | Choice |
| --- | --- |
| Primary purpose | Stronger copilot brain — replaces the LLM behind the existing assistant, not a coding agent |
| SwebKit tool access | MCP bridge: sidecar exposes `AgentToolRegistry` as a streamable-HTTP MCP endpoint, handed to the agent at `session/new` |
| fs/terminal capabilities | Off; agent calls to `fs/*`/`terminal/*` get JSON-RPC `-32601` |
| ACP client host | .NET sidecar (`src-sidecar`) — reuses sessions, SSE streaming, status, and pending approvals |
| Permission requests | Auto-approve by default + per-profile `RequireToolApproval` toggle surfacing an approval card |
| Agent targets | Generic `command`/`args` profile fields + presets (Claude via `npx -y @zed-industries/claude-agent-acp`, one native-ACP agent) |

## Dependencies

- Builds on the provider seam created by `ai-augmented-app` (`IAgentModelClient`, `AgentProfile`,
  `AgentToolCallOrchestrator`) and the session/streaming infrastructure of
  `workspace-intelligence`.
- New NuGet dependency: `ModelContextProtocol.AspNetCore` (MCP server hosting for the tool bridge).
- Runtime prerequisites on the user's machine, per agent: e.g. Node/`npx` for
  `claude-agent-acp`, and the agent's own authentication (Claude Code login, Gemini login, …).

## Documents

- `technical-plan.md` — full design and phased implementation plan
- `test-plan.md` — test scope and scenarios
- `status.md` — current state and handoff notes
