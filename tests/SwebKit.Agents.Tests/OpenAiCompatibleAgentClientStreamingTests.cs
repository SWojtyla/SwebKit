using System.Net;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Queues canned SSE (or plain) HTTP responses and records the requests it received.</summary>
internal sealed class FakeStreamingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<string> RequestBodies { get; } = [];

    public void EnqueueSse(string sseBody) =>
        _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
        });

    public void EnqueueJson(string json) =>
        _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more SSE responses queued in FakeStreamingHttpMessageHandler.");

        return _responses.Dequeue();
    }
}

internal sealed class NoopCredentialStore : ICredentialStore
{
    public void Set(string key, string secret) { }
    public void Save(string key, string secret) { }
    public string? Get(string key) => null;
    public void Delete(string key) { }
    public IReadOnlyList<string> ListKeys(string prefix = "") => [];
}

public class OpenAiCompatibleAgentClientStreamingTests
{
    private static (OpenAiCompatibleAgentClient Client, FakeStreamingHttpMessageHandler Handler) Build()
    {
        var handler = new FakeStreamingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Local LM Studio",
            Provider = ProviderKind.LmStudio,
            BaseUrl = "http://localhost:1234/v1",
            Model = "test-model",
        });
        settings.Settings.Agent.ActiveProfileId = "p1";

        return (new OpenAiCompatibleAgentClient(httpClient, settings, new NoopCredentialStore()), handler);
    }

    private static AgentModelRequest Request() => new()
    {
        SystemPrompt = "You are a test.",
        UserMessage = "hello",
    };

    private static async Task<List<AgentStreamEvent>> Drain(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in stream)
            events.Add(evt);
        return events;
    }

    [Fact]
    public async Task ChatStreamAsync_TextOnlyReply_StreamsEachTokenThenDoneWithFullText()
    {
        var (client, handler) = Build();
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"content":"Hel"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);

        var events = await Drain(client.ChatStreamAsync(Request(), toolExecutor: null, CancellationToken.None));

        var tokens = events.Where(e => e.Kind == AgentStreamEventKind.Token).Select(e => e.Token).ToList();
        Assert.Equal(["Hel", "lo"], tokens);

        var done = Assert.Single(events, e => e.Kind == AgentStreamEventKind.Done);
        Assert.Equal("Hello", done.Result!.Text);
        Assert.False(done.Result.HitMaxRounds);
        Assert.Same(done, events[^1]); // Done is always last
    }

    [Fact]
    public async Task ChatStreamAsync_RequestBody_SetsStreamTrue()
    {
        var (client, handler) = Build();
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":"stop"}]}

            data: [DONE]

            """);

        await Drain(client.ChatStreamAsync(Request(), toolExecutor: null, CancellationToken.None));

        Assert.Contains("\"stream\":true", handler.RequestBodies[0]);
    }

    // ── Wire format regression coverage ──
    //
    // `AgentMessage.ToWireFormat()` (IAgentModelClient.cs) exists specifically to serialize messages
    // with the OpenAI-compatible lowercase field names ("role"/"content"/"tool_calls") — every one of
    // ChatAsync/CompleteAsync/ChatStreamAsync's request-building code previously serialized the
    // `List<AgentMessage>` directly instead, which (with no naming policy on that ad-hoc
    // `JsonSerializer.Serialize` call) produced PascalCase keys ("Role"/"Content") that real
    // OpenAI-compatible servers reject outright — this was caught via manual LM Studio verification
    // (ai-augmented-app technical-plan.md Module 7), not by any of the mocked/fake-based tests above,
    // since none of them had inspected the actual outgoing JSON body until now. One test per call site
    // so a future edit can't silently reintroduce the bug in just one of the three.

    [Fact]
    public async Task ChatAsync_RequestBody_UsesLowercaseOpenAiFieldNames_NotCSharpPropertyNames()
    {
        var (client, handler) = Build();
        handler.EnqueueJson("""{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"OK"}}]}""");

        await client.ChatAsync(Request(), toolExecutor: null, CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"role\":\"system\"", body);
        Assert.Contains("\"role\":\"user\"", body);
        Assert.DoesNotContain("\"Role\"", body);
        Assert.DoesNotContain("\"Content\"", body);
    }

    [Fact]
    public async Task CompleteAsync_RequestBody_UsesLowercaseOpenAiFieldNames_NotCSharpPropertyNames()
    {
        var (client, handler) = Build();
        handler.EnqueueJson("""{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"OK"}}]}""");

        await client.CompleteAsync(Request(), CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"role\":\"user\"", body);
        Assert.DoesNotContain("\"Role\"", body);
    }

    [Fact]
    public async Task ChatStreamAsync_RequestBody_UsesLowercaseOpenAiFieldNames_NotCSharpPropertyNames()
    {
        var (client, handler) = Build();
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":"stop"}]}

            data: [DONE]

            """);

        await Drain(client.ChatStreamAsync(Request(), toolExecutor: null, CancellationToken.None));

        var body = handler.RequestBodies[0];
        Assert.Contains("\"role\":\"system\"", body);
        Assert.Contains("\"role\":\"user\"", body);
        Assert.DoesNotContain("\"Role\"", body);
    }

    [Fact]
    public async Task ChatStreamAsync_ToolCall_ReassemblesArgumentsFragmentedAcrossChunks_ThenExecutesAndContinues()
    {
        var (client, handler) = Build();
        // Round 1: the model streams a tool call whose id/name arrive in the first fragment and
        // whose `arguments` JSON string arrives split across two more fragments — the realistic
        // shape of OpenAI-compatible streaming tool calls, not a single complete blob.
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_pod_status","arguments":""}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"pod"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\":\"nginx\"}"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """);
        // Round 2: after the tool result is fed back, the model finishes with plain text.
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"content":"Pod is healthy."},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);

        string? capturedToolName = null;
        JsonElement capturedArgs = default;
        Task<string> ToolExecutor(string name, JsonElement args, CancellationToken ct)
        {
            capturedToolName = name;
            capturedArgs = args.Clone();
            return Task.FromResult("""{"status":"Running"}""");
        }

        var events = await Drain(client.ChatStreamAsync(Request(), ToolExecutor, CancellationToken.None));

        Assert.Equal("get_pod_status", capturedToolName);
        Assert.Equal("nginx", capturedArgs.GetProperty("pod").GetString());

        var started = Assert.Single(events, e => e.Kind == AgentStreamEventKind.ToolCallStarted);
        Assert.Equal("get_pod_status", started.ToolName);
        var finished = Assert.Single(events, e => e.Kind == AgentStreamEventKind.ToolCallResult);
        Assert.Equal("get_pod_status", finished.ToolName);

        var done = Assert.Single(events, e => e.Kind == AgentStreamEventKind.Done);
        Assert.Equal("Pod is healthy.", done.Result!.Text);
        Assert.Contains("get_pod_status", done.Result.ToolsUsed);

        // Tool call events must precede the follow-up round's tokens/Done.
        Assert.True(events.IndexOf(finished) < events.IndexOf(done));

        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task ChatStreamAsync_NoToolExecutorProvided_TreatsToolCallsRoundAsFinal()
    {
        var (client, handler) = Build();
        handler.EnqueueSse("""
            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_pod_status","arguments":"{}"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """);

        var events = await Drain(client.ChatStreamAsync(Request(), toolExecutor: null, CancellationToken.None));

        Assert.DoesNotContain(events, e => e.Kind is AgentStreamEventKind.ToolCallStarted or AgentStreamEventKind.ToolCallResult);
        var done = Assert.Single(events, e => e.Kind == AgentStreamEventKind.Done);
        Assert.False(done.Result!.HitMaxRounds);
        Assert.Single(handler.RequestBodies);
    }
}
