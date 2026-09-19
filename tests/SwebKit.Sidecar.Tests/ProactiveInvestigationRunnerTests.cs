using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>A tool the test fully controls — records invocations, returns a canned payload, and
/// can be marked <see cref="ToolKind.Mutate"/> to prove the ask-mode gate drops it.</summary>
internal sealed class FakeInvestigationTool(string name, FeatureArea area, ToolKind kind = ToolKind.Read) : IAgentTool
{
    private static readonly JsonElement Schema = AgentToolSchema.Parse("""{ "type": "object", "properties": {} }""");

    public List<JsonElement> Calls { get; } = [];
    public string Result { get; set; } = """{"ok":true}""";

    public string Name => name;
    public string Description => $"fake {name}";
    public JsonElement ParametersSchema => Schema;
    public FeatureArea FeatureArea => area;
    public ToolKind Kind => kind;

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        Calls.Add(arguments.Clone());
        return Task.FromResult(Result);
    }
}

/// <summary>Model client driven by a scripted <see cref="OnChat"/> callback — the test decides
/// whether the "model" calls tools (through the executor the runner hands it) and what final
/// text it produces.</summary>
internal sealed class ScriptedInvestigationModelClient : IAgentModelClient
{
    public AgentModelRequest? LastRequest { get; private set; }
    public Func<AgentModelRequest, Func<string, JsonElement, CancellationToken, Task<string>>?, CancellationToken, Task<AgentChatResult>>? OnChat { get; set; }
    public string ChatReplyText { get; set; } = "reply";

    public async Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        LastRequest = request;
        if (OnChat is not null)
            return await OnChat(request, toolExecutor, ct);
        return new AgentChatResult { Text = ChatReplyText, ToolsUsed = [], Elapsed = TimeSpan.Zero };
    }

    public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
        Task.FromResult(new AgentModelResponse
        {
            FinishReason = AgentFinishReason.Stop,
            Content = null,
            AssistantMessage = new AgentMessage { Role = "assistant" },
        });

    public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        throw new NotSupportedException("runner tests only use ChatAsync");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}

public class ProactiveInvestigationRunnerTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    private static ProactiveInvestigationRunner BuildRunner(
        ScriptedInvestigationModelClient modelClient,
        AgentToolRegistry registry,
        TimeSpan? budget = null) =>
        new(modelClient, registry, new ProfileRepository(), new DemoModeService(),
            NullLogger<ProactiveInvestigationRunner>.Instance, budget);

    private static AlertFiredEvent Fired() => new(
        RuleId: "r1",
        RuleName: "pod health",
        Source: AlertRuleSource.AksPodHealth,
        Severity: AlertSeverity.Warning,
        Message: "pod api-7c9f is not ready",
        Detail: "namespace prod",
        FiredAt: DateTimeOffset.UtcNow,
        ProfileName: "test");

    [Fact]
    public async Task NoToolsResolved_ReturnsNull()
    {
        var modelClient = new ScriptedInvestigationModelClient();
        var runner = BuildRunner(modelClient, new AgentToolRegistry([]));

        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);

        Assert.Null(result);
        Assert.Null(modelClient.LastRequest); // never even asked the model
    }

    [Fact]
    public async Task ModelCallsToolsAndReturnsJson_ProducesStructuredResult_WithAuditTrail()
    {
        var tool = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var modelClient = new ScriptedInvestigationModelClient
        {
            OnChat = async (_, executor, _) =>
            {
                Assert.NotNull(executor);
                var toolResult = await executor!("fake_read", EmptyArgs, CancellationToken.None);
                Assert.Contains("ok", toolResult);
                return new AgentChatResult
                {
                    Text = """{"hypothesis":"pod OOMKilled","evidence":["restart count 7","OOM in events"],"severity":"high","suggested_next_steps":["raise memory limit"]}""",
                    ToolsUsed = [],
                    Elapsed = TimeSpan.FromSeconds(1),
                };
            },
        };
        var runner = BuildRunner(modelClient, new AgentToolRegistry([tool]));

        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("pod OOMKilled", result!.Hypothesis);
        Assert.Equal(["restart count 7", "OOM in events"], result.Evidence);
        Assert.Equal("high", result.Severity);
        Assert.Equal(["raise memory limit"], result.SuggestedNextSteps);
        Assert.Single(tool.Calls);
        Assert.Contains("fake_read", result.ToolsUsed); // audit trail came from the step tracker
    }

    [Fact]
    public async Task ModelReturnsProse_HypothesisFallsBackToRawText_EvidenceEmpty()
    {
        var tool = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var modelClient = new ScriptedInvestigationModelClient { ChatReplyText = "the pod is crashlooping after the last deploy" };
        var runner = BuildRunner(modelClient, new AgentToolRegistry([tool]));

        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("the pod is crashlooping after the last deploy", result!.Hypothesis);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Severity);
    }

    [Fact]
    public async Task AskModeResolution_MutateToolsNeverReachTheModel()
    {
        var read = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var mutate = new FakeInvestigationTool("propose_fake_write", FeatureArea.Aks, ToolKind.Mutate);
        var modelClient = new ScriptedInvestigationModelClient();
        var runner = BuildRunner(modelClient, new AgentToolRegistry([read, mutate]));

        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(modelClient.LastRequest);
        Assert.Contains(modelClient.LastRequest!.Tools, t => t.Name == "fake_read");
        Assert.DoesNotContain(modelClient.LastRequest.Tools, t => t.Name == "propose_fake_write");
    }

    [Fact]
    public async Task ModelReturnsEmptyText_ReturnsNull_SoCallerCanFallBack()
    {
        var tool = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var modelClient = new ScriptedInvestigationModelClient { ChatReplyText = "   " };
        var runner = BuildRunner(modelClient, new AgentToolRegistry([tool]));

        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task WallClockBudgetExceeded_ReturnsNull_Promptly()
    {
        var tool = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var modelClient = new ScriptedInvestigationModelClient
        {
            OnChat = async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return new AgentChatResult { Text = "too late", ToolsUsed = [], Elapsed = TimeSpan.Zero };
            },
        };
        var runner = BuildRunner(modelClient, new AgentToolRegistry([tool]), budget: TimeSpan.FromMilliseconds(50));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None);
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"budget cancellation took {sw.Elapsed}");
    }

    [Fact]
    public async Task ModelClientThrows_Propagates_SoCallerCanLogAndFallBack()
    {
        var tool = new FakeInvestigationTool("fake_read", FeatureArea.Aks);
        var modelClient = new ScriptedInvestigationModelClient
        {
            OnChat = (_, _, _) => throw new InvalidOperationException("provider down"),
        };
        var runner = BuildRunner(modelClient, new AgentToolRegistry([tool]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.InvestigateAsync(Fired(), "Aks/prod", CancellationToken.None));
    }
}
