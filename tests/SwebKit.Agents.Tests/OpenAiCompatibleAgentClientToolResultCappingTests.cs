using System.Text.Json;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Covers workspace-intelligence Module 5's tool-result capping — a single oversized tool
/// result (e.g. GetPodLogsTool) must never blow the context budget within one turn, before
/// SidecarAgentChatService's history-level rolling summarization ever gets a chance to run.</summary>
public class OpenAiCompatibleAgentClientToolResultCappingTests
{
    [Fact]
    public void CapToolResult_UnderTheCap_ReturnsUnchanged()
    {
        var result = new string('a', 100);

        Assert.Equal(result, OpenAiCompatibleAgentClient.CapToolResult(result));
    }

    [Fact]
    public void CapToolResult_ExactlyAtTheCap_ReturnsUnchanged()
    {
        var result = new string('a', 8_000);

        Assert.Equal(result, OpenAiCompatibleAgentClient.CapToolResult(result));
    }

    [Fact]
    public void CapToolResult_OverTheCap_TruncatesWithAnExplicitMarker_NotSilently()
    {
        var result = new string('a', 8_500);

        var capped = OpenAiCompatibleAgentClient.CapToolResult(result);

        Assert.StartsWith(new string('a', 8_000), capped);
        Assert.Contains("...truncated, 500 more characters available", capped);
        Assert.True(capped.Length > 8_000);
    }

    [Fact]
    public void CapToolResult_NullOrEmpty_ReturnsEmptyString_NotAnException()
    {
        Assert.Equal(string.Empty, OpenAiCompatibleAgentClient.CapToolResult(null));
        Assert.Equal(string.Empty, OpenAiCompatibleAgentClient.CapToolResult(string.Empty));
    }

    [Fact]
    public async Task ChatAsync_ToolReturnsOversizedResult_CapsItBeforeFeedingItBackToTheModel()
    {
        var (client, handler) = Build();
        var oversized = new string('x', 20_000);
        handler.EnqueueJson("""{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"id":"call_1","function":{"name":"get_pod_logs","arguments":"{}"}}]}}]}""");
        handler.EnqueueJson("""{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""");

        await client.ChatAsync(
            Request(),
            (name, args, ct) => Task.FromResult(oversized),
            CancellationToken.None);

        // Round 2's request body carries the tool result from round 1 — it must be capped, not the
        // full 20,000-char blob.
        var round2Body = handler.RequestBodies[1];
        Assert.Contains("...truncated,", round2Body);
        Assert.DoesNotContain(new string('x', 20_000), round2Body);
    }

    private static (OpenAiCompatibleAgentClient Client, FakeStreamingHttpMessageHandler Handler) Build()
    {
        var handler = new FakeStreamingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var settings = new Core.Configuration.UserSettingsRepository();
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
}
