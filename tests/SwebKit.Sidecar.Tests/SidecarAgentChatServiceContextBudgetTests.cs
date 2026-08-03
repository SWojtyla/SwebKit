using System.Runtime.CompilerServices;
using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Records every <see cref="AgentModelRequest"/> passed to <see cref="ChatAsync"/> and
/// <see cref="CompleteAsync"/> so tests can inspect exactly what history a later turn actually sent
/// — the only reliable way to observe workspace-intelligence Module 5's rolling summarization, since
/// <see cref="SidecarAgentChatService"/> exposes no direct accessor for a session's raw history
/// content. <see cref="CompleteAsync"/>'s behavior (the summarization model call) is configurable
/// per test via <see cref="OnComplete"/>.</summary>
internal sealed class ContextBudgetModelClient : IAgentModelClient
{
    public List<AgentModelRequest> ChatRequests { get; } = [];
    public List<AgentModelRequest> CompleteRequests { get; } = [];
    public Func<AgentModelRequest, string>? OnComplete { get; set; }
    public string ChatReplyText { get; set; } = "reply";

    public Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        ChatRequests.Add(request);
        return Task.FromResult(new AgentChatResult { Text = ChatReplyText, ToolsUsed = [], Elapsed = TimeSpan.Zero });
    }

    public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct)
    {
        CompleteRequests.Add(request);
        if (OnComplete is null)
            throw new NotSupportedException("This test's ContextBudgetModelClient has no OnComplete configured.");

        var content = OnComplete(request);
        return Task.FromResult(new AgentModelResponse
        {
            FinishReason = AgentFinishReason.Stop,
            Content = content,
            AssistantMessage = new AgentMessage { Role = "assistant", Content = content },
        });
    }

    public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        throw new NotSupportedException("This test's ContextBudgetModelClient only supports the non-streaming path.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}

public class SidecarAgentChatServiceContextBudgetTests
{
    private static SidecarAgentChatService CreateService(
        ContextBudgetModelClient client, int? contextWindowTokens, out UserSettingsRepository settings)
    {
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Test",
            Capability = AgentCapability.ChatOnly,
            ContextWindowTokens = contextWindowTokens,
        });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();

        return new SidecarAgentChatService(client, registry, profiles, settings, demo);
    }

    [Fact]
    public async Task GetContextUsagePercent_SessionNeverTouched_ReturnsZero()
    {
        var service = CreateService(new ContextBudgetModelClient(), contextWindowTokens: 4096, out _);

        Assert.Equal(0, service.GetContextUsagePercent("never-used"));
    }

    [Fact]
    public async Task SendAsync_ShortConversation_DefaultContextWindow_NeverSummarizes()
    {
        var client = new ContextBudgetModelClient();
        var service = CreateService(client, contextWindowTokens: null, out _); // null -> 4096 default

        for (var i = 0; i < 4; i++)
        {
            var reply = await service.SendAsync("session-a", $"short message {i}");
            Assert.False(reply.Summarized);
        }

        Assert.True(service.GetContextUsagePercent("session-a") > 0);
        Assert.True(service.GetContextUsagePercent("session-a") < 100); // nowhere near a 4096-token window
    }

    [Fact]
    public async Task SendAsync_TinyContextWindow_EnoughHistory_SummarizesOlderMessages_KeepsRecentVerbatim()
    {
        var client = new ContextBudgetModelClient { OnComplete = _ => "SUMMARY-TEXT" };
        var service = CreateService(client, contextWindowTokens: 10, out _); // tiny -> threshold crossed almost immediately

        // 4 turns build up 8 history messages (4 user + 4 assistant) — not yet enough for
        // summarization to fire (needs > 6 messages excluding the just-sent one).
        for (var i = 1; i <= 4; i++)
        {
            var reply = await service.SendAsync("session-b", $"distinct-user-message-{i}");
            Assert.False(reply.Summarized);
        }

        // 5th turn: history now has 8 messages before this turn's own user message is excluded —
        // summarization should fire.
        var fifthReply = await service.SendAsync("session-b", "distinct-user-message-5");

        Assert.True(fifthReply.Summarized);
        Assert.Single(client.CompleteRequests); // exactly one summarization call, not one per turn

        // Prove it via the NEXT turn's actual outgoing history, not just the flag.
        await service.SendAsync("session-b", "distinct-user-message-6");
        var lastSentHistory = client.ChatRequests[^1].History;

        Assert.Contains(lastSentHistory, m => m.Role == "system" && m.Content!.Contains("SUMMARY-TEXT"));
        Assert.DoesNotContain(lastSentHistory, m => m.Content != null && m.Content.Contains("distinct-user-message-1"));
        // The most recent turns must survive verbatim, not be swept into the summary.
        Assert.Contains(lastSentHistory, m => m.Content == "distinct-user-message-5");
    }

    [Fact]
    public async Task SendAsync_SummarizationModelCallThrows_FailsOpen_TurnStillSucceeds_HistoryUntouched()
    {
        var client = new ContextBudgetModelClient
        {
            OnComplete = _ => throw new InvalidOperationException("summarizer unreachable"),
        };
        var service = CreateService(client, contextWindowTokens: 10, out _);

        for (var i = 1; i <= 5; i++)
            await service.SendAsync("session-c", $"msg-{i}");

        var sixthReply = await service.SendAsync("session-c", "msg-6");

        Assert.False(sixthReply.Error);
        Assert.False(sixthReply.Summarized); // the summarization attempt failed, so it never "happened"
        // History keeps growing normally (capped only by the unrelated _maxHistory=20 trim) rather
        // than being silently corrupted by a half-applied summarization.
        Assert.Equal(12, service.GetHistoryCount("session-c")); // 6 turns * (user + assistant)
    }

    [Fact]
    public async Task SendAsync_ToolCallTurn_PopulatesStepsWithCallThenResultPair()
    {
        var registry = new AgentToolRegistry([new FakeAgentTool()]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = AgentCapability.ToolCalling });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();

        var modelClient = new RecordingToolInvokingModelClient();
        var service = new SidecarAgentChatService(modelClient, registry, profiles, settings, demo);

        var reply = await service.SendAsync("hello");

        Assert.Equal(2, reply.Steps.Count);
        Assert.Equal("tool_call", reply.Steps[0].Type);
        Assert.Equal("fake_tool", reply.Steps[0].ToolName);
        Assert.Equal("tool_result", reply.Steps[1].Type);
        Assert.Equal("fake_tool", reply.Steps[1].ToolName);
        Assert.Equal("{}", reply.Steps[1].Summary);
    }

    /// <summary>Actually invokes the tool executor it's given (unlike <c>FakeAgentModelClient</c>,
    /// which never calls its own executor) so <see cref="AgentChatStep"/> recording can be observed
    /// end-to-end.</summary>
    private sealed class RecordingToolInvokingModelClient : IAgentModelClient
    {
        public async Task<AgentChatResult> ChatAsync(
            AgentModelRequest request,
            Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
            CancellationToken ct)
        {
            if (toolExecutor is not null)
                await toolExecutor("fake_tool", JsonDocument.Parse("{}").RootElement, ct);

            return new AgentChatResult { Text = "done", ToolsUsed = ["fake_tool"], Elapsed = TimeSpan.Zero };
        }

        public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
            AgentModelRequest request,
            Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Done, Result = new AgentChatResult { Text = "done", Elapsed = TimeSpan.Zero } };
        }
    }
}
