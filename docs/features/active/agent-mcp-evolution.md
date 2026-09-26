# Agent MCP Evolution

**State:** `In Progress`

## Goal

Make the agent harness smarter and more composable:

1. Let ACP profiles attach **external MCP servers** alongside `swebkit` tools (e.g. Azure MCP for App Insights breadth beyond our native `query_logs`/`get_metrics`).
2. Reduce the tool calls a good answer needs — more composite investigative tools, tighter projections, better descriptions — instead of more primitive tools.
3. Productize `SwebKitToolsMcpBridge` as a **standalone MCP endpoint** usable from Claude Desktop/Code and other MCP clients.
4. (Later) Give non-ACP providers parity via an MCP *client* adapter.

Architecture background: `docs/architecture/ai-and-mcp.md`.

## Scope

- `AgentProfile` gains an `ExtraMcpServers` list (name + url/command transport); `AcpAgentHost.EnsureSessionAsync` appends them to `session/new → mcpServers` after the `swebkit` entry.
- Settings UI: per-profile MCP server editor; visible capability summary on the chat surface.
- Tool-quality pass: composite `resolve_workload_config`-style tools where multi-step flows recur; description improvements ("when NOT to use"); in-turn memoization of identical read calls.
- Standalone MCP: documented config snippets for common clients + a read-only allowlist mode for the bridge.

## Non-goals

- ACP `fs/*`/`terminal` client capabilities — remain off (reserved profile flags stay inert).
- A *new* native App Insights integration — `query_logs`/`get_metrics` already cover KQL+metrics; expansion is a separate product decision, tracked here only as "revisit if needed".
- Auth for non-loopback MCP clients — loopback desktop only in these phases.
- Exposing `propose_*`/mutation tools on the standalone profile.

## Phases

### Phase 1 — External MCP passthrough

- [x] `AgentProfile.ExtraMcpServers` (list of `{ id, name, enabled, transport: http|stdio, url, headers, command, arguments, environmentVariables }`) persisted in `AgentConfig`.
- [x] `EnsureSessionAsync`: `BuildMcpServers` = `swebkit` first + enabled extras; warn-and-skip invalid entries and http extras when the agent lacks `mcpCapabilities.http`; enabled extras folded into the session spec so edits force a fresh session.
- [x] Approval default: the settings UI flips `requireToolApproval` on when the first enabled extra is attached (their tools lack our propose/confirm gate) — user-overridable, backend semantics unchanged.
- [x] Settings UI: `McpServersEditor` rows on the ACP profile card (enabled/name/transport/url/command+args, headers/env as KEY=VALUE).
- [x] Docs: `ai-and-mcp.md` §6 updated with real config shape.
- [x] Tests: 5 `BuildMcpServers` unit tests + settings e2e (round-trip persistence, auto-approval flip).

### Phase 1b — Harness quality (minimal-tool-call answers)

- [ ] Audit read tools for projection shape; add purpose-shaped projections where raw dumps leak through.
- [ ] Composite tool(s) for the recurring env-var/config recipe (pod → workload YAML → configmap/secret → resolved value).
- [ ] Tool description pass: cheaper-alternative + when-not-to-use hints.
- [ ] In-turn memoization of identical read calls (keyed by name+args hash, turn-scoped).

### Phase 2 — Standalone MCP

- [ ] Read-only bridge mode (explicit allowlist profile, no `propose_*`).
- [ ] Docs + config snippets (Claude Desktop, Claude Code, etc.).
- [ ] Verify credential-resolution paths hold under direct MCP calls (sidecar must be running; document it).

### Phase 2b — MCP client adapter (parity for local providers)

- [ ] `IAgentTool` shim that proxies calls to an external MCP server; registered per-profile.
- [ ] Same allowlist/selection semantics as native tools.

## Test plan

- Sidecar tests: `mcpServers` array construction (ordering, disabled entries, capability mismatch skip), session spec change → re-create, permission default flip.
- Tool tests: new composite tool projections; memoization unit test.
- Bridge tests: read-only mode rejects mutation tools; standalone URL without `?tools=` honours configured profile.
- e2e: settings UI round-trip for extra MCP servers (demo/mock server); permission card still renders for external tool calls.

## Validation results

_Not started._

## Decisions

- **Passthrough before native integrations.** External MCPs give breadth (Azure MCP covers App Insights/Log Analytics and far more) with no domain code; native tools are reserved for what becomes a *product* feature needing our scoping/projection/confirmation semantics.
- **External tools gated at the provider layer.** They don't know `propose_*`; `RequireToolApproval` becomes the default safety when extras exist.
- **Composite tools are the "smartness" lever** — deterministic server-side fan-out beats teaching the model multi-step recipes; measure by tool calls per answer, not answer latency.
- **The bridge IS the standalone MCP** — we harden/document an endpoint that already exists rather than building a parallel server.
