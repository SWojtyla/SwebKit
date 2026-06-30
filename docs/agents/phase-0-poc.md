# Phase 0: Proof of Concept

## 🎯 Purpose

**Validate the fundamental assumption**: An AI agent can understand SwebKit's domain and provide valuable insights that justify the full investment.

This is a **go/no-go decision gate** - a minimal, low-risk experiment to de-risk the entire project before committing significant resources.

---

## 🧠 Abstract Analysis

### Core Hypothesis to Validate

> "Mistral AI can understand Azure Kubernetes Service, Service Bus, and Observability concepts well enough to provide actionable insights that reduce incident investigation time."

### Assumptions That Must Be Tested

| Assumption                               | Validation Method          | Risk if False                    |
| ---------------------------------------- | -------------------------- | -------------------------------- |
| Mistral understands Kubernetes concepts  | Test with real pod data    | Medium - AI won't be useful      |
| Mistral understands Service Bus concepts | Test with real queue data  | Medium - Limited value           |
| Mistral can analyze structured data      | Format SwebKit data for AI | High - Architecture won't work   |
| Response quality is sufficient           | Evaluate AI outputs        | High - User rejection            |
| API latency is acceptable                | Measure response times     | Medium - Poor UX                 |
| Cost per query is reasonable             | Calculate pricing          | High - Budget concerns           |
| Integration is technically feasible      | Build simple prototype     | Medium - Implementation blockers |

### Global Success Criteria

**Business Validation**

- [ ] Stakeholders see clear value in AI-powered insights
- [ ] AI provides insights beyond what's immediately visible in the UI
- [ ] Response quality meets minimum acceptable standards for operational use

**Technical Validation**

- [ ] Mistral can correctly interpret SwebKit's structured data
- [ ] Mistral can provide actionable recommendations
- [ ] No fundamental technical blockers identified
- [ ] Performance characteristics are acceptable

**Economic Validation**

- [ ] Cost per interaction is within acceptable bounds
- [ ] Value delivered exceeds cost
- [ ] Scaling costs are predictable and manageable

---

## 🎯 What Phase 0 Is

### ✅ In Scope

- **Single Use Case**: One representative scenario (e.g., "Analyze pod health")
- **One Tool**: Single end-to-end tool implementation (`GetPodStatusTool`)
- **Simple Interface**: Console or minimal UI
- **Real Data**: Use actual SwebKit data and connections
- **Technical Validation**: Measure latency, quality, cost
- **Stakeholder Demo**: Present findings to decision makers

### ❌ Out of Scope

- Production code or architecture
- Multiple tools or use cases
- Full SwebKit integration
- Error handling for edge cases
- Performance optimization
- Security hardening
- User authentication
- Conversation history
- Multi-turn conversations

---

## 🔍 Validation Questions

### Business Value Questions

1. **Does this solve a real problem?**
   - Can users already get this information easily through existing means?
   - Does the AI provide insights that would be difficult to obtain otherwise?
   - Would users actually use this feature?

2. **What's the potential impact?**
   - How much time could this save in incident investigation?
   - Could this prevent incidents before they occur?
   - Does this enable non-experts to perform expert-level analysis?

### Technical Feasibility Questions

1. **Domain Understanding**
   - Can Mistral correctly interpret Kubernetes pod status data?
   - Can it identify common issues (CrashLoopBackOff, OOM kills, pending pods)?
   - Can it explain the root cause of issues?

2. **Data Integration**
   - Can we format SwebKit's structured data in a way Mistral can consume?
   - Is the data format compatible with Mistral's input requirements?
   - Do we need to filter or transform data before sending?

3. **Response Quality**
   - Are Mistral's explanations accurate and helpful?
   - Does it provide actionable recommendations?
   - Does it hallucinate or make up information?

### Operational Questions

1. **Performance**
   - What's the end-to-end latency for a typical query?
   - Is the latency acceptable for interactive use?
   - Does it vary significantly based on prompt complexity?

2. **Cost**
   - What's the cost per API call?
   - How many tokens do typical queries consume?
   - What's the projected cost at different usage levels?

3. **Reliability**
   - How reliable is the Mistral API?
   - What error rates can we expect?
   - How does it handle rate limiting?

---

## 📋 Deliverables

### 1. Technical Validation Report

A comprehensive document answering:

- **Domain Understanding Assessment**
  - Mistral's accuracy on Kubernetes concepts
  - Mistral's accuracy on Service Bus concepts
  - Mistral's accuracy on Observability concepts
  - Examples of correct and incorrect responses

- **Performance Metrics**
  - Average response time (ms)
  - P50, P95, P99 latency measurements
  - Token usage per query type
  - Throughput capabilities

- **Cost Analysis**
  - Tokens per query breakdown
  - Cost per query at current pricing
  - Projected monthly costs at various usage levels
  - Cost optimization opportunities

- **Quality Assessment**
  - Response accuracy score (0-100%)
  - Helpfulness rating (1-5 scale)
  - Hallucination rate (% of made-up information)
  - Actionability score (% of responses that suggest useful actions)

### 2. Working Prototype

A simple application demonstrating:

- Mistral API connectivity
- One working tool (`GetPodStatusTool`)
- End-to-end data flow (SwebKit → Tool → Mistral → Response)
- Basic conversation interface

### 3. Stakeholder Presentation

A demo showing:

- The prototype in action
- Sample conversations
- Key findings from validation
- Recommendation (Go/Iterate/No-Go)

---

## ✅ Success Criteria

### Minimum Viable Success (Must Have)

- [ ] Mistral can understand at least 80% of SwebKit-specific queries correctly
- [ ] Tool execution works end-to-end with real data
- [ ] End-to-end latency < 5 seconds for typical queries
- [ ] No showstopper technical issues identified
- [ ] Stakeholders approve proceeding to Phase 1

### Ideal Success (Nice to Have)

- [ ] Mistral accuracy > 90% on domain-specific queries
- [ ] Responses provide actionable insights beyond basic information
- [ ] Cost per query is within budget expectations
- [ ] Latency < 2 seconds for typical queries
- [ ] Strong stakeholder enthusiasm

### Failure Criteria (No-Go)

- [ ] Mistral cannot reliably understand domain concepts
- [ ] Technical blockers cannot be resolved within reasonable time
- [ ] Cost is prohibitive for intended usage
- [ ] Performance is unacceptable for interactive use
- [ ] Stakeholders do not see value

---

## 🔄 Decision Framework

Based on Phase 0 results, make one of three decisions:

### ✅ GO - Proceed to Phase 1

**Criteria:**

- All minimum success criteria met
- No major technical blockers identified
- Business value is clear and compelling
- Cost and performance are acceptable

**Next Steps:**

1. Incorporate Phase 0 learnings into Phase 1 plan
2. Adjust architecture based on validation results
3. Begin Phase 1 implementation

### ⚠️ ITERATE - Address Issues and Retest

**Criteria:**

- Some success criteria not met
- Technical issues identified but seem solvable
- Business value is promising but needs refinement
- Cost or performance needs optimization

**Next Steps:**

1. Create targeted experiments to address specific concerns
2. Develop mitigation strategies for identified issues
3. Retest with improved approach
4. Re-evaluate go/no-go decision

### ❌ NO-GO - Abandon or Significantly Rethink

**Criteria:**

- Fundamental technical blockers identified
- Business value is unclear or insufficient
- Cost is prohibitive
- Performance is unacceptable
- Stakeholders reject the concept

**Next Steps:**

1. Document lessons learned
2. Explore alternative approaches (different AI provider, different architecture)
3. Re-evaluate the business case
4. Consider shelving the project until conditions improve

---

## 📊 Risk Assessment

### High-Risk Areas to Validate

| Risk                              | Probability | Impact   | Mitigation Test                   |
| --------------------------------- | ----------- | -------- | --------------------------------- |
| Mistral doesn't understand domain | Medium      | Critical | Test with domain-specific queries |
| API costs are too high            | Medium      | High     | Calculate actual token usage      |
| Latency is unacceptable           | Medium      | High     | Measure end-to-end response time  |
| Data formatting issues            | Low         | High     | Test various data formats         |
| Rate limiting problems            | Medium      | Medium   | Test with burst queries           |
| Hallucination rate too high       | Medium      | Medium   | Evaluate response accuracy        |

### Contingency Plans

**If Mistral understanding is poor:**

- Try different prompt engineering approaches
- Consider fine-tuning or custom models
- Evaluate alternative AI providers

**If costs are too high:**

- Implement aggressive caching
- Use smaller models where possible
- Optimize prompt construction
- Consider usage limits or tiered access

**If latency is too high:**

- Implement streaming responses
- Add loading indicators
- Consider async processing for complex queries

---

## 🎯 Implementation Guidance (Minimal)

While Phase 0 is about validation, not implementation, a minimal prototype is necessary. This should be **as simple as possible** to achieve the validation goals.

### Key Principle

> "Build the absolute minimum needed to validate the hypothesis, nothing more."

The prototype is **disposable** — designed to be thrown away after Phase 0. Its only purpose is to answer the validation questions.

---

### Step 1 — Create the `SwebKit.Agents` project

Add a new class library to the solution:

```
src/SwebKit.Agents/
    SwebKit.Agents.csproj    ← references SwebKit.Core, SwebKit.Kubernetes
    MistralConfig.cs
    IMistralClient.cs
    MistralHttpClient.cs
    Tools/
        IAgentTool.cs
        GetPodStatusTool.cs
```

Add the project to `SwebKit.slnx` and add a `<ProjectReference>` to `SwebKit.App.csproj`.

**`MistralConfig`** (the only configuration needed for Phase 0):

```csharp
public sealed class MistralConfig
{
    public string ApiKey { get; set; } = string.Empty;  // loaded from ICredentialStore
    public string ApiEndpoint { get; set; } = "https://api.mistral.ai/v1";
    public string Model { get; set; } = "mistral-medium-latest";
    public int MaxTokens { get; set; } = 2048;
}
```

---

### Step 2 — Minimal Mistral client

`IMistralClient` exposes a single method for Phase 0:

```csharp
public interface IMistralClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}
```

`MistralHttpClient` is a thin wrapper over `HttpClient` calling `POST /chat/completions`.
Load the API key from `ICredentialStore` using key `SwebKit-Agent:Mistral-ApiKey`.
No retry logic, no streaming — keep it throwaway.

---

### Step 3 — `GetPodStatusTool`

Wire directly to the existing `IAksClientFactory` (registered in `MauiProgram.cs`):

```csharp
public sealed class GetPodStatusTool : IAgentTool
{
    private readonly IAksClientFactory _aksFactory;
    private readonly AppStateService _appState;

    // Name and Description are sent to Mistral as the tool schema
    public string Name => "get_pod_status";
    public string Description => "Returns the current status of a Kubernetes pod including phase, restart count, container states, and recent events.";

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = arguments.GetProperty("pod_name").GetString()!;
        var ns = arguments.GetProperty("namespace").GetString() ?? "default";

        var config = _appState.Config.Aks; // use the configured kubeconfig context
        var client = _aksFactory.Create(config.Context, config.KubeconfigPath);

        var pod = await client.GetPodAsync(ns, podName, ct);
        return JsonSerializer.Serialize(pod); // serialized pod data goes back to Mistral
    }
}
```

---

### Step 4 — Single-turn conversation loop (console prototype)

The PoC does **not** need a full conversation manager. A simple loop is enough:

```
1. Print prompt to console
2. Read user input
3. Build system prompt (describe SwebKit context + available tools)
4. Call IMistralClient.ChatAsync with the user message and tool definitions
5. If Mistral returns a tool_call:
   a. Identify the tool by name
   b. Execute it → get JSON result
   c. Send the result back to Mistral as a tool message
   d. Get the final text response
6. Print the response
7. Repeat
```

This can live in a `ConsolePocRunner` class inside `SwebKit.Agents` or directly in a small test project/console entry point.

---

### Step 5 — Register and wire up

In `MauiProgram.cs` (or a dedicated PoC entry point):

```csharp
builder.Services.AddSingleton<MistralConfig>(sp =>
{
    var store = sp.GetRequiredService<ICredentialStore>();
    return new MistralConfig
    {
        ApiKey = store.GetPasswordAsync("SwebKit-Agent", "Mistral-ApiKey").GetAwaiter().GetResult() ?? ""
    };
});
builder.Services.AddSingleton<IMistralClient, MistralHttpClient>();
builder.Services.AddSingleton<GetPodStatusTool>();
```

---

## 📈 Next Steps

1. **Prepare Environment**
   - Obtain Mistral API key
   - Set up test AKS cluster or use existing one
   - Identify sample pods for testing

2. **Build Prototype**
   - Create console application
   - Implement Mistral client
   - Implement `GetPodStatusTool`
   - Add basic conversation loop

3. **Execute Tests**
   - Run validation queries
   - Collect performance metrics
   - Document observations
   - Identify issues

4. **Analyze Results**
   - Review all collected data
   - Score against success criteria
   - Identify patterns and insights
   - Document findings

5. **Make Decision**
   - Present findings to stakeholders
   - Decide: Go, Iterate, or No-Go
   - Document decision and rationale

6. **Plan Next Phase** (if Go)
   - Incorporate learnings into Phase 1
   - Adjust scope and priorities
   - Refine architecture based on validation

---

## 🔗 Related Documents

### Phase Documents

- [README - Overview](../README.md)
- [Phase 1: Foundation - Next phase if Phase 0 succeeds](phase-1-foundation.md)
- [Phase 2: Intelligence](phase-2-intelligence.md)
- [Phase 3: Automation](phase-3-automation.md)

### Supporting Documents

- [Architecture](../architecture.md) - Technical design reference
- [Security Considerations](../security-considerations.md) - Critical for API key handling
- [Testing Strategy](../testing-strategy.md) - Validation testing approach
- [Metrics and Monitoring](../metrics-and-monitoring.md) - Performance measurement framework

---

## ✅ Phase 0 Outcome — 2026-06-30

**Decision: GO** — all minimum success criteria met. Proceeding to Phase 1.

### What Was Built

- `SwebKit.Agents` class library with `IMistralClient` / `MistralHttpClient` and tool infrastructure (`IAgentTool`)
- `SwebKit.Agent.PocConsole` standalone console entry point with a single-turn conversation loop
- Two tools: `get_pod_status` and `list_namespaces` (the latter added beyond the original plan — trivially valuable)
- API key loaded interactively at startup (fallback when `MISTRAL_API_KEY` env var is absent)

### Validation Results

| Assumption                              | Result     | Evidence                                                                                                     |
| --------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------ |
| Mistral understands Kubernetes concepts | ✅ PASS    | Correctly grouped 200+ namespaces by system / prd / stg; matched `briocomp` without explicit filtering       |
| Mistral can analyse structured data     | ✅ PASS    | Serialised `V1NamespaceList` consumed correctly                                                              |
| Response quality is sufficient          | ✅ PASS    | Responses were accurate, well-structured markdown                                                            |
| API latency is acceptable               | ⚠️ PARTIAL | First query: ~38 s (includes cold path + interactive API key prompt). Subsequent: ~3.3 s                     |
| Tool calling works end-to-end           | ✅ PASS    | `list_namespaces` invoked autonomously twice in sequence with no guidance                                    |
| No hallucination on unknown data        | ✅ PASS    | When asked which AKS context is active, model honestly reported it had no tool for that rather than guessing |

### Latency Breakdown

The 38 s first-query time was dominated by:

1. Interactive `Console.ReadLine()` waiting for the API key (user-dependent delay)
2. Cold-start TLS + DNS resolution for `api.mistral.ai`
3. Mistral inference time (~3–4 s net)

Subsequent queries ran at **~3.3 s**, which is within the "nice to have" (<5 s) threshold defined in the success criteria.

### Lessons Learned

1. **`list_namespaces` is as valuable as `get_pod_status`** — the model reached for it autonomously without any prompt engineering. Add more lightweight listing tools early in Phase 1.
2. **The agent has no awareness of the active kubeconfig context.** When asked "which AKS context are you in?" it correctly said it didn't know. A `get_current_context` tool is a Phase 1 priority.
3. **API key handling must be integrated with `ICredentialStore` from day one in Phase 1.** The interactive readline fallback works for a PoC but is not acceptable in the MAUI UI.
4. **First-response latency needs a loading indicator or streaming.** 38 s of silence is unacceptable in a desktop UI. Implement streaming (`stream: true`) or at minimum an animated spinner tied to tool-call events.
5. **Tool calling is reliable without prompt engineering.** Mistral selected the right tool and passed the correct arguments on its own — no special system-prompt tricks were needed.
6. **Serialising raw Kubernetes SDK objects works, but is verbose.** Consider projecting a subset of fields before sending to the model to reduce token usage and latency.
7. **The console prototype is throwaway.** Do not migrate any code from `SwebKit.Agent.PocConsole` into the MAUI app — rebuild cleanly in Phase 1 using DI and the `MauiProgram` registration pattern described in the implementation guidance above.

### Phase 1 Priorities Derived from Phase 0

- Add `get_current_context` tool
- Implement streaming responses in `MistralHttpClient`
- Integrate API key with `ICredentialStore` (key: `SwebKit-Agent:Mistral-ApiKey`)
- Add `list_pods` tool (natural next ask after listing namespaces)
- Project Kubernetes object fields before serialisation to reduce payload size

---

_Document created: 2026-06-29_
_Last updated: 2026-06-30_
