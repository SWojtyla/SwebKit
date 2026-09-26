# Agent

## Overview

The SwebKit Agent is a React + .NET sidecar copilot for investigating the operator's configured AKS, Service Bus, Redis, SQL, Storage, API Client, Monitoring, and Application Insights resources. It supports read-only investigation and approval-gated proposed mutations.

Chat appears in three forms that share the same sidecar/session model:

- `/agent` full-page conversation and visualization workspace;
- `GlobalAgentPanel`, docked from the shell/dashboard; and
- `ContextualAssistant`, opened from a selected feature resource.

The frontend streams responses over SSE. `SidecarAgentChatService` owns context budgeting, prompt construction, provider routing, tool orchestration, sessions, and pending actions.

> How the tool model, MCP bridge, and ACP plumbing actually work — with wire schemas and diagrams — lives in `docs/architecture/ai-and-mcp.md`.

## Provider Model

`AgentModelClientRouter` selects an `IAgentModelClient` from the active `AgentProfile`:

- `OpenAiCompatibleAgentClient` — LM Studio, Mistral, and other OpenAI-compatible chat/tool endpoints;
- `AcpAgentModelClient` — external Agent Client Protocol processes.

Profiles are configured/tested in Settings. Credentials are stored by reference rather than returned to the React client. Capability testing records whether a provider supports chat/tools and exposes sanitized diagnostics.

## Request Flow

```text
React chat surface
  → POST /api/agent/chat/stream
  → SidecarAgentChatService
      → AgentContextBudgetPlanner
      → AgentSystemPromptBuilder
      → AgentToolCallOrchestrator resolves allowed tools
      → AgentModelClientRouter
          → OpenAiCompatibleAgentClient, or
          → AcpAgentModelClient
      → SSE AgentStreamEvent frames
  → shared conversation store / reasoning / visualization / approval UI
```

The stream carries assistant tokens, thought/reasoning updates, tool activity, visualization payloads, summary/context notices, pending actions, completion, and error events. Cancellation stops the active provider request; clearing removes the local conversation and associated ACP session.

## Tool Safety

Tools implement `IAgentTool` and are discovered through `AgentToolRegistry`. `AgentToolCallOrchestrator` filters them per request using:

- feature area;
- resource/workspace scope;
- `ask` versus `ask_and_do` mode;
- active profile capability; and
- explicit tool fences.

Read tools execute directly. Mutating tools are named/provided as `propose_*` operations and create a pending action rather than applying immediately. The frontend renders `PendingActionCard`; confirm routes to the matching action executor, reject removes the proposal, and expired actions are reconciled out of the feed.

Authorization failures crossing `AgentToolRegistry.ExecuteAsync` are classified by `AccessAdvisor` (`SwebKit.Core/Security`) — a duck-typed recognizer for Azure SDK 401/403, Service Bus AMQP `Unauthorized`, k8s 403, `SqlException` 229/230/297, and Redis NOAUTH/NOPERM — and returned to the model as a structured `{"status":"access_denied", capability, featureArea, requiredAccess, guidance, detail}` rather than a flattened `{"error": …}`. `requiredAccess` names the least-privilege fix per feature area (e.g. `Azure Service Bus Data Receiver`, `Storage Blob Data Reader`, `Monitoring Reader`, `VIEW DEFINITION`/`db_datareader`). The system prompt instructs the model not to retry denied calls and to close investigations with an "Access gaps" section — in locked-down environments this is the difference between a raw 403 and an actionable access request.

Current tool families cover AKS, Service Bus, Redis, SQL, Storage, API Client, Monitoring, Application Insights, workspace investigation, and current screen state.

## Context

### System prompt and workspace maps

`AgentSystemPromptBuilder` includes the current feature/resource scope and a bounded rendering of `AppConfig.Maps`. Nodes are grouped by area, relationships are rendered as `from → to (label)`, and caps prevent large maps from consuming the context window. `investigate_workspace_issue` can walk those relationships live; a missing map match never blocks direct investigation.

### Screen state

Feature pages publish bounded whitelisted snapshots through `useScreenStateProvider` to `POST /api/agent/screen-state`. `ScreenStateStore` retains the latest snapshot for five minutes, and `GetScreenStateTool` returns it on demand.

Serializers expose only the fields needed to reason about the visible UI. They must not include auth headers, tokens, connection strings, complete message bodies, or other secrets.

### Context budgeting

`AgentContextBudgetPlanner` bounds history, workspace data, screen state, and tool results. Long conversations can be summarized; the React UI surfaces the summary boundary and context-usage indicator instead of silently dropping history.

## ACP External Agents

For `ProviderKind.Acp`, `AcpAgentHost` launches the configured process and `AcpJsonRpcPeer` speaks newline-delimited JSON-RPC over stdio:

```text
initialize → session/new → session/prompt
session/update → AgentStreamEvent
session/cancel → provider cancellation
```

The external agent owns its transcript and internal loop. SwebKit exposes permitted domain tools through `SwebKitToolsMcpBridge`, a stateless streamable-HTTP MCP endpoint. The bridge URL includes the orchestrator's per-request allowlist; an empty allowlist exposes no tools.

Profiles may also attach **external MCP servers** (`AgentProfile.ExtraMcpServers`, http or stdio) — forwarded to the agent in the same `session/new` alongside the SwebKit entry. Calls to them bypass the propose/confirm pipeline, so `RequireToolApproval` is their only gate (the settings UI enables it by default on first attach). Wire details in `docs/architecture/ai-and-mcp.md`.

SwebKit does not advertise filesystem or terminal ACP client capabilities. Calls to unsupported client methods receive JSON-RPC method-not-found.

ACP permission requests are auto-approved by default. With `RequireToolApproval`, `AcpPermissionStore` parks requests for up to five minutes and the chat renders `AcpPermissionCard` using:

- `GET /api/agent/acp/permissions`
- `POST /api/agent/acp/permissions/{id}/respond`

This provider-level permission is separate from SwebKit's `propose_*` domain-action approval pipeline.

## Proactive Investigation

`ProactiveInsightService` subscribes to fired Monitoring alerts whose `AiInvestigationEnabled` flag is on. It:

1. Matches the fired resource against configured workspace maps when possible.
2. Runs a workspace-scoped, ask-mode investigation with mutation tools structurally unavailable.
3. Enforces tool-round and wall-clock budgets.
4. Persists a report and seeds a dedicated conversation session.
5. Publishes `InsightReady` over Monitoring SSE.

The always-mounted shell subscription emits the OS notification/in-app toast. Dashboard and Monitoring cards deep-link to the persisted report; they do not fabricate chat responses.

## Frontend State

- `useAgentConversationStore` owns the shared global conversation.
- `useAgentPanelStore` owns docked-panel open state and queued dashboard prompts.
- `useAgentChatStream` is the single stream owner for a chat surface.
- `AgentReasoningTrace`, `AgentThoughtBlock`, and `AgentSummarizedNotice` render model-process metadata.
- `AgentVisualizationPanel` hosts Mermaid, topology, and timeline outputs in a resizable workspace.
- `AcpPermissionCard` and `PendingActionCard` handle the two approval layers.

## Main Code Locations

- `web/src/components/agent/AgentPage.tsx`
- `web/src/components/agent/GlobalAgentPanel.tsx`
- `web/src/components/agent/ContextualAssistant.tsx`
- `web/src/components/agent/PendingActionCard.tsx`
- `web/src/components/agent/AcpPermissionCard.tsx`
- `web/src/components/agent/AgentVisualizationPanel.tsx`
- `web/src/lib/hooks/useAgent.ts`
- `web/src/lib/hooks/useContextualAgent.ts`
- `web/src/lib/stores/agent-conversation.ts`
- `web/src/lib/stores/agent-panel.ts`
- `web/src/lib/stores/screen-state.ts`
- `src-sidecar/Endpoints/AgentEndpoints.cs`
- `src-sidecar/Services/SidecarAgentChatService.cs`
- `src-sidecar/Services/AgentModelClientRouter.cs`
- `src-sidecar/Services/AgentToolCallOrchestrator.cs`
- `src-sidecar/Services/AgentSystemPromptBuilder.cs`
- `src-sidecar/Services/AgentContextBudgetPlanner.cs`
- `src-sidecar/Services/Acp/`
- `src/SwebKit.Agents/IAgentModelClient.cs`
- `src/SwebKit.Agents/OpenAiCompatibleAgentClient.cs`
- `src/SwebKit.Agents/AgentToolRegistry.cs`
- `src/SwebKit.Agents/Tools/`

## Security Constraints

- Never place provider credentials or resource secrets in stream events, prompts, screen state, or logs.
- Mutation tools must remain proposal-only until explicit confirmation.
- `ask` mode and proactive investigations must not expose mutation tools.
- ACP tool access must use the resolved allowlist; never expose the entire registry by default.
- Keep workspace/screen/tool context bounded to protect provider context limits.
- External ACP processes are user-configured executables and must not receive filesystem/terminal client capabilities from SwebKit.

## Validation Pointers

- `web/e2e/agent.spec.ts`
- `web/e2e/contextual-assistant.spec.ts`
- `web/e2e/global-agent-panel.spec.ts`
- `web/src/components/agent/agent-reasoning-trace.test.ts`
- `web/src/components/agent/agent-visualization.test.ts`
- `web/src/components/agent/pending-action-card.test.ts`
- `tests/SwebKit.Agents.Tests/`
- `tests/SwebKit.Sidecar.Tests/AgentEndpointsTests.cs`
- `tests/SwebKit.Sidecar.Tests/Acp*Tests.cs`
