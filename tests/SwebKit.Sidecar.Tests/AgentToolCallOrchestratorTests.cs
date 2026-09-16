using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the tool-orchestration seam extracted out of <see cref="SidecarAgentChatService"/>
/// directly, rather than only through a full turn: the three visibility gates and the
/// <c>tool_call</c>/<c>tool_result</c> step pair the frontend's step list is built from.</summary>
public class AgentToolCallOrchestratorTests
{
    private sealed class SelectionProbeTool : IAgentTool
    {
        public string Name => "selection_probe";
        public string Description => "Captures ambient selection";
        public FeatureArea FeatureArea => FeatureArea.Aks;
        public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("{\"type\":\"object\",\"properties\":{}}");
        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
            Task.FromResult(JsonSerializer.Serialize(AgentExecutionContext.Selection));
    }

    private static AgentToolCallOrchestrator CreateOrchestrator() => new(new AgentToolRegistry([
        new FakeReadTool("read_aks", FeatureArea.Aks),
        new FakeMutateTool("mutate_aks", FeatureArea.Aks),
        new FakeReadTool("read_redis", FeatureArea.Redis),
        new FakeReadTool("read_traces", FeatureArea.Observability),
    ]));

    private static List<string> Names(IReadOnlyList<ToolDefinition> tools) =>
        tools.Select(t => t.Name).OrderBy(n => n).ToList();

    [Theory]
    [InlineData("ask_and_do", "ask_and_do")]
    [InlineData("ask", "ask")]
    [InlineData("Ask_And_Do", "ask")]   // a typo must never silently grant the permissive mode
    [InlineData("", "ask")]
    [InlineData(null, "ask")]
    public void NormalizeMode_AnythingButExactAskAndDo_CollapsesToAsk(string? input, string expected)
    {
        Assert.Equal(expected, AgentToolCallOrchestrator.NormalizeMode(input));
    }

    [Theory]
    [InlineData("workspace", "workspace")]
    [InlineData("Workspace", "feature")]
    [InlineData("feature", "feature")]
    [InlineData(null, "feature")]
    public void NormalizeScope_AnythingButExactWorkspace_CollapsesToFeature(string? input, string expected)
    {
        Assert.Equal(expected, AgentToolCallOrchestrator.NormalizeScope(input));
    }

    [Fact]
    public void ResolveTools_WithoutToolCallingCapability_ReturnsNothing()
    {
        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: false, "ask_and_do", context: null, "workspace");

        Assert.Empty(tools);
    }

    [Fact]
    public void ResolveTools_AskMode_DropsMutatingTools()
    {
        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: true, "ask", context: null, "feature");

        Assert.Equal(["read_aks", "read_redis", "read_traces"], Names(tools));
    }

    [Fact]
    public void ResolveTools_AskAndDoWithNoContext_KeepsEveryAreasTools()
    {
        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: true, "ask_and_do", context: null, "feature");

        Assert.Equal(["mutate_aks", "read_aks", "read_redis", "read_traces"], Names(tools));
    }

    [Fact]
    public void ResolveTools_FeatureScopeWithArea_KeepsThatAreaPlusObservability()
    {
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: true, "ask_and_do", context, "feature");

        // Observability is cross-cutting and deliberately exempt from the per-area filter.
        Assert.Equal(["mutate_aks", "read_aks", "read_traces"], Names(tools));
    }

    [Fact]
    public void ResolveTools_UnparseableAreaName_SkipsTheAreaGateRatherThanReturningNothing()
    {
        var context = new AgentChatContext { FeatureArea = "NotAnArea" };

        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: true, "ask_and_do", context, "feature");

        Assert.Equal(["mutate_aks", "read_aks", "read_redis", "read_traces"], Names(tools));
    }

    [Fact]
    public void ResolveTools_WorkspaceScope_SkipsThePerAreaFilterForTheTurn()
    {
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var tools = CreateOrchestrator().ResolveTools(hasToolCalling: true, "ask_and_do", context, "workspace");

        Assert.Equal(["mutate_aks", "read_aks", "read_redis", "read_traces"], Names(tools));
    }

    [Fact]
    public void BuildStepTrackingToolExecutor_NoTools_ReturnsNullSoNoExecutorIsSentToTheModel()
    {
        Assert.Null(CreateOrchestrator().BuildStepTrackingToolExecutor([], []));
    }

    [Fact]
    public async Task StepTrackingToolExecutor_RecordsACallThenResultPair_AndReturnsTheRawToolOutput()
    {
        var orchestrator = CreateOrchestrator();
        var tools = orchestrator.ResolveTools(hasToolCalling: true, "ask", context: null, "feature");
        var steps = new List<AgentChatStep>();
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, steps)!;

        var result = await executor("read_aks", JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(2, steps.Count);
        Assert.Equal("tool_call", steps[0].Type);
        Assert.Equal("read_aks", steps[0].ToolName);
        Assert.Equal("Calling read_aks", steps[0].Summary);
        Assert.Equal("tool_result", steps[1].Type);
        Assert.Equal("read_aks", steps[1].ToolName);
        Assert.Equal("{}", steps[1].Summary);
    }

    [Fact]
    public async Task StepTrackingToolExecutor_MutatingTool_SaysPreparingRatherThanCalling()
    {
        var orchestrator = CreateOrchestrator();
        var tools = orchestrator.ResolveTools(hasToolCalling: true, "ask_and_do", context: null, "workspace");
        var steps = new List<AgentChatStep>();
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, steps)!;

        await executor("mutate_aks", JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.Equal("Preparing mutate_aks (mutation)", steps[0].Summary);
    }

    [Fact]
    public async Task StepTrackingToolExecutor_ScopesSelectionToTheToolCall()
    {
        var orchestrator = new AgentToolCallOrchestrator(new AgentToolRegistry([new SelectionProbeTool()]));
        var tools = orchestrator.ResolveTools(true, "ask", null, "feature");
        var selection = new Dictionary<string, string> { ["namespace"] = "dev" };
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, [], selection)!;

        var result = await executor("selection_probe", JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.Equal("dev", JsonDocument.Parse(result).RootElement.GetProperty("namespace").GetString());
        Assert.Null(AgentExecutionContext.Selection);
    }

    [Theory]
    [InlineData("", "Empty result")]
    [InlineData("short", "short")]
    public void SummarizeToolResult_ShortOrEmptyResults_PassThroughOrGetAPlaceholder(string result, string expected)
    {
        Assert.Equal(expected, AgentToolCallOrchestrator.SummarizeToolResult(result));
    }

    [Fact]
    public void SummarizeToolResult_LongResult_TruncatesToEightyCharsPlusEllipsis()
    {
        var summary = AgentToolCallOrchestrator.SummarizeToolResult(new string('x', 200));

        Assert.Equal(new string('x', 80) + "…", summary);
    }
}
