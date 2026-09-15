# ACP External Agents — Status

## State

`Planned` — design complete, no code written. Committed as a plan only; implementation is
expected on a dedicated branch.

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

## Handoff

Start from `technical-plan.md` — it is written to be implementable without further context.
Phase order matters: Phase 1 (transport + chat) before Phase 2 (MCP bridge), because the bridge
needs a working `session/new` to attach to.

Watch items called out in the plan: Windows `.cmd` spawn quirks (`npx` is `npx.cmd`), no
system-prompt channel in ACP (context is prompt-stuffed per session), protocol v2 is still a
draft (negotiate v1 via `initialize`), and `claude-agent-acp` requires `npx` plus an existing
`claude` login on the machine.
