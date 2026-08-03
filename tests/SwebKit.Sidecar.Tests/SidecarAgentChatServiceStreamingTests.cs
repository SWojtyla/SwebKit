using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Plays back a fixed, scripted sequence of <see cref="AgentStreamEvent"/>s (optionally
/// ending in a thrown exception instead of a terminal event) so tests can assert on how
/// <see cref="SidecarAgentChatService.SendStreamAsync"/> forwards and reacts to them without a real
/// model client.</summary>
internal sealed class ScriptedStreamingModelClient : IAgentModelClient
{
    private readonly Func<IAsyncEnumerable<AgentStreamEvent>> _factory;

    public ScriptedStreamingModelClient(Func<IAsyncEnumerable<AgentStreamEvent>> factory) => _factory = factory;

    public Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct) =>
        throw new NotSupportedException("This fake only supports the streaming path.");

    public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
        throw new NotSupportedException("This fake only supports the streaming path.");

    public IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct) =>
        _factory();

    public static async IAsyncEnumerable<AgentStreamEvent> TokensThenDone(string fullText, IReadOnlyList<string>? toolsUsed = null)
    {
        foreach (var ch in fullText)
        {
            yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Token, Token = ch.ToString() };
            await Task.Yield();
        }

        yield return new AgentStreamEvent
        {
            Kind = AgentStreamEventKind.Done,
            Result = new AgentChatResult { Text = fullText, ToolsUsed = toolsUsed ?? [], Elapsed = TimeSpan.Zero }
        };
    }

    public static async IAsyncEnumerable<AgentStreamEvent> TokenThenThrow(string partialToken, string exceptionMessage)
    {
        yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Token, Token = partialToken };
        await Task.Yield();
        throw new InvalidOperationException(exceptionMessage);
    }
}

public class SidecarAgentChatServiceStreamingTests
{
    private static SidecarAgentChatService CreateService(
        Func<IAsyncEnumerable<AgentStreamEvent>> streamFactory,
        out UserSettingsRepository settings)
    {
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = AgentCapability.ChatOnly });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();

        return new SidecarAgentChatService(new ScriptedStreamingModelClient(streamFactory), registry, profiles, settings, demo);
    }

    private static async Task<List<AgentStreamEvent>> Drain(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in stream)
            events.Add(evt);
        return events;
    }

    [Fact]
    public async Task SendStreamAsync_ForwardsEveryEventInOrder_EndingInDone()
    {
        var service = CreateService(() => ScriptedStreamingModelClient.TokensThenDone("hi"), out _);

        var events = await Drain(service.SendStreamAsync(sessionId: null, "hello"));

        Assert.Equal(AgentStreamEventKind.Token, events[0].Kind);
        Assert.Equal("h", events[0].Token);
        Assert.Equal(AgentStreamEventKind.Token, events[1].Kind);
        Assert.Equal("i", events[1].Token);
        Assert.Equal(AgentStreamEventKind.Done, events[2].Kind);
        Assert.Equal("hi", events[2].Result!.Text);
    }

    [Fact]
    public async Task SendStreamAsync_OnDone_RecordsOnlyTheFinalTextInHistory_NotIndividualTokens()
    {
        var service = CreateService(() => ScriptedStreamingModelClient.TokensThenDone("full reply"), out _);

        await Drain(service.SendStreamAsync("session-1", "hello"));

        // 1 user message + 1 assistant message — never one entry per streamed token.
        Assert.Equal(2, service.GetHistoryCount("session-1"));
    }

    [Fact]
    public async Task SendStreamAsync_ModelClientThrowsMidStream_YieldsErrorEvent_AsTheLastEvent()
    {
        var service = CreateService(
            () => ScriptedStreamingModelClient.TokenThenThrow("partial", "connection reset"),
            out _);

        var events = await Drain(service.SendStreamAsync(sessionId: null, "hello"));

        Assert.Equal(AgentStreamEventKind.Token, events[0].Kind);
        var error = Assert.Single(events, e => e.Kind == AgentStreamEventKind.Error);
        Assert.Contains("connection reset", error.ErrorMessage);
        Assert.Same(error, events[^1]);
    }

    [Fact]
    public async Task SendStreamAsync_ModelClientThrowsMidStream_RecordsErrorInHistory_NotThePartialToken()
    {
        var service = CreateService(
            () => ScriptedStreamingModelClient.TokenThenThrow("partial", "connection reset"),
            out _);

        await Drain(service.SendStreamAsync("session-err", "hello"));

        Assert.Equal(2, service.GetHistoryCount("session-err")); // user + the error message
    }

    [Fact]
    public async Task SendStreamAsync_DifferentSessionIds_HaveIndependentHistory()
    {
        var service = CreateService(() => ScriptedStreamingModelClient.TokensThenDone("ok"), out _);

        await Drain(service.SendStreamAsync("session-a", "hello from a"));
        await Drain(service.SendStreamAsync("session-b", "hello from b"));

        Assert.Equal(2, service.GetHistoryCount("session-a"));
        Assert.Equal(2, service.GetHistoryCount("session-b"));

        service.ClearHistory("session-a");

        Assert.Equal(0, service.GetHistoryCount("session-a"));
        Assert.Equal(2, service.GetHistoryCount("session-b"));
    }
}
