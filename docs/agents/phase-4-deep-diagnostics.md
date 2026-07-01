# Phase 4: Deep Diagnostics and Agent Actions

## Status: Planned

---

## 🎯 Purpose

Two concrete capabilities the agent currently cannot do:

1. **Cross-service correlation** — "Why are requests for operation ID X failing?" The agent fans
   out to App Insights, pod logs, and Service Bus simultaneously and synthesises a root-cause
   narrative. Today a platform engineer does this manually across three separate tools.

2. **Safe agent actions** — "This pod has ImagePullBackOff. Propose the right pipeline to
   trigger." The agent suggests an action, the user confirms with one click, the agent executes.
   The confirmation gate is mandatory — the agent never acts without user approval.

Phase 3 adds proactive monitoring. Phase 4 adds deep tracing and controlled write operations.

---

## 📐 Design Decisions

### On KQL and Mistral

Mistral writes correct KQL for the standard App Insights tables (`requests`, `exceptions`,
`traces`, `dependencies`, `customEvents`, `availabilityResults`). The existing `QueryLogsTool`
already works for this. We keep it unchanged.

The real gap is schema discovery, not KQL generation:

- **Standard columns** (e.g. `operation_Id`, `innermostMessage`) — Mistral knows these from
  training data but occasionally gets the exact name wrong. Fixed by injecting the table schemas
  into the agent's system context (T1).

- **Custom dimensions** (e.g. `customDimensions["CorrelationId"]`) — completely app-specific.
  Mistral cannot know these. Fixed by `GetAppInsightsSchemaTool` (T2) which samples the actual
  values from the live workspace.

**Approach:** Trust Mistral to write KQL. Equip it with schema knowledge so it writes it
correctly. Add `CorrelateRequestFailureTool` (T3) not because Mistral can't write KQL, but
because fan-out across three services + result merging is orchestration logic that belongs in
code, not in a prompt.

---

## 🔑 What This Phase Delivers (6 Tasks)

| #  | Track | Task | Addresses |
|----|-------|------|-----------|
| T1 | Observability | Inject App Insights schema into agent context | Mistral knows standard column names |
| T2 | Observability | `GetAppInsightsSchemaTool` | Mistral discovers app-specific customDimensions |
| T3 | Observability | `CorrelateRequestFailureTool` | "find why X is failing" fan-out |
| T4 | Actions | Action proposal/confirmation system | Agent proposes, user confirms |
| T5 | Actions | `ListPipelinesTool` | Agent discovers available pipelines |
| T6 | Actions | `TriggerPipelineTool` + `TriggerPipelineConfirmedTool` | Agent triggers pipeline after confirmation |

> **Note for implementer:** The two tracks (T1-T3 and T4-T6) are independent and can be
> implemented in either order. Build and run `dotnet build` after every task.

---

## 📁 Key Files Reference

```
src/SwebKit.Agents/
  AgentContextBuilder.cs            ← T1: append App Insights schema summary
  Tools/
    QueryLogsTool.cs                ← read-only (no change — keep for free-form KQL)
    GetMetricsTool.cs               ← read-only (no change)
    GetAppInsightsSchemaTool.cs     ← T2: create
    CorrelateRequestFailureTool.cs  ← T3: create
    ListPipelinesTool.cs            ← T5: create
    TriggerPipelineTool.cs          ← T6: create
    TriggerPipelineConfirmedTool.cs ← T6: create
  IAgentChatService.cs              ← T4: add AgentProposedAction + ExecuteConfirmedActionAsync
  AgentChatService.cs               ← T4: implement proposal detection + ExecuteConfirmedActionAsync
  AgentToolRegistry.cs              ← T4: exclude _confirmed tools from Mistral

src/SwebKit.App/
  Components/Pages/
    AgentChatPanel.razor            ← T4: render confirm button
    AgentChatPanel.razor.css        ← T4: style confirm button
  MauiProgram.cs                    ← T2+T3+T5+T6: register new tools

src/SwebKit.Core/
  Abstractions/
    IObservabilityProvider.cs       ← read-only (RunQueryAsync is sufficient)
    IObservabilityProviderFactory.cs ← read-only
    IDevOpsClient.cs                ← read-only (GetPipelinesAsync, TriggerPipelineRunAsync)
    IDevOpsClientFactory.cs         ← read-only
```

**Project reference note:** `SwebKit.Agents` references `SwebKit.Core`, which contains both
`IObservabilityProviderFactory` and `IDevOpsClientFactory`. No new `.csproj` references needed.

---

## Track A — Deep Observability (T1–T3)

---

## T1 — Inject App Insights Schema into Agent Context

### What to change

Modify `src/SwebKit.Agents/AgentContextBuilder.cs` so that when observability is configured,
the system prompt includes a compact schema cheat sheet. This ensures Mistral always uses the
correct column names (e.g. `operation_Id` not `operationId`, `innermostMessage` not `message`).

### How

**Read the file first** to understand the existing `BuildContext()` method and how other
config sections are appended.

At the point where `ObservabilityConfig` is included in the context string, also append:

```csharp
if (config.ObservabilityConfig?.SelectedResourceId is not null)
{
    context.AppendLine("App Insights tables and key columns:");
    context.AppendLine("  requests: timestamp, name, resultCode, success, duration, operation_Id, cloud_RoleName, url");
    context.AppendLine("  exceptions: timestamp, type, innermostMessage, problemId, operation_Id, cloud_RoleName");
    context.AppendLine("  traces: timestamp, message, severityLevel, operation_Id, cloud_RoleName");
    context.AppendLine("  dependencies: timestamp, name, target, type, resultCode, success, duration, operation_Id");
    context.AppendLine("  customEvents: timestamp, name, customDimensions, operation_Id");
    context.AppendLine("  Note: use tostring(customDimensions[\"Key\"]) to filter custom properties.");
    context.AppendLine("  Note: operation_Id has an underscore — not operationId.");
}
```

This is a pure string append — no new dependencies, no interface changes.

### Acceptance

System prompt for a session with observability configured contains "operation_Id has an
underscore". No build errors.

---

## T2 — `GetAppInsightsSchemaTool`

### What it does

Returns the names of tables that are actually populated in the user's App Insights workspace
(some workspaces have no `availabilityResults`, no `customEvents`, etc.) and a sample of
`customDimensions` keys seen in `requests` and `exceptions` over the last 24 hours.

Mistral calls this once when it needs to know how to filter by a correlation ID or custom
property. The result tells it exactly which key names to use.

### File to create

`src/SwebKit.Agents/Tools/GetAppInsightsSchemaTool.cs`

### Constructor injection

```csharp
public GetAppInsightsSchemaTool(
    IObservabilityProviderFactory providerFactory,
    AppStateService appState)
```

Same pattern as `QueryLogsTool` — read that file for the exact injection pattern.

### Parameters schema

```json
{
  "type": "object",
  "properties": {
    "include_custom_dimensions": {
      "type": "boolean",
      "description": "Whether to sample customDimensions keys (default: true). Set false for faster response."
    }
  }
}
```

### Implementation

**Step 1:** Get the provider (same check as `QueryLogsTool` — return error JSON if not configured).

**Step 2:** Run two KQL queries in parallel using `Task.WhenAll`:

```kql
-- Query 1: which tables have data in the last 24h
union requests, exceptions, traces, dependencies, customEvents, availabilityResults, customMetrics
| where timestamp > ago(24h)
| summarize Count=count() by Type=itemType
| order by Count desc
```

```kql
-- Query 2: sample customDimensions keys from requests + exceptions (last 24h)
union
  (requests | where timestamp > ago(24h) | project customDimensions | take 200),
  (exceptions | where timestamp > ago(24h) | project customDimensions | take 200)
| mv-expand bag_keys(customDimensions)
| summarize Count=count() by Key=tostring(bag_keys_1)
| order by Count desc
| take 30
```

**Step 3:** Parse query results and build the return JSON.

For Query 1: collect the `Type` column values where `Count > 0`.
For Query 2: collect the `Key` column values.

If Query 2 fails (some workspaces restrict `bag_keys`), return `"custom_dimension_keys": null`
without failing the whole tool.

### Return value

```json
{
  "populated_tables": ["requests", "exceptions", "traces", "dependencies"],
  "custom_dimension_keys": ["CorrelationId", "TenantId", "UserId", "Environment"],
  "note": "Use tostring(customDimensions[\"Key\"]) == \"value\" to filter by these keys"
}
```

### Tool name

`"get_app_insights_schema"`

### Tool description (for Mistral)

```
"Returns the tables that contain data in the configured App Insights workspace and the
custom dimension keys available. Call this before writing KQL queries that filter on
custom properties, to discover the exact key names used by this application."
```

### Registration

```csharp
services.AddSingleton<IAgentTool, GetAppInsightsSchemaTool>();
```

---

## T3 — `CorrelateRequestFailureTool`

### What it does

This is the "dream tool". Given an operation ID or correlation ID, fans out to:
- App Insights (requests + exceptions for the ID) — via `RunQueryAsync`
- Pod logs (grep for the ID string in specified pod) — via existing Kubernetes log client
- Service Bus dead-letter queue (check for messages with matching CorrelationId) — optional

Returns a unified investigation report. Mistral synthesises this into a root-cause narrative.

**Why not just have Mistral call each tool separately?**
Because multi-turn fan-out wastes tokens, loses the parallel execution advantage, and makes
the conversation awkward. One tool call = one complete picture.

### File to create

`src/SwebKit.Agents/Tools/CorrelateRequestFailureTool.cs`

### Constructor injection

**Before writing this file, read:**
- `src/SwebKit.Agents/Tools/GetPodLogsTool.cs` — find the exact Kubernetes client type and how it is injected
- The Service Bus tool in `src/SwebKit.Agents/Tools/` (look for a tool that reads queue messages) — find the SB client type

Then inject those types alongside `IObservabilityProviderFactory` and `AppStateService`.

### Parameters schema

```json
{
  "type": "object",
  "properties": {
    "operation_id": {
      "type": "string",
      "description": "Azure Monitor operation_Id or correlation ID to trace across services"
    },
    "pod_name": {
      "type": "string",
      "description": "Pod name to grep logs in (optional — include when the failing service is known)"
    },
    "namespace": {
      "type": "string",
      "description": "Kubernetes namespace of the pod (required when pod_name is provided)"
    },
    "queue_name": {
      "type": "string",
      "description": "Service Bus queue to check for dead-lettered messages (optional)"
    },
    "time_range_hours": {
      "type": "integer",
      "description": "How many hours back to search (default: 2, max: 24)"
    }
  },
  "required": ["operation_id"]
}
```

### Implementation pattern

1. Sanitize `operation_id`: reject if it contains `'`, `"`, `\`, or is longer than 200 chars.
   Return `{"error": "Invalid operation_id"}` if validation fails.

2. Build a list of `Task` objects for each source that is available:
   - **Always:** Two App Insights KQL queries in parallel (requests + exceptions by operation_Id)
   - **If `pod_name` provided:** fetch pod logs, filter lines containing the operation_id string
   - **If `queue_name` provided and SB is configured:** check dead-letter sub-queue

3. `await Task.WhenAll(allTasks)` — all sources run in parallel.

4. Wrap each individual Task in its own try/catch. A failure in one source never blocks others.
   Failed sources return `{"error": "reason"}` in their slot.

### KQL queries for App Insights (substitute sanitized `{opId}` value)

```kql
-- requests
requests
| where operation_Id == "{opId}"
| project timestamp, name, resultCode, success, duration, cloud_RoleName, url
| order by timestamp asc
| take 30
```

```kql
-- exceptions
exceptions
| where operation_Id == "{opId}"
    or tostring(customDimensions["CorrelationId"]) == "{opId}"
    or tostring(customDimensions["correlationId"]) == "{opId}"
| project timestamp, type, innermostMessage, cloud_RoleName
| order by timestamp asc
| take 20
```

### Pod log grep

Fetch the last 200 log lines from the pod (same call as `GetPodLogsTool`). Then filter:
```csharp
var matchingLines = allLines
    .Where(l => l.Contains(operationId, StringComparison.OrdinalIgnoreCase))
    .ToList();
```

Return matching lines only. If there are none, return `"matching_lines": []`.

### Return value

```json
{
  "operation_id": "abc123",
  "time_range_hours": 2,
  "app_insights": {
    "requests": [
      { "timestamp": "...", "name": "POST /api/orders", "resultCode": "500",
        "success": false, "duration_ms": 1230, "role": "api-service" }
    ],
    "exceptions": [
      { "timestamp": "...", "type": "System.NullReferenceException",
        "message": "Object reference not set", "role": "api-service" }
    ]
  },
  "pod_logs": {
    "pod": "api-deployment-7d9f-xk2p",
    "namespace": "default",
    "matching_lines": [
      "2026-07-01T12:00:01Z ERROR CorrelationId=abc123 NullReferenceException in OrderService"
    ]
  },
  "service_bus": {
    "queue": "orders",
    "matching_dead_letters": []
  }
}
```

Omit `pod_logs` key entirely if no `pod_name` was provided.
Omit `service_bus` key entirely if no `queue_name` was provided.

### Tool name

`"correlate_request_failure"`

### Registration

```csharp
services.AddSingleton<IAgentTool, GetAppInsightsSchemaTool>();
services.AddSingleton<IAgentTool, CorrelateRequestFailureTool>();
```

---

## Track B — Agent Actions (T4–T6)

### The confirmation model

The agent must NEVER execute a write action without user confirmation. The flow is:

```
User: "Pod nginx has ImagePullBackOff. Fix it."

Agent → get_pod_events → ImagePullBackOff confirmed
Agent → list_pipelines  → finds "Build API" (ID 42)
Agent → trigger_pipeline(project="MyApp", pipeline_id=42, branch="main")
       → returns a PROPOSAL (does NOT execute the pipeline)

Reply text:   "I found ImagePullBackOff. I can trigger 'Build API' on main to rebuild the image."
Reply action: { label: "Trigger 'Build API' on main", ... }

UI: Renders "▶ Trigger 'Build API' on main" button

User: clicks button

UI → ExecuteConfirmedActionAsync("trigger_pipeline_confirmed", "{project, pipelineId, branch}")
Agent → actually triggers the pipeline
Reply text: "Pipeline run #1234 started."
```

---

## T4 — Action Proposal / Confirmation System

### What to change — four files

---

### 4a — `src/SwebKit.Agents/IAgentChatService.cs`

**Read the existing file completely before editing.**

Add `AgentProposedAction` in the same namespace:

```csharp
/// <summary>
/// An action the agent is proposing that requires user confirmation before execution.
/// </summary>
public sealed class AgentProposedAction
{
    /// <summary>Stable ID generated at proposal time. Use Guid.NewGuid().ToString("N").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable button label, e.g. "Trigger 'Build API' on main".</summary>
    public required string Label { get; init; }

    /// <summary>Tool name to call upon confirmation, e.g. "trigger_pipeline_confirmed".</summary>
    public required string ToolName { get; init; }

    /// <summary>Pre-built arguments as a raw JSON string.</summary>
    public required string ArgumentsJson { get; init; }
}
```

Add `ProposedAction` to the existing `AgentChatReply` (look for its definition and add one property):

```csharp
public AgentProposedAction? ProposedAction { get; init; }
```

Add to the `IAgentChatService` interface:

```csharp
/// <summary>
/// Executes a previously proposed action after user confirmation.
/// toolName and argumentsJson come directly from AgentProposedAction.
/// </summary>
Task<AgentChatReply> ExecuteConfirmedActionAsync(
    string toolName,
    string argumentsJson,
    CancellationToken ct = default);
```

---

### 4b — `src/SwebKit.Agents/AgentChatService.cs`

**Read the existing file completely before editing.** Understand how `SendAsync` builds the
reply and how tool results are returned to Mistral.

Add a private field:

```csharp
private AgentProposedAction? _pendingProposal;
```

Inside the tool-call result processing (where each tool result string is obtained), add a
proposal check BEFORE adding the result to Mistral's context:

```csharp
if (TryParseProposal(result, out var proposal))
{
    _pendingProposal = proposal;
    result = "I've prepared an action for your confirmation.";
}
```

At the start of each `SendAsync` call, reset: `_pendingProposal = null;`

When building the final `AgentChatReply`, include:

```csharp
ProposedAction = _pendingProposal
```

Add the helper method:

```csharp
private static bool TryParseProposal(string json, [NotNullWhen(true)] out AgentProposedAction? proposal)
{
    proposal = null;
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("__proposal", out var flag) || !flag.GetBoolean())
            return false;
        proposal = new AgentProposedAction
        {
            Id            = root.GetProperty("id").GetString()!,
            Label         = root.GetProperty("label").GetString()!,
            ToolName      = root.GetProperty("tool_name").GetString()!,
            ArgumentsJson = root.GetProperty("arguments_json").GetString()!
        };
        return true;
    }
    catch { return false; }
}
```

Implement `ExecuteConfirmedActionAsync`:

```csharp
public async Task<AgentChatReply> ExecuteConfirmedActionAsync(
    string toolName,
    string argumentsJson,
    CancellationToken ct = default)
{
    var sw = Stopwatch.StartNew();
    using var doc = JsonDocument.Parse(argumentsJson);
    var result = await _registry.ExecuteAsync(toolName, doc.RootElement, ct);
    sw.Stop();

    // Preserve context — look at how SendAsync adds messages and match that pattern
    _session.Add(/* user: "[User confirmed action: {toolName}]" */);
    _session.Add(/* assistant: result */);

    return new AgentChatReply { Text = result, ToolsUsed = [toolName], Elapsed = sw.Elapsed };
}
```

> Adapt the `_session.Add(...)` calls to match the exact `ConversationMessage` or record type
> used in the existing `SendAsync` — read that code first.

---

### 4c — `src/SwebKit.Agents/AgentToolRegistry.cs`

**Read the existing file.** In `GetDefinitions()`, filter out tools whose name ends with
`"_confirmed"` so Mistral never sees them:

```csharp
.Where(t => !t.Name.EndsWith("_confirmed", StringComparison.Ordinal))
```

---

### 4d — `src/SwebKit.App/Components/Pages/AgentChatPanel.razor`

**Read the existing file completely before editing.**

Update the `ChatMessage` record — add one field:

```csharp
private sealed record ChatMessage(
    bool IsUser,
    string Content,
    IReadOnlyList<string> ToolsUsed,
    TimeSpan Elapsed,
    AgentProposedAction? ProposedAction = null);
```

When appending the assistant reply to `_messages`, pass `reply.ProposedAction`:

```csharp
_messages.Add(new ChatMessage(false, reply.Text, reply.ToolsUsed, reply.Elapsed, reply.ProposedAction));
```

In the message rendering loop, after the assistant bubble content, add:

```razor
@if (msg.ProposedAction is not null && !_executedActions.Contains(msg.ProposedAction.Id))
{
    <div class="agent-action-proposal">
        <FluentButton Appearance="Appearance.Accent"
                      Disabled="@_isLoading"
                      OnClick="@(() => ExecuteProposedAction(msg.ProposedAction))">
            ▶ @msg.ProposedAction.Label
        </FluentButton>
    </div>
}
else if (msg.ProposedAction is not null)
{
    <div class="agent-action-executed">Action executed.</div>
}
```

Add to `@code`:

```csharp
private readonly HashSet<string> _executedActions = [];

private async Task ExecuteProposedAction(AgentProposedAction action)
{
    _executedActions.Add(action.Id);
    _isLoading = true;
    StateHasChanged();

    var reply = await Agent.ExecuteConfirmedActionAsync(action.ToolName, action.ArgumentsJson);
    _messages.Add(new ChatMessage(false, reply.Text, reply.ToolsUsed, reply.Elapsed));

    _isLoading = false;
    await ScrollToBottomAsync();
    StateHasChanged();
}
```

> Check if `_isLoading` and `ScrollToBottomAsync` already exist. If `_isLoading` is named
> differently, match the existing name. Do not add a duplicate field.

Add to `AgentChatPanel.razor.css`:

```css
.agent-action-proposal { margin-top: 8px; }

.agent-action-executed {
    margin-top: 6px;
    font-size: 0.78em;
    color: var(--neutral-foreground-hint);
    font-style: italic;
}
```

---

## T5 — `ListPipelinesTool`

### What it does

Lists available Azure DevOps pipelines so the agent can identify which one to propose
triggering for a given workload.

### File to create

`src/SwebKit.Agents/Tools/ListPipelinesTool.cs`

### Constructor injection

```csharp
public ListPipelinesTool(IDevOpsClientFactory devOpsClientFactory, AppStateService appState)
```

**Before implementing, read `src/SwebKit.App/MauiProgram.cs`** to understand how `DevOpsConfig`
is obtained and how the factory is registered — replicate the same access pattern for config.

### Parameters schema

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Azure DevOps project name. Uses the configured default project if omitted."
    },
    "filter": {
      "type": "string",
      "description": "Optional case-insensitive substring to filter pipeline names."
    }
  }
}
```

### Implementation

```csharp
var devOpsConfig = _appState.Config.DevOpsConfig;
if (devOpsConfig == null || string.IsNullOrWhiteSpace(devOpsConfig.Organization))
    return JsonSerializer.Serialize(new { error = "Azure DevOps not configured." });

var client = _devOpsClientFactory.Create(devOpsConfig);
var projectName = /* use "project" argument if provided, else devOpsConfig.DefaultProject
                    or similar — read DevOpsConfig model to find the right property */;
var pipelines = await client.GetPipelinesAsync(projectName, ct);

if (filter is not null)
    pipelines = pipelines
        .Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();

return JsonSerializer.Serialize(new {
    project  = projectName,
    pipelines = pipelines.Select(p => new { id = p.Id, name = p.Name, folder = p.Folder })
});
```

### Return value

```json
{
  "project": "MyApp",
  "pipelines": [
    { "id": 42, "name": "Build API", "folder": "\\services" },
    { "id": 43, "name": "Build Frontend", "folder": "\\ui" }
  ]
}
```

### Tool name

`"list_pipelines"`

---

## T6 — `TriggerPipelineTool` and `TriggerPipelineConfirmedTool`

### Design

Two tools work as a pair:
- **`TriggerPipelineTool`** — called by Mistral, returns a `__proposal` JSON (never executes)
- **`TriggerPipelineConfirmedTool`** — called only via `ExecuteConfirmedActionAsync`, actually
  triggers the pipeline. Hidden from Mistral via the `_confirmed` filter in `AgentToolRegistry`.

---

### `TriggerPipelineTool`

**File:** `src/SwebKit.Agents/Tools/TriggerPipelineTool.cs`

**Constructor:** `TriggerPipelineTool(AppStateService appState)` — validates config only, no
  client injection needed here.

**Parameters schema:**

```json
{
  "type": "object",
  "properties": {
    "project":       { "type": "string",  "description": "Azure DevOps project name" },
    "pipeline_id":   { "type": "integer", "description": "Pipeline ID (from list_pipelines)" },
    "pipeline_name": { "type": "string",  "description": "Pipeline display name (for button label)" },
    "branch":        { "type": "string",  "description": "Branch to build (default: main)" }
  },
  "required": ["project", "pipeline_id"]
}
```

**ExecuteAsync — returns a proposal, does NOT call the DevOps API:**

```csharp
var devOpsConfig = _appState.Config.DevOpsConfig;
if (devOpsConfig == null)
    return JsonSerializer.Serialize(new { error = "Azure DevOps not configured." });

var project      = arguments.GetProperty("project").GetString()!;
var pipelineId   = arguments.GetProperty("pipeline_id").GetInt32();
var pipelineName = arguments.TryGetProperty("pipeline_name", out var pn)
                      ? pn.GetString() ?? $"Pipeline {pipelineId}"
                      : $"Pipeline {pipelineId}";
var branch       = arguments.TryGetProperty("branch", out var br)
                      ? br.GetString() ?? "main"
                      : "main";

var confirmedArgs = JsonSerializer.Serialize(new { project, pipeline_id = pipelineId, branch });

return JsonSerializer.Serialize(new
{
    __proposal    = true,
    id            = Guid.NewGuid().ToString("N"),
    label         = $"Trigger '{pipelineName}' on {branch}",
    tool_name     = "trigger_pipeline_confirmed",
    arguments_json = confirmedArgs
});
```

**Tool name:** `"trigger_pipeline"`

---

### `TriggerPipelineConfirmedTool`

**File:** `src/SwebKit.Agents/Tools/TriggerPipelineConfirmedTool.cs`

**Constructor:** `TriggerPipelineConfirmedTool(IDevOpsClientFactory devOpsClientFactory, AppStateService appState)`

**Parameters schema:** Same as `TriggerPipelineTool` (project, pipeline_id, branch).

**Tool name:** `"trigger_pipeline_confirmed"` — this name ends with `_confirmed` so it is
automatically excluded from the tool definitions sent to Mistral (see T4c).

**ExecuteAsync — actually triggers the pipeline:**

```csharp
var devOpsConfig = _appState.Config.DevOpsConfig;
if (devOpsConfig == null)
    return JsonSerializer.Serialize(new { error = "Azure DevOps not configured." });

var client     = _devOpsClientFactory.Create(devOpsConfig);
var project    = arguments.GetProperty("project").GetString()!;
var pipelineId = arguments.GetProperty("pipeline_id").GetInt32();
var branch     = arguments.TryGetProperty("branch", out var br) ? br.GetString() ?? "main" : "main";

var run = await client.TriggerPipelineRunAsync(project, pipelineId, branch, null, ct);

return JsonSerializer.Serialize(new
{
    status   = "triggered",
    run_id   = run.Id,
    run_name = run.Name,
    state    = run.State,
    web_url  = run.WebUrl
});
```

### Registration (add all new tools in `MauiProgram.cs`)

```csharp
services.AddSingleton<IAgentTool, GetAppInsightsSchemaTool>();
services.AddSingleton<IAgentTool, CorrelateRequestFailureTool>();
services.AddSingleton<IAgentTool, ListPipelinesTool>();
services.AddSingleton<IAgentTool, TriggerPipelineTool>();
services.AddSingleton<IAgentTool, TriggerPipelineConfirmedTool>();
```

---

## ✅ Acceptance Checklist

**Track A — Observability**
- [ ] T1: System prompt for a session with observability configured contains the column name hints
- [ ] T2: `GetAppInsightsSchemaTool` returns populated tables and custom dimension keys
- [ ] T2: Returns gracefully when observability is not configured (error JSON, no throw)
- [ ] T3: `CorrelateRequestFailureTool` runs App Insights queries and returns merged results
- [ ] T3: Pod log section is omitted from response when no `pod_name` is provided
- [ ] T3: Service Bus section is omitted when no `queue_name` is provided
- [ ] T3: Rejects `operation_id` values containing `'`, `"`, `\`, or longer than 200 chars
- [ ] T3: A failure in one source (e.g. pod not found) does not fail the whole tool

**Track B — Actions**
- [ ] T4: `AgentProposedAction` type added, `ProposedAction` property on `AgentChatReply`
- [ ] T4: `ExecuteConfirmedActionAsync` implemented in `AgentChatService`
- [ ] T4: `_confirmed` tools excluded from `GetDefinitions()` in `AgentToolRegistry`
- [ ] T4: "▶ Confirm & Run" button renders below the message when ProposedAction is set
- [ ] T4: After click, button disappears and "Action executed." appears — cannot double-click
- [ ] T5: `ListPipelinesTool` returns pipelines for the configured DevOps project
- [ ] T6: `TriggerPipelineTool` returns `__proposal` JSON, never calls the DevOps API
- [ ] T6: `TriggerPipelineConfirmedTool` triggers the pipeline and returns run ID + web URL
- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` passes with zero failures

---

## ❌ Deliberately Out of Scope

| Feature | Reason |
|---------|--------|
| Workload→pipeline auto-mapping | Requires new configuration schema |
| Approve/reject pipeline stages in agent | Already in the DevOps page UI |
| Rollback actions | Too risky without dedicated approval workflow |
| Agent-triggered scaling | AKS write permissions + very high risk |
| Multiple simultaneous proposed actions | Complex multi-button UX |

---

## 🔗 Related Documents

- [Phase 3: Automation](phase-3-automation.md)
- [Phase 2: Intelligence (Planned)](phase-2-intelligence.md)
- [Architecture](../architecture/architecture.md)

---

*Document created: 2026-07-01*
*Last updated: 2026-07-01*
