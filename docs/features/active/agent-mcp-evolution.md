# Agent MCP Evolution

**State:** `Review`

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
- A *new* native App Insights integration — `query_logs`/`get_metrics` already cover KQL+metrics; user-confirmed out of scope while Azure MCP passthrough covers breadth.
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

- [x] Projection audit: read tools were already bounded (SB messages clamp+project, SQL 50 rows, pod-log tail, blob paging); the one gap was `list_pods` — now capped at 200 rows, unhealthy-first ordering, `truncated` flag.
- [x] Composite tool: `resolve_pod_env` — pod spec `env`/`envFrom` resolved end-to-end (configMapKeyRef → inline values, secretKeyRef → masked, fieldRef → noted), one call instead of pod-YAML + N manifest fetches; referenced objects fetched once per call; `pod_name`/`namespace` default to the UI selection.
- [x] Description pass: `list_pods` (prefer `get_pod_status`/`investigate_pod_issue`), `get_resource_yaml` (prefer `resolve_pod_env` for env questions), `get_queue_messages` (prefer stats/health for counts).
- [x] In-turn memoization: `BuildStepTrackingToolExecutor` caches `ToolKind.Read` results keyed by name+args for the turn's executor lifetime; mutations and errors never cached; ACP bridge deliberately not memoized (no turn boundary).

### Phase 2 — Standalone MCP

- [x] Read-only bridge mode: absent `?tools=` ⇒ read tools only (`propose_*` hidden + `tool_read_only` error on call); `?mode=full` opt-in for the full surface. Internal ACP sessions are unaffected — they always pass an explicit allowlist.
- [x] Docs + config snippets (Claude Desktop `claude_desktop_config.json`, Claude Code `claude mcp add`) — `docs/architecture/ai-and-mcp.md` §6.2a.
- [x] Credential-resolution verified: tools execute sidecar-side via `ICredentialStore`, so direct MCP calls work while the sidecar runs (loopback-only bind — no auth layer needed; documented caveat).

### Phase 2b — MCP client adapter (parity for local providers) ✅

- [x] `ExternalMcpToolSource` (sidecar): `ExtraMcpServers` → cached `McpClient` per server config → external tools exposed as `mcp_{server}_{tool}` `ToolDefinition`s (`FeatureArea.External`, appended post-area-filter = area-exempt). Routing via `externalExecutors` in the step-tracking executor — registry untouched, per-turn memoization applies.
- [x] Mutation pipeline: `readOnlyHint` tools execute directly; everything else becomes a `ToolKind.Mutate` proposal — "calling" it registers an `ExternalMcpCall` pending action (payload = serialized server config + tool + args) confirmed via the existing pending-approvals endpoint; `ExternalMcpActionExecutor` is the only place a mutating remote call fires. `ask` mode never sees them (Mutate gate); `destructiveHint` → High risk.
- [x] Dead servers skipped per-turn, never break the chat, retried next turn.
- [x] Selection semantics: external tools don't consume `sel=`/`AgentExecutionContext` — documented; no action possible (foreign servers don't know our selection model).
- [x] Wire-verified: `ExternalMcpToolSourceWireTests` hosts a real in-process MCP server and exercises handshake → list → classify → call end-to-end.

## Test plan

- Sidecar tests: `mcpServers` array construction (ordering, disabled entries, capability mismatch skip), session spec change → re-create, permission default flip.
- Tool tests: new composite tool projections; memoization unit test.
- Bridge tests: read-only mode rejects mutation tools; standalone URL without `?tools=` honours configured profile.
- e2e: settings UI round-trip for extra MCP servers (demo/mock server); permission card still renders for external tool calls.

## Validation results

- .NET: **2023/2023** (Sidecar 590 · Agents 268 · Core 810 · Azure 151 · K8s 158 · Sql 46)
- Frontend: **545/545** vitest · tsc/vite/eslint clean
- e2e: settings external-MCP round-trip + auto-approval flip green
- Branch: `feat/agent-mcp-evolution` (rebased on main post-PR #106)

_User decision (2026-05):_ **Phase 3 native observability expansion is not needed** — native `query_logs`/`get_metrics` cover the in-context diagnosis case and Azure MCP passthrough covers breadth. Revisit only if App Insights ever becomes a dedicated UI surface. The mutation pipeline is no longer deferred — built in Phase 2b via `ExternalMcpCall` pending actions.

## Decisions

- **Passthrough before native integrations.** External MCPs give breadth (Azure MCP covers App Insights/Log Analytics and far more) with no domain code; native tools are reserved for what becomes a *product* feature needing our scoping/projection/confirmation semantics.
- **External tools gated at the provider layer.** They don't know `propose_*`; `RequireToolApproval` becomes the default safety when extras exist.
- **Composite tools are the "smartness" lever** — deterministic server-side fan-out beats teaching the model multi-step recipes; measure by tool calls per answer, not answer latency.
- **The bridge IS the standalone MCP** — we harden/document an endpoint that already exists rather than building a parallel server.
