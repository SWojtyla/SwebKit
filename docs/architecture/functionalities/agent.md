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

## Future Enhancements

- **Context Awareness**: Deep integration with SwebKit state for richer context
- **Advanced Tooling**: Multi-step investigations and correlation
- **Proactive Monitoring**: Agent-initiated health checks and alerts
- **Automated Remediation**: Self-healing capabilities
- **Performance Optimization**: Caching, streaming responses, load balancing