using System.Net;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Queues canned responses (or exceptions, to simulate a genuinely unreachable server)
/// and records every request the tester actually sent.</summary>
internal sealed class FakeCapabilityHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<string> RequestUrls { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public List<string?> RequestAuthorizationHeaders { get; } = [];

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        _responses.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    public void EnqueueThrow(Exception ex) => _responses.Enqueue(() => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestUrls.Add(request.RequestUri!.ToString());
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
        RequestAuthorizationHeaders.Add(request.Headers.Authorization?.ToString());

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more responses queued in FakeCapabilityHttpMessageHandler.");

        return _responses.Dequeue()();
    }
}

internal sealed class FakeCapabilityCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _values = new();
    public void Set(string key, string secret) => _values[key] = secret;
    public void Save(string key, string secret) => _values[key] = secret;
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _values.Remove(key);
    public IReadOnlyList<string> ListKeys(string prefix = "") => [];
}

public class AgentCapabilityTesterTests
{
    private const string ModelsJson = """{"data":[{"id":"test-model"}]}""";
    private const string ChatOkJson = """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"OK"}}]}""";
    private const string ChatEmptyJson = """{"choices":[{"finish_reason":"length","message":{"role":"assistant","content":null}}]}""";
    private const string ToolCallSupportedJson = """{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"id":"call_1","function":{"name":"echo_test","arguments":"{}"}}]}}]}""";
    private const string ToolCallNotSupportedJson = """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"I can't call tools."}}]}""";

    private static (AgentCapabilityTester Tester, FakeCapabilityHttpMessageHandler Handler) Build() =>
        BuildWithStore(out _);

    private static (AgentCapabilityTester Tester, FakeCapabilityHttpMessageHandler Handler) BuildWithStore(
        out FakeCapabilityCredentialStore store)
    {
        var handler = new FakeCapabilityHttpMessageHandler();
        store = new FakeCapabilityCredentialStore();
        return (new AgentCapabilityTester(new HttpClient(handler), store), handler);
    }

    private static AgentProfile LmStudioProfile() => new()
    {
        Id = "p1",
        DisplayName = "Local",
        Provider = ProviderKind.LmStudio,
        BaseUrl = "http://localhost:1234/v1",
        Model = "test-model",
    };

    [Fact]
    public async Task TestAsync_FullHappyPath_ReportsToolCalling()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.True(result.ServerReachable);
        Assert.True(result.ModelAvailable);
        Assert.True(result.ChatValid);
        Assert.True(result.ToolCallingValid);
        Assert.Equal(AgentCapability.ToolCalling, result.Capability);
        Assert.Equal("Tool calling supported.", result.Diagnostic);
        Assert.Equal(["test-model"], result.AvailableModels);
    }

    [Fact]
    public async Task TestAsync_ToolCallNotSupported_ReportsChatOnly()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallNotSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.True(result.ChatValid);
        Assert.False(result.ToolCallingValid);
        Assert.Equal(AgentCapability.ChatOnly, result.Capability);
        Assert.Equal("Tool calling not supported. Chat-only mode active.", result.Diagnostic);
    }

    [Fact]
    public async Task TestAsync_MiniChatRequest_UsesAGenerousMaxTokens_NotTheOldTinyCapThatBrokeReasoningModels()
    {
        // Regression coverage: a reasoning-capable local model (observed with a Gemma QAT model in
        // LM Studio) can spend its entire token budget on hidden reasoning before any visible
        // content, so the old max_tokens:10 reported "Chat returned empty response" for a model
        // that works fine in real conversation. Assert the actual outgoing request, not just the
        // end-to-end result, so a future edit can't silently shrink the cap back down unnoticed.
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        await tester.TestAsync(LmStudioProfile());

        var chatRequestBody = handler.RequestBodies[1]; // [0]=models, [1]=mini chat, [2]=mini tool call
        Assert.Contains("\"max_tokens\":64", chatRequestBody);
        Assert.DoesNotContain("\"max_tokens\":10", chatRequestBody);
    }

    [Fact]
    public async Task TestAsync_ChatReturnsEmptyContent_ReportsEmptyResponse_AndNeverAttemptsToolCallTest()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatEmptyJson);
        // No third response queued — if the tester incorrectly proceeded to the tool-call test,
        // it would throw InvalidOperationException("No more responses queued"), failing this test.

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.True(result.ServerReachable);
        Assert.False(result.ChatValid);
        Assert.Equal(AgentCapability.Unknown, result.Capability);
        Assert.Equal("Chat returned empty response.", result.Diagnostic);
        Assert.Equal(2, handler.RequestBodies.Count); // models + mini chat only, no tool-call probe
    }

    [Fact]
    public async Task TestAsync_ServerUnreachable_ReportsUnreachable_AndNeverAttemptsChatOrToolTests()
    {
        var (tester, handler) = Build();
        handler.EnqueueThrow(new HttpRequestException("Connection refused"));

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.False(result.ServerReachable);
        Assert.Contains("Server unreachable", result.Diagnostic);
        Assert.Contains("Connection refused", result.Diagnostic);
        Assert.Single(handler.RequestUrls); // only the /models attempt, nothing after
    }

    [Fact]
    public async Task TestAsync_ModelsEndpointReturnsErrorStatus_StillTreatedAsReachable_ProceedsToChatTest()
    {
        // A non-success status from /models is NOT the same as a network-level failure — some
        // OpenAI-compatible servers simply don't implement /models. That must not be conflated
        // with "server unreachable".
        var (tester, handler) = Build();
        handler.EnqueueJson("""{"error":"not found"}""", HttpStatusCode.NotFound);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.True(result.ServerReachable);
        Assert.True(result.ModelAvailable); // can't verify against an unknown model list -> assumed available
        Assert.Null(result.AvailableModels);
        Assert.True(result.ChatValid);
    }

    [Fact]
    public async Task TestAsync_ChatEndpointReturnsErrorStatus_ReportsChatTestFailed_WithoutTryingToolCall()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson("""{"error":"Unexpected endpoint or method."}""", HttpStatusCode.NotFound);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.True(result.ServerReachable);
        Assert.False(result.ChatValid);
        Assert.StartsWith("Chat test failed:", result.Diagnostic);
        Assert.Contains("404", result.Diagnostic);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task TestAsync_ModelNotInAdvertisedList_ReportsModelUnavailable_ButStillRunsChatTest()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson("""{"data":[{"id":"some-other-model"}]}""");
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.False(result.ModelAvailable);
        Assert.True(result.ChatValid); // model-availability mismatch doesn't block the rest of the test
    }

    [Fact]
    public async Task TestAsync_ApiKeyRequiredButNotFound_ReturnsImmediately_WithoutAnyHttpCall()
    {
        var (tester, handler) = BuildWithStore(out _);
        var profile = new AgentProfile
        {
            Id = "p1",
            DisplayName = "Cloud",
            Provider = ProviderKind.Mistral,
            BaseUrl = "https://api.mistral.ai/v1",
            Model = "mistral-medium-latest",
            CredentialKey = "SwebKit-Agent:Mistral-ApiKey",
        };

        var result = await tester.TestAsync(profile);

        Assert.False(result.ServerReachable);
        Assert.Contains("API key not found", result.Diagnostic);
        Assert.Contains("SwebKit-Agent:Mistral-ApiKey", result.Diagnostic!);
        Assert.Empty(handler.RequestUrls);
    }

    [Fact]
    public async Task TestAsync_ModelsResponseAdvertisesContextLength_PopulatesDetectedContextWindowTokens()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson("""{"data":[{"id":"test-model","context_length":8192}]}""");
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.Equal(8192, result.DetectedContextWindowTokens);
    }

    [Fact]
    public async Task TestAsync_ModelsResponseAdvertisesContextLength_OnlyForADifferentModel_DetectsNothing()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson("""{"data":[{"id":"some-other-model","context_length":8192}]}""");
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.Null(result.DetectedContextWindowTokens);
    }

    [Fact]
    public async Task TestAsync_ModelsResponseHasNoContextLengthField_DetectsNothing_NotAnException()
    {
        var (tester, handler) = Build();
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);

        var result = await tester.TestAsync(LmStudioProfile());

        Assert.Null(result.DetectedContextWindowTokens);
    }

    [Fact]
    public async Task TestAsync_ApiKeyResolved_SendsBearerAuthorizationHeaderOnEveryRequest()
    {
        var (tester, handler) = BuildWithStore(out var store);
        store.Set("SwebKit-Agent:Mistral-ApiKey", "secret-value");
        handler.EnqueueJson(ModelsJson);
        handler.EnqueueJson(ChatOkJson);
        handler.EnqueueJson(ToolCallSupportedJson);
        var profile = new AgentProfile
        {
            Id = "p1",
            DisplayName = "Cloud",
            Provider = ProviderKind.Mistral,
            BaseUrl = "https://api.mistral.ai/v1",
            Model = "test-model",
            CredentialKey = "SwebKit-Agent:Mistral-ApiKey",
        };

        var result = await tester.TestAsync(profile);

        Assert.True(result.ServerReachable);
        Assert.Equal(3, handler.RequestUrls.Count);
        Assert.All(handler.RequestAuthorizationHeaders, h => Assert.Equal("Bearer secret-value", h));
    }
}
