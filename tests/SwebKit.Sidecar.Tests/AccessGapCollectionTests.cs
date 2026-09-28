using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>agent-colleague item 2 — the step-tracking executor's access_denied collection:
/// every <c>{"status":"access_denied"}</c> tool result parses into an <see cref="AccessGap"/>
/// on the caller's per-turn list, deduped, while ordinary results and errors add nothing.
/// Also covers the runner threading them onto <c>ProactiveInvestigationResult</c>.</summary>
public class AccessGapCollectionTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    private const string DenialJson = """
        {"status":"access_denied","capability":"service-bus.data",
         "featureArea":"ServiceBus","requiredAccess":"Azure Service Bus Data Receiver",
         "guidance":"Ask a resource owner to assign the role.",
         "detail":"403 from https://prod-sb.servicebus.windows.net/orders/messages?sig=SECRET"}
        """;

    private sealed class DenialTool(string name, ToolKind kind = ToolKind.Read) : IAgentTool
    {
        public int Calls { get; private set; }
        public string Name => name;
        public string Description => "denial fake";
        public ToolKind Kind => kind;
        public FeatureArea FeatureArea => FeatureArea.Aks;
        public JsonElement ParametersSchema { get; } =
            AgentToolSchema.Parse("""{"type":"object","properties":{}}""");
        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(DenialJson);
        }
    }

    [Fact]
    public async Task Executor_AccessDeniedResult_CollectedOnTheCallerList()
    {
        var tool = new DenialTool("peek_dlq");
        var orchestrator = new AgentToolCallOrchestrator(new AgentToolRegistry([tool]));
        var tools = orchestrator.ResolveTools(true, "ask_and_do", null, "workspace");
        var denials = new List<AccessGap>();
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, [], accessDenials: denials)!;

        var args = JsonDocument.Parse("""{"namespace":"prod-sb","entity_path":"orders"}""").RootElement;
        await executor("peek_dlq", args, CancellationToken.None);

        var gap = Assert.Single(denials);
        Assert.Equal("ServiceBus", gap.FeatureArea);
        Assert.Equal("service-bus.data", gap.Capability);
        Assert.Equal("Azure Service Bus Data Receiver", gap.RequiredAccess);
        Assert.Equal("prod-sb/orders", gap.Resource);   // args → best-effort resource
        Assert.Equal("peek_dlq", gap.Tool);
        Assert.DoesNotContain("SECRET", gap.Detail);    // URI reduced to host
    }

    [Fact]
    public async Task Executor_SameDenialTwice_Deduped()
    {
        var tool = new DenialTool("peek_dlq", ToolKind.Mutate); // mutate → never memoized, so
                                                              // both calls really execute
        var orchestrator = new AgentToolCallOrchestrator(new AgentToolRegistry([tool]));
        var tools = orchestrator.ResolveTools(true, "ask_and_do", null, "workspace");
        var denials = new List<AccessGap>();
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, [], accessDenials: denials)!;

        await executor("peek_dlq", EmptyArgs, CancellationToken.None);
        await executor("peek_dlq", EmptyArgs, CancellationToken.None);

        Assert.Equal(2, tool.Calls);
        Assert.Single(denials); // identical gap collapses — the report lists it once
    }

    [Fact]
    public async Task Executor_NoCollector_DenialResultsStillFlowButAreNotTracked()
    {
        var tool = new DenialTool("peek_dlq");
        var orchestrator = new AgentToolCallOrchestrator(new AgentToolRegistry([tool]));
        var tools = orchestrator.ResolveTools(true, "ask_and_do", null, "workspace");
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, [])!;

        var result = await executor("peek_dlq", EmptyArgs, CancellationToken.None);

        Assert.Contains("access_denied", result); // the model still sees the denial
    }

    [Fact]
    public async Task Executor_NormalResult_NothingCollected()
    {
        var orchestrator = new AgentToolCallOrchestrator(new AgentToolRegistry([
            new FakeReadTool("read_aks", FeatureArea.Aks)]));
        var tools = orchestrator.ResolveTools(true, "ask", null, "feature");
        var denials = new List<AccessGap>();
        var executor = orchestrator.BuildStepTrackingToolExecutor(tools, [], accessDenials: denials)!;

        await executor("read_aks", EmptyArgs, CancellationToken.None);

        Assert.Empty(denials);
    }

    [Fact]
    public async Task Runner_ToolLoopDenials_LandOnTheInvestigationResult()
    {
        var denied = new DenialTool("peek_dlq_denied");
        var registry = new AgentToolRegistry([new FakeInvestigationTool("fake_read", FeatureArea.Aks), denied]);
        var modelClient = new ScriptedInvestigationModelClient
        {
            OnChat = async (_, executor, ct) =>
            {
                var toolResult = await executor!("peek_dlq_denied",
                    JsonDocument.Parse("""{"namespace":"prod-sb","entity_path":"orders"}""").RootElement, ct);
                Assert.Contains("access_denied", toolResult);
                return new AgentChatResult
                {
                    Text = """{"hypothesis":"can't see the queue","evidence":["denied"]}""",
                    ToolsUsed = [], Elapsed = TimeSpan.Zero,
                };
            },
        };
        var runner = new ProactiveInvestigationRunner(modelClient, registry,
            new ProfileRepository(), new DemoModeService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProactiveInvestigationRunner>.Instance);

        var result = await runner.InvestigateAsync(new AlertFiredEvent(
            "r1", "dlq", AlertRuleSource.ServiceBusDlqDepth, AlertSeverity.Warning,
            "m", "d", DateTimeOffset.UtcNow, "test"), "ServiceBus/prod-sb", null, CancellationToken.None);

        Assert.NotNull(result);
        var gap = Assert.Single(result!.AccessGaps);
        Assert.Equal("ServiceBus", gap.FeatureArea);
        Assert.Equal("prod-sb/orders", gap.Resource);
    }
}
