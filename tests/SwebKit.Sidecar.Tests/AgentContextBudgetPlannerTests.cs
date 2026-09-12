using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the context-budget seam extracted out of <see cref="SidecarAgentChatService"/>
/// directly. The end-to-end rolling-summarization behavior stays asserted through the chat service
/// in <see cref="SidecarAgentChatServiceContextBudgetTests"/>; this pins the planner's own
/// boundaries (what counts toward the estimate, when the threshold fires, and failing open).</summary>
public class AgentContextBudgetPlannerTests
{
    private static AgentConversationSession SessionWith(params string[] contents)
    {
        var session = new AgentConversationSession();
        foreach (var c in contents)
            session.History.Enqueue(new AgentMessage { Role = "user", Content = c });
        return session;
    }

    private static AgentProfile Profile(int? contextWindowTokens) =>
        new() { Id = "p1", DisplayName = "Test", ContextWindowTokens = contextWindowTokens };

    [Theory]
    [InlineData(null, 4096)]  // unknown window is treated conservatively, never as "unlimited"
    [InlineData(0, 4096)]
    [InlineData(-1, 4096)]
    [InlineData(32000, 32000)]
    public void ResolveContextWindow_FallsBackToTheConservativeDefault(int? declared, int expected)
    {
        Assert.Equal(expected, AgentContextBudgetPlanner.ResolveContextWindow(Profile(declared)));
    }

    [Fact]
    public void ResolveContextWindow_NoActiveProfile_UsesTheDefault()
    {
        Assert.Equal(4096, AgentContextBudgetPlanner.ResolveContextWindow(null));
    }

    [Fact]
    public void EstimateFullRequestTokens_CountsToolSchemasAndSystemPrompt_NotJustHistory()
    {
        var tools = new AgentToolRegistry([new FakeReadTool("read_aks", FeatureArea.Aks)]).GetDefinitions();
        AgentMessage[] history = [new() { Role = "user", Content = new string('x', 40) }];

        var withoutTools = AgentContextBudgetPlanner.EstimateFullRequestTokens("prompt", [], history, "hi");
        var withTools = AgentContextBudgetPlanner.EstimateFullRequestTokens("prompt", tools, history, "hi");

        Assert.True(withoutTools > 0);
        Assert.True(withTools > withoutTools);
    }

    [Fact]
    public async Task PrepareHistoryForModelAsync_ExcludesTheJustEnqueuedUserMessage_AndRecordsTheEstimate()
    {
        var planner = new AgentContextBudgetPlanner(new ContextBudgetModelClient());
        var session = SessionWith("older", "pending");

        var (history, summarized) = await planner.PrepareHistoryForModelAsync(
            session, "prompt", [], "pending", Profile(131072), CancellationToken.None);

        Assert.False(summarized);
        Assert.Equal(["older"], history.Select(m => m.Content));
        Assert.Equal(131072, session.LastContextWindowTokens);
        Assert.True(session.LastRequestEstimatedTokens > 0);
    }

    [Fact]
    public async Task PrepareHistoryForModelAsync_OverThresholdButTooFewMessages_DoesNotSummarize()
    {
        // A tiny window crosses the threshold immediately, but with only 6 messages left after
        // excluding the pending one there is nothing older worth summarizing away.
        var client = new ContextBudgetModelClient { OnComplete = _ => "SUMMARY" };
        var planner = new AgentContextBudgetPlanner(client);
        var session = SessionWith("m1", "m2", "m3", "m4", "m5", "m6", "pending");

        var (_, summarized) = await planner.PrepareHistoryForModelAsync(
            session, "prompt", [], "pending", Profile(10), CancellationToken.None);

        Assert.False(summarized);
        Assert.Empty(client.CompleteRequests);
    }

    [Fact]
    public async Task PrepareHistoryForModelAsync_OverThreshold_ReplacesOlderMessagesWithOneSummaryTurn()
    {
        var client = new ContextBudgetModelClient { OnComplete = _ => "SUMMARY" };
        var planner = new AgentContextBudgetPlanner(client);
        var session = SessionWith("m1", "m2", "m3", "m4", "m5", "m6", "m7", "pending");

        var (history, summarized) = await planner.PrepareHistoryForModelAsync(
            session, "prompt", [], "pending", Profile(10), CancellationToken.None);

        Assert.True(summarized);
        Assert.Single(client.CompleteRequests);
        // Summary turn + the 6 most recent kept verbatim, minus the pending message excluded again.
        Assert.Equal(
            ["[Earlier conversation summarized]: SUMMARY", "m3", "m4", "m5", "m6", "m7"],
            history.Select(m => m.Content));
        Assert.Contains("m1", client.CompleteRequests[0].UserMessage);
        Assert.Contains("m2", client.CompleteRequests[0].UserMessage);
    }

    [Fact]
    public async Task PrepareHistoryForModelAsync_SummarizerThrows_FailsOpenAndLeavesHistoryIntact()
    {
        var client = new ContextBudgetModelClient { OnComplete = _ => throw new InvalidOperationException("unreachable") };
        var planner = new AgentContextBudgetPlanner(client);
        var session = SessionWith("m1", "m2", "m3", "m4", "m5", "m6", "m7", "pending");

        var (history, summarized) = await planner.PrepareHistoryForModelAsync(
            session, "prompt", [], "pending", Profile(10), CancellationToken.None);

        Assert.False(summarized);
        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6", "m7"], history.Select(m => m.Content));
        Assert.Equal(8, session.History.Count);
    }

    [Fact]
    public async Task PrepareHistoryForModelAsync_SummarizerReturnsBlank_IsTreatedAsNoSummarization()
    {
        var client = new ContextBudgetModelClient { OnComplete = _ => "   " };
        var planner = new AgentContextBudgetPlanner(client);
        var session = SessionWith("m1", "m2", "m3", "m4", "m5", "m6", "m7", "pending");

        var (history, summarized) = await planner.PrepareHistoryForModelAsync(
            session, "prompt", [], "pending", Profile(10), CancellationToken.None);

        Assert.False(summarized);
        Assert.Equal(7, history.Count);
    }
}
