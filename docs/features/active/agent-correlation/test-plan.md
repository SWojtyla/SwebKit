# Agent Correlation — Test Plan

## Unit tests — `tests/SwebKit.Sidecar.Tests`

| Area | Tests |
| --- | --- |
| `AgentSystemPromptBuilder` | scoped turn (area context + feature scope) lists configured-but-fenced areas; workspace scope or no context omits the section; Observability never listed (exempt); section absent when `hasToolCalling` false |
| `SwebKitToolsMcpBridge` | known-but-disallowed tool → `tool_out_of_scope` payload with area + remediation message; unknown tool → generic error; both `IsError`; allowlist-absent calls never produce out-of-scope |
| `OutOfScopeCallTracker` | rejection increments under the `?tools=` key; unrelated keys unaffected |
| `AcpAgentModelClient` | `done` result carries `SuggestedScope="workspace"` when tracker grew during the turn; absent otherwise |
| `AgentEndpoints` wire | `suggestedScope` serializes into the `done` event payload |
| `InvestigateWorkspaceIssueTool` | Storage node now invokes `analyze_storage_health` and returns a real report instead of `skipped`; other areas unchanged |
| `WorkspaceRelationshipSuggestionService` | pod-log-only match yields a suggestion with the log-sourced `Reason`; config-sourced matches keep the config `Reason`; log-fetch failure degrades gracefully (config suggestions still returned); existing-pair suppression unchanged |

## Unit tests — `tests/SwebKit.Agents.Tests`

- `AnalyzeStorageHealthTool`: fake `IStorageClient` → reachable account reports
  containers/properties; unreachable → `error` payload per the `{"error":...}`
  convention; correct `FeatureArea.Storage`/`ToolKind.Read`.

## Frontend — `npm --prefix web run test:unit`

- `ContextualAssistant`: `{provider:"Acp", capability:"Unknown"}` → workspace-scope
  checkbox enabled; `{provider:"LmStudio", capability:"Unknown"}` → still disabled
  with reason text; `ChatOnly` still disabled for all providers.
- Retry chip: `done` event with `suggestedScope:"workspace"` → chip renders; click →
  resends last user message with `scope:"workspace"`; no flag → no chip.
- Thought rendering: `thought` events accumulate into a collapsed reasoning block;
  attached to the assistant turn on `done`; absent events → unchanged rendering.

## Manual / E2E

- Real ACP agent (`npx -y @agentclientprotocol/claude-agent-acp`, Node + `claude`
  login — user prerequisite): Ask AI on a crashlooping demo pod in a feature-scoped
  panel → agent investigates pod, hits a Service Bus tool, gets the
  `tool_out_of_scope` hint → either tells the user to widen scope or the retry chip
  appears; retrying with workspace scope lets it call `analyze_queue_health`.
- Demo mode (`SWEBKIT_APPDATA_ROOT` sandbox): the workspace-intelligence demo walk
  still works; `investigate_workspace_issue` across a demo topology now reports on
  Storage nodes instead of skipping them.
- Reasoning blocks render in all three chat surfaces for ACP profiles and are absent
  (no regressions) for LM Studio profiles.

## Regression watch

- `SidecarAgentChatServiceFilteringTests` — new `Build` signature ripples; the
  area/mode/scope gates themselves must be unchanged (run the full filtering suite).
- `SwebKitToolsMcpBridgeTests` — error payload shape change is intentional; update
  existing assertions to the new codes.
- `ProactiveInsightService` uses `CompleteAsync` (one-shot, no tools) — must not be
  affected by prompt or tracker changes.
