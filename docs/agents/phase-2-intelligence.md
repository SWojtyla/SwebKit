# Phase 2: Intelligence

## Status: Implemented

---

## 🎯 Purpose

Make the agent **context-aware and investigation-capable**: it should know what the user is currently looking at, reflect active alerts in its reasoning, bundle multi-step diagnostics into single tool calls, and let users jump directly from a chat response into the Incident Timeline.

Phase 1 proved the agent works. Phase 2 makes it genuinely useful on a daily basis.

---

## 🔑 What This Phase Delivers (5 Tasks)

| # | Task | Value |
|---|------|-------|
| T1 | Context awareness — inject selection + alerts into the system prompt | Agent answers feel relevant to what is on screen |
| T2 | Composite tool: `InvestigatePodIssueTool` | One message instead of 3-4 separate queries |
| T3 | Composite tool: `AnalyzeQueueHealthTool` | One message for queue dead-letter analysis |
| T4 | Markdown rendering in the chat panel | Responses render tables, bold, code blocks properly |
| T5 | "Investigate in Timeline" button in chat panel | Direct hand-off to the Incident Timeline from agent context |

> **Note for implementer:** Every file path, type name, and interface below is exact. Do not rename or reorganize unless the existing code has already changed. Build after each task and fix any errors before moving to the next.

---

## 📁 Key Files Reference

```
src/SwebKit.Agents/
  AgentContextBuilder.cs          ← T1: modify
  IAgentContextBuilder.cs         ← T1: no change to interface signature
  AgentChatService.cs             ← no change needed
  Tools/
    InvestigatePodIssueTool.cs    ← T2: create
    AnalyzeQueueHealthTool.cs     ← T3: create
    (existing tools stay as-is)

src/SwebKit.App/
  Components/Pages/
    AgentChatPanel.razor          ← T4 + T5: modify
  MauiProgram.cs                  ← T2 + T3: register new tools

src/SwebKit.Core/
  Abstractions/
    ISelectionContext.cs          ← read-only (interface for T1)
    IAlertMonitorService.cs       ← read-only (interface for T1)
  Models/
    IncidentTimelineModels.cs     ← read-only (IncidentInvestigationSeed, etc.)

src/SwebKit.App/Services/
  IncidentInvestigationLauncher.cs  ← read-only (launcher for T5)
  SelectionContext.cs               ← read-only (implementation)
```

---

## T1 — Enhanced Context Awareness

### What to do

Modify `AgentContextBuilder` so that `BuildContext()` also appends:
- The currently selected resource (from `ISelectionContext`)
- The last 3 fired alerts (from `IAlertMonitorService`)

### Existing interfaces (do not change)

**`ISelectionContext`** (`SwebKit.Core.Abstractions`):
```csharp
public interface ISelectionContext
{
    void SetSelection(string area, object? selected);
    T? GetSelection<T>(string area) where T : class;
    event Action? SelectionChanged;
}
```
Selection areas used by the app: `"aks"`, `"servicebus"`, `"redis"`, `"storage"`, `"observability"`.

**`IAlertMonitorService`** (`SwebKit.Core.Abstractions`):
```csharp
public interface IAlertMonitorService : IAsyncDisposable
{
    bool IsMonitoring { get; }
    IReadOnlyList<AlertFiredEvent> RecentAlerts { get; }
    // ...
}
```

**`AlertFiredEvent`** (`SwebKit.Core.Models`):
```csharp
public sealed record AlertFiredEvent(
    string RuleId,
    string RuleName,
    AlertRuleSource Source,
    AlertSeverity Severity,
    string Message,
    string Detail,
    DateTimeOffset FiredAt,
    string ProfileName);
```

### Current `AgentContextBuilder` constructor

```csharp
// Current: no parameters
public sealed class AgentContextBuilder : IAgentContextBuilder
{
    public string BuildContext(AppStateService appState) { ... }
}
```

### How to change it

1. Add constructor injection for `ISelectionContext` and `IAlertMonitorService`.
2. Both interfaces are in `SwebKit.Core.Abstractions` which `SwebKit.Agents` already references via `SwebKit.Core`.
3. Keep `BuildContext(AppStateService appState)` — do **not** change the interface `IAgentContextBuilder`.
4. At the end of `BuildContext`, append selection and alerts as additional context lines.

**New constructor signature:**
```csharp
public AgentContextBuilder(ISelectionContext selection, IAlertMonitorService alertMonitor)
```

**Context lines to append inside `BuildContext`:**

For selection — iterate the known areas and append any non-null selection:
```
Selected: aks=<value>, observability=<value>
```
Use `.ToString()` on the selected object. Fall back to the type name if `.ToString()` returns the full type name.

For alerts — take at most the 3 most recent from `alertMonitor.RecentAlerts`:
```
Recent alerts (last 3):
- [Critical] Pod OOM Kill on nginx-abc at 2026-07-01 14:05 UTC
- [Warning] Queue depth > 500 on orders-queue at 2026-07-01 13:55 UTC
```
If `RecentAlerts` is empty, append nothing — do not write "No recent alerts".

### Where DI registration lives

`src/SwebKit.App/MauiProgram.cs` — `AgentContextBuilder` is already registered there. Since `ISelectionContext` and `IAlertMonitorService` are already registered as singletons in the app, DI will inject them automatically. **No change needed in `MauiProgram.cs` for T1.**

### Acceptance criteria

- `AgentContextBuilder` constructor takes `ISelectionContext` and `IAlertMonitorService`.
- `BuildContext()` appends a "Selected:" line when at least one selection is set.
- `BuildContext()` appends alert lines when `RecentAlerts.Count > 0` (capped at 3).
- `dotnet build` passes.

---

## T2 — Composite Tool: `InvestigatePodIssueTool`

### What it does

Runs `GetPodStatusTool`, `GetPodLogsTool`, and `GetPodEventsTool` in parallel for a given pod, then returns a single merged JSON object. This replaces 3-4 round-trips with one tool call.

### File to create

`src/SwebKit.Agents/Tools/InvestigatePodIssueTool.cs`

### Parameters schema

```json
{
  "type": "object",
  "properties": {
    "namespace": { "type": "string", "description": "Kubernetes namespace" },
    "pod_name":  { "type": "string", "description": "Exact pod name" }
  },
  "required": ["namespace", "pod_name"]
}
```

### Implementation pattern

**Read `GetPodStatusTool.cs`, `GetPodLogsTool.cs`, and `GetPodEventsTool.cs` before writing this.** You need to know exactly what Kubernetes client they inject (likely `IAksClientFactory` or `IAksClient`).

- Implement `IAgentTool` (from `SwebKit.Agents.Tools`)
- Name: `"investigate_pod_issue"`
- Inject the same Kubernetes client dependency that the existing pod tools use
- In `ExecuteAsync`:
  1. Read `namespace` and `pod_name` from `arguments` (`arguments.GetProperty("namespace").GetString()`, etc.)
  2. Call the status, logs, and events fetches in parallel with `Task.WhenAll`
  3. Return a JSON string:
     ```json
     {
       "pod": "...",
       "namespace": "...",
       "status": { ... },
       "recent_logs": [ "line1", "line2", "..." ],
       "events": [ ... ]
     }
     ```
  4. Limit logs to 50 lines. If any sub-call throws, capture the exception message and put `{ "error": "message" }` in that field — do not let exceptions propagate.

**Do not call `AgentToolRegistry` or instantiate the other tool classes.** Inject the underlying Kubernetes client directly and reuse the fetch logic.

### Registration

In `src/SwebKit.App/MauiProgram.cs`, add alongside existing tool registrations:
```csharp
services.AddSingleton<IAgentTool, InvestigatePodIssueTool>();
```

### Acceptance criteria

- Tool is named `"investigate_pod_issue"`.
- Returns merged JSON with `status`, `recent_logs`, and `events` fields.
- If any sub-call fails, that field contains `{ "error": "..." }` rather than throwing.
- `dotnet build` passes.

---

## T3 — Composite Tool: `AnalyzeQueueHealthTool`

### What it does

Calls stats and dead-letter message fetch for a given queue in parallel, returns a merged result with a plain-English `health_summary` field.

### File to create

`src/SwebKit.Agents/Tools/AnalyzeQueueHealthTool.cs`

### Parameters schema

```json
{
  "type": "object",
  "properties": {
    "queue_name":       { "type": "string", "description": "Service Bus queue name" },
    "namespace_alias":  { "type": "string", "description": "Namespace alias configured in SwebKit (optional)" }
  },
  "required": ["queue_name"]
}
```

### Implementation pattern

**Read `GetQueueStatsTool.cs` and `GetQueueMessagesTool.cs` before writing this.** You need to know what Service Bus client they inject.

- Name: `"analyze_queue_health"`
- Inject the same Service Bus client dependency that `GetQueueStatsTool` uses
- In `ExecuteAsync`:
  1. Run stats and dead-letter fetches in parallel with `Task.WhenAll`
  2. Return:
     ```json
     {
       "queue": "...",
       "stats": { ... },
       "dead_letter_sample": [ ... ],
       "health_summary": "Healthy"
     }
     ```
  3. Derive `health_summary`:
     - `"Critical"` if dead-letter count > 0 or active message count > 1000
     - `"Warning"` if active message count > 100
     - `"Healthy"` otherwise
  4. If any sub-call throws, put `{ "error": "message" }` in that field.

### Registration

```csharp
services.AddSingleton<IAgentTool, AnalyzeQueueHealthTool>();
```

### Acceptance criteria

- Tool is named `"analyze_queue_health"`.
- Returns merged JSON with `stats`, `dead_letter_sample`, and `health_summary`.
- `health_summary` is exactly one of `"Healthy"`, `"Warning"`, or `"Critical"`.
- `dotnet build` passes.

---

## T4 — Markdown Rendering in AgentChatPanel

### What to change

**File:** `src/SwebKit.App/Components/Pages/AgentChatPanel.razor`

### Current rendering (look for this line)

```razor
<pre class="agent-bubble__text">@msg.Content</pre>
```

### What to do

Replace the `<pre>` tag with a Markdown renderer.

**Option A — Check Fluent UI first:** Check `Microsoft.FluentUI.AspNetCore.Components` for a markdown/richtext component. If one exists, use it.

**Option B — Use Markdig (default if no Fluent UI component):**

1. Add to `src/SwebKit.App/SwebKit.App.csproj`:
   ```xml
   <PackageReference Include="Markdig" Version="0.38.0" />
   ```

2. Add a helper in the `@code` block:
   ```csharp
   private static MarkupString RenderMarkdown(string text)
   {
       var html = Markdig.Markdown.ToHtml(text ?? string.Empty);
       return new MarkupString(html);
   }
   ```

3. Replace the `<pre>` tag with:
   ```razor
   <div class="agent-bubble__text agent-bubble__markdown">
       @RenderMarkdown(msg.Content)
   </div>
   ```

4. In `AgentChatPanel.razor.css`, add prose styles:
   ```css
   .agent-bubble__markdown table { border-collapse: collapse; width: 100%; font-size: 0.82em; }
   .agent-bubble__markdown th,
   .agent-bubble__markdown td   { border: 1px solid var(--neutral-stroke-rest); padding: 4px 8px; }
   .agent-bubble__markdown code { background: var(--neutral-fill-secondary-rest); padding: 1px 4px; border-radius: 3px; font-size: 0.85em; }
   .agent-bubble__markdown pre  { background: var(--neutral-fill-secondary-rest); padding: 8px; border-radius: 4px; overflow-x: auto; }
   .agent-bubble__markdown p    { margin: 0 0 6px; }
   .agent-bubble__markdown ul,
   .agent-bubble__markdown ol   { margin: 0 0 6px; padding-left: 18px; }
   ```

> **Security note:** `Markdig.ToHtml()` uses a safe pipeline by default (HTML in source is escaped). Do not change the pipeline settings.

### Acceptance criteria

- Assistant messages render markdown: bold, tables, code blocks, bullet lists.
- User messages still render as plain text (no change to the user bubble).
- `dotnet build` passes.

---

## T5 — "Investigate in Timeline" Button

### What it does

Adds an icon button to the agent panel header. When clicked, it launches the Incident Timeline page with a pre-seeded investigation scope derived from the current AKS selection.

### What to change

**File:** `src/SwebKit.App/Components/Pages/AgentChatPanel.razor`

### Services to inject (add to top of file)

```razor
@inject IncidentInvestigationLauncher InvestigationLauncher
@inject ISelectionContext SelectionCtx
```

`IncidentInvestigationLauncher` is in `SwebKit.App.Services`.
`ISelectionContext` is in `SwebKit.Core.Abstractions`.

Both are already registered as singletons in `MauiProgram.cs`.

### Relevant types (from `SwebKit.Core.Models`)

```csharp
// IncidentInvestigationSeed
public sealed record IncidentInvestigationSeed
{
    public required IncidentInvestigationSourceArea SourceArea { get; init; }
    public required DateTimeOffset LaunchedAtUtc { get; init; }
    public required TimeRange SelectedRange { get; init; }   // TimeRange(DateTimeOffset start, DateTimeOffset end)
    public IncidentSeedEvidenceRef? EvidenceRef { get; init; }
    public IncidentWorkloadScope? CandidateScope { get; init; }
}

// IncidentInvestigationLauncher.Launch(seed) navigates to /incident-timeline
```

### Where to add the button

Find the panel header actions block (contains clear + close buttons). Add the new button before the close button:

```razor
@if (_canInvestigate)
{
    <button class="top-bar-icon-btn agent-panel-header-btn"
            @onclick="LaunchInvestigation"
            title="Investigate current selection in Incident Timeline">
        <FluentIcon Value="@(new Icons.Regular.Size16.Timeline())" Width="14px" />
    </button>
}
```

### Code to add in `@code` block

```csharp
private bool _canInvestigate =>
    SelectionCtx.GetSelection<object>("aks") is not null;

private void LaunchInvestigation()
{
    var now = DateTimeOffset.UtcNow;
    var seed = new IncidentInvestigationSeed
    {
        SourceArea    = IncidentInvestigationSourceArea.Observability,
        LaunchedAtUtc = now,
        SelectedRange = new TimeRange(now.AddHours(-2), now)
        // CandidateScope is null — user fills in the form on the timeline page
    };
    InvestigationLauncher.Launch(seed);
}
```

### Namespaces to add at the top of the file (if not already present)

```razor
@using SwebKit.Core.Abstractions
@using SwebKit.Core.Models
@using SwebKit.App.Services
```

### Acceptance criteria

- "Investigate in Timeline" button appears in the panel header.
- Button is hidden when no AKS selection is active (`_canInvestigate` is false).
- Clicking it navigates to `/incident-timeline`.
- `dotnet build` passes.

---

## ✅ Acceptance Checklist (full phase)

- [ ] T1: `AgentContextBuilder` constructor takes `ISelectionContext` and `IAlertMonitorService`
- [ ] T1: `BuildContext()` appends "Selected:" line when at least one selection is set
- [ ] T1: `BuildContext()` appends alert lines (max 3) when recent alerts exist
- [ ] T2: `InvestigatePodIssueTool` registered, returns merged JSON with status + logs + events
- [ ] T3: `AnalyzeQueueHealthTool` registered, returns merged JSON with health_summary
- [ ] T4: Assistant bubbles render markdown (tables, bold, code blocks)
- [ ] T5: "Investigate in Timeline" button works and navigates to `/incident-timeline`
- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` passes with zero failures

---

## ❌ Deliberately Out of Scope

| Feature | Reason |
|---------|--------|
| `PredictImpactTool`, `FindSimilarIssuesTool`, `AnalyzeTrendsTool` | Require persistent history storage not yet built |
| `ExplainAlertTool`, `CompareDeploymentsTool`, `AnalyzeDeploymentTool` | Lower value; defer to Phase 3 |
| Alert auto-trigger on `AlertFired` event | Complex wiring; lower priority than core tasks |
| Conversation history search | Nice-to-have, not core value |
| Session save/restore | Not needed yet |
| Streaming responses | `MistralHttpClient` does not support streaming yet |
| Smart prompt template system | Current single template is sufficient |
| Context caching | Premature optimization |
| Response rating (thumbs up/down) | No analytics backend yet |

---

## 🔗 Related Documents

- [Phase 1: Foundation (Done)](phase-1-foundation.md)
- [Phase 3: Automation](phase-3-automation.md)
- [Architecture](../architecture/architecture.md)
- [Testing Strategy](testing-strategy.md)

---

*Document created: 2026-06-29*
*Last updated: 2026-07-01*
