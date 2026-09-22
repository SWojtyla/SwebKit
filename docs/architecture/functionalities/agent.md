# Agent

## Overview

The SwebKit Agent is an AI copilot that provides intelligent assistance for DevOps operations. It integrates Mistral AI's language model capabilities with SwebKit's existing services to help users diagnose and understand their Kubernetes clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, and observability data.

## Core Components

```
┌─────────────────────────────────────────────────────────────┐
│                      User Interface                            │
│  ┌─────────────────┐    ┌───────────────────────────────┐  │
│  │   AgentChatPanel │    │     ToolExecutionStatus        │  │
│  │   (Blazor)       │    │     (Progress indicators)     │  │
│  └────────┬────────┘    └──────────────┬────────────────┘  │
└───────────┼──────────────────────────┼────────────────────┘
            │                              │
            ▼                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Agent Services                            │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────┐ │
│  │  AgentChatService │  │  AgentToolRegistry│  │  Context    │ │
│  │  (Main orchestrator)│  │  (Tool discovery) │  │  Builder    │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬─────┘ │
│            │                   │                    │       │
└───────────┼───────────────────┼────────────────────┼───────┘
            │                   │                    │
            ▼                   ▼                    ▼
┌─────────────────────────────────────────────────────────────┐
│                    External Integrations                       │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────┐ │
│  │ Mistral AI      │  │ SwebKit Services  │  │ Credential  │ │
│  │ (Chat API)      │  │ (AKS, Service Bus │  │ Store       │ │
│  └──────────────────┘  │  Observability)   │  └────────────┘ │
│                         └──────────────────┘                │
└─────────────────────────────────────────────────────────────┘
```

## Architecture Flow

### 1. Data Flow

```
User Request → AgentChatService → Mistral AI → Tool Calls → AgentToolRegistry → Specific Tools → SwebKit Services → Response
```

### 2. Request Processing Flow

1. **User Input**: User types a query in the AgentChatPanel
2. **Context Building**: `IAgentContextBuilder` gathers relevant SwebKit context (active namespaces, configurations, etc.)
3. **System Prompt Construction**: `AgentChatService` builds a context-aware system prompt
4. **Mistral Analysis**: Mistral AI analyzes the request + context using the Mistral API
5. **Tool Discovery**: Mistral may request tool execution based on the analysis
6. **Tool Execution**: `AgentToolRegistry` dispatches to the appropriate tool via dependency injection
7. **Data Retrieval**: Tools fetch data from SwebKit services (AKS, Service Bus, Observability)
8. **Response Generation**: Tool results are fed back to Mistral for final response synthesis
9. **User Presentation**: Final response displayed in AgentChatPanel with markdown formatting

### 3. Tool Execution Flow

```
Mistral Request → AgentToolRegistry.ExecuteAsync() → Specific Tool.ExecuteAsync() → Service Call → JSON Response → Mistral
```

## Mistral Integration

### Core Interface: `IMistralClient`

The `IMistralClient` interface provides the core integration with Mistral AI:

```csharp
public interface IMistralClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        List<object>? history,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct);
}
```

### Implementation: `MistralHttpClient`

The `MistralHttpClient` implements the interface and handles:

- **API Authentication**: Uses API key from `MistralConfig` or credential store (`SwebKit-Agent:Mistral-ApiKey`)
- **Request Formatting**: Converts tools to Mistral's function calling format
- **Agentic Loop**: Handles the conversational loop where Mistral may call multiple tools
- **Tool Execution**: Executes tools via the provided `toolExecutor` callback
- **History Management**: Maintains conversation history for context

### Configuration: `MistralConfig`

```csharp
public sealed class MistralConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = "https://api.mistral.ai/v1";
    public string Model { get; set; } = "mistral-large-latest";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
}
```

### Agentic Loop Implementation

The `MistralHttpClient.ChatAsync()` method implements the agentic loop:

1. **Request Preparation**: Builds the request with system prompt, user message, tools, and history
2. **API Call**: Posts to Mistral's `/chat/completions` endpoint
3. **Response Processing**: Parses Mistral's response for tool calls or final text
4. **Tool Execution**: If Mistral requests tool calls, executes each tool via the `toolExecutor` callback
5. **Result Integration**: Feeds tool results back to Mistral for the next round
6. **Termination**: Returns final text response or stops after maximum rounds (5)

## Tool System

### Tool Interface: `IAgentTool`

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}
```

### Tool Registry: `AgentToolRegistry`

- **Discovery**: Automatically discovers all tools registered as `IAgentTool` via DI
- **Tool Definitions**: Provides `ToolDefinition` objects for Mistral with name, description, and parameters
- **Execution**: Routes tool calls from Mistral to the appropriate tool implementation

### Available Tools

**Kubernetes Tools (5):**

- `GetPodStatusTool` - Pod health and status information
- `GetPodLogsTool` - Fetch and analyze pod logs
- `ListPodsTool` - List pods with filtering
- `GetPodEventsTool` - Kubernetes events for pods
- `ListNamespacesTool` - List all namespaces

**Service Bus Tools (2):**

- `GetQueueStatsTool` - Queue statistics and metrics
- `GetQueueMessagesTool` - Retrieve messages from queues

**Observability Tools (2):**

- `QueryLogsTool` - Execute KQL queries against Application Insights
- `GetMetricsTool` - Retrieve metric data

### Demo Mode Support

All tools support demo mode when `AppState.UseDemoData` is true:

- **Kubernetes Tools**: Use `DemoAksClient` (injected as singleton)
- **Service Bus Tools**: Use `DemoServiceBusClient.OrdersDev()`
- **Observability Tools**: Use `DemoObservabilityProvider` (via `ObservabilityProviderFactory.Create()` with `useDemoData=true`)

This allows the agent to work without real API connections while maintaining realistic context and data structures.

## Context Building

### `IAgentContextBuilder`

Builds context information that's injected into the system prompt:

```csharp
public interface IAgentContextBuilder
{
    string BuildContext(AppStateService appState);
}
```

### Context Information

The context includes:

- Active Kubernetes namespaces and clusters
- Configured Service Bus namespaces
- Selected observability resources
- User preferences and settings

## Chat Service

### `AgentChatService`

Main orchestrator that:

- **Manages Conversations**: Uses `ConversationSession` to maintain message history
- **Builds System Prompts**: Combines template with current workspace context
- **Handles Tool Execution**: Coordinates tool calls between Mistral and tool registry
- **Tracks Metrics**: Records tools used and response times

### Conversation Management

- **History Limit**: Configurable maximum history messages via `UserSettingsRepository`
- **Session State**: Tracks conversation count and near-limit warnings
- **Clear Functionality**: Allows clearing conversation history

## Service Registration

### DI Configuration (MauiProgram.cs)

```csharp
// Mistral Client
builder.Services.AddSingleton<IMistralClient, MistralHttpClient>();

// Agent Services
builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();
builder.Services.AddSingleton<IAgentChatService, AgentChatService>();
builder.Services.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();

// Tools - registered as IAgentTool for automatic discovery
builder.Services.AddSingleton<IAgentTool, GetPodStatusTool>();
builder.Services.AddSingleton<IAgentTool, ListNamespacesTool>();
builder.Services.AddSingleton<IAgentTool, ListPodsTool>();
builder.Services.AddSingleton<IAgentTool, GetPodLogsTool>();
builder.Services.AddSingleton<IAgentTool, GetPodEventsTool>();
builder.Services.AddSingleton<IAgentTool, GetQueueStatsTool>();
builder.Services.AddSingleton<IAgentTool, GetQueueMessagesTool>();
builder.Services.AddSingleton<IAgentTool, QueryLogsTool>();
builder.Services.AddSingleton<IAgentTool, GetMetricsTool>();

// Demo clients
builder.Services.AddSingleton<DemoAksClient>();
```

## System Prompt Template

```
You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
application for platform engineers. You help users diagnose and understand their Kubernetes
clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, and
observability data.

Current workspace context:
{CONTEXT}

Guidelines:
- Be concise and technical. Prefer bullet points and tables over prose.
- When a user asks about pods, events, or logs, use the available tools to fetch live data.
- If a tool returns an error, explain what it means and suggest a resolution.
- Do not expose internal JSON schemas or tool names in your replies.
- If you are unsure, say so rather than guessing.
```

## Error Handling

- **API Errors**: Mistral API errors are caught and wrapped with status code information
- **Tool Errors**: Tool execution errors are caught and returned as JSON error objects
- **Missing Tools**: Unknown tool calls return error messages
- **Rate Limiting**: API errors include retry information when applicable

## Security Considerations

- **API Key Management**: Mistral API keys stored in credential store with key `SwebKit-Agent:Mistral-ApiKey`
- **Environment Variables**: Can also use `MISTRAL_API_KEY` environment variable
- **No Secret Exposure**: Tool schemas and responses don't expose sensitive information
- **Input Validation**: All tool inputs are validated against JSON schemas

## Performance

- **Tool Round Limit**: Maximum 5 tool call rounds per request to prevent runaway loops
- **History Limit**: Configurable maximum history messages to manage memory usage
- **Async Processing**: All tool execution is asynchronous for non-blocking operation
- **Caching**: Demo clients provide cached synthetic data for better performance in demo mode

## Testing

- **Unit Tests**: Basic tool instantiation and schema validation tests
- **Integration Tests**: End-to-end testing with Mistral API (requires API key)
- **Demo Mode Tests**: All tools tested in demo mode with synthetic data

## Related Components

- **AgentChatPanel**: Main UI component for agent interaction
- **ToolExecutionStatus**: Visual indicators for tool execution progress
- **AgentContextDisplay**: Shows current context information to users
- **Demo Clients**: Synthetic data providers for demo mode operation

## Current pipeline (Tauri + sidecar) and ACP external agents

> The sections above describe the original MAUI/Blazor implementation. In the current
> architecture the chat surfaces are React, the orchestrator is `SidecarAgentChatService` in
> `src-sidecar`, and the model seam is `IAgentModelClient` — with `AgentModelClientRouter`
> dispatching per active `AgentProfile`: `OpenAiCompatibleAgentClient` for
> LM Studio / OpenAI-compatible / Mistral endpoints, `AcpAgentModelClient` for
> `ProviderKind.Acp`.

For `ProviderKind.Acp` the sidecar acts as an [Agent Client Protocol](https://agentclientprotocol.com)
client: `AcpAgentHost` spawns the configured agent command (e.g.
`npx -y @agentclientprotocol/claude-agent-acp`, `gemini --acp`) and speaks newline-delimited
JSON-RPC over stdio via `AcpJsonRpcPeer` (`initialize` → `session/new` → `session/prompt`,
with `session/update` notifications mapped onto the existing SSE `AgentStreamEvent`s and
`session/cancel` wired to the cancel button). The agent's own transcript and tool loop stay
inside the agent process; SwebKit never sees its raw tool calls.

SwebKit's domain tools reach the agent through `SwebKitToolsMcpBridge`, a stateless
streamable-HTTP MCP endpoint (`ModelContextProtocol.AspNetCore`) whose URL is handed to the
agent in `session/new` → `mcpServers`. The `?tools=` query parameter carries the per-request
allowlist already resolved by `AgentToolCallOrchestrator`, so mode (`ask`/`ask_and_do`),
feature-area, and scope gates are preserved — an empty allowlist exposes zero tools.
`propose_*` mutations still flow through the existing pending-approvals pipeline.

`session/request_permission` is auto-approved by default (the first `allow*` option wins);
a per-profile `RequireToolApproval` toggle instead parks the request in `AcpPermissionStore`
(5-minute expiry) and surfaces it through `GET /api/agent/acp/permissions` +
`POST /api/agent/acp/permissions/{id}/respond`, rendered as `AcpPermissionCard` in the chat
surfaces. `fs/*` and `terminal/*` client capabilities are not advertised, so agent calls to
them receive JSON-RPC `-32601`. `session/clear` drops the ACP session alongside the local
conversation. The implementation lives under `src-sidecar/Services/Acp/`.

The per-turn system prompt (`AgentSystemPromptBuilder`) carries a bounded
`## Workspace map` section rendering the user-curated `AppConfig.Maps` — multiple
named maps, each rendered under a `### {name}` heading with nodes grouped by area
(AKS nodes show `ctx: {kubeconfigContext}` when pinned) plus relationships as
`from → to (label)`, capped (30 nodes/area, 40 edges, `+N more` overflow) so dense
maps can't eat the context window. This is
how every provider, ACP included, always sees the declared relationships; the
`investigate_workspace_issue` tool (workspace scope) then walks those edges live
rather than being the only way the map is discovered. When no declared node
matches the investigated resource, the tool inspects the hinted resource
directly — the map is enrichment, never a gate.

### Screen state (agent-workspace-awareness)

The "Current focus" prompt section tells the model *where* the user is; the
`get_screen_state` tool answers *what they see*. Feature pages register bounded,
whitelisted serializers through `web/src/lib/stores/screen-state.ts`
(`useScreenStateProvider`), which publishes debounced + heartbeated snapshots to
`POST /api/agent/screen-state`. A singleton `ScreenStateStore` keeps the latest
snapshot for 5 minutes; `GetScreenStateTool` returns it on demand — route,
feature area, capture timestamp/age, and the area-specific payload — so the
model reasons about data the UI already fetched instead of re-fetching it.
The tool is fence-exempt in `AgentToolCallOrchestrator` (like Observability):
it reads UI state, not area data, and reaches ACP agents through the normal
resolved-tools allowlist. Serializers never include auth headers, tokens,
connection strings, or full bodies — whitelisted fields and short previews only.

### Proactive investigation (agent-workspace-awareness)

`ProactiveInsightService` subscribes to `MonitoringAlertEvaluationService.AlertFired`.
When a rule fires with `AiInvestigationEnabled` (per-rule flag, default `true`),
a single-flight background investigation runs via `ProactiveInvestigationRunner`.
The service auto-matches the fired resource against every map
(`WorkspaceMapLookup.FindNode` — context-aware for AKS, so a `prod/api` node
pinned to `aks-dev` doesn't match an `aks-prod` alert); the matching map alone
scopes the runner's prompt, while an unmatched resource switches to map-less
self-discovery instructions instead of being skipped: a headless agentic loop
(`IAgentModelClient.ChatAsync`) with workspace-scope, ask-mode tools — so
`propose_*` mutations are structurally unreachable — capped at 5 tool rounds and
a 90-second wall-clock budget. The model is asked to end with a JSON object
(hypothesis/evidence/severity/next steps); on a null/failed run the service
falls back to the Module 4 single-shot `investigate_workspace_issue` + summarize
path. The report seeds a dedicated chat session, `InsightReady` flows over the
monitoring SSE stream, and `AppLayout`'s always-mounted subscription turns it
into an OS notification (plus in-app toast) — the only notification site, so it
fires while the app is minimized without ever double-toasting.

## Future Enhancements

- **Automated Remediation**: Self-healing capabilities (mutations stay behind
  the `propose_*` approval pipeline)
- **Performance Optimization**: Caching, streaming responses, load balancing
- **Insight feedback loop**: user feedback on proactive hypotheses improving
  future investigations
