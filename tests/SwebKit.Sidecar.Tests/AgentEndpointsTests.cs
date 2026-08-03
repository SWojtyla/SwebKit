using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

public class AgentEndpointsTests
{
    private static SidecarAgentChatService CreateService(IAgentModelClient? modelClient = null)
    {
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Test Profile",
            Capability = AgentCapability.ChatOnly,
        });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();

        return new SidecarAgentChatService(modelClient ?? new FakeAgentModelClient(), registry, profiles, settings, demo);
    }

    // ── Chat ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_EmptyMessage_ReturnsBadRequest_AndNeverCallsModelClient()
    {
        var modelClient = new FakeAgentModelClient();
        var service = CreateService(modelClient);
        var req = new AgentChatRequest { Message = "   " };

        var result = await AgentEndpoints.ChatAsync(service, req, CancellationToken.None);

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, status.StatusCode);
        Assert.Null(modelClient.LastRequest);
    }

    [Fact]
    public async Task ChatAsync_ValidMessage_ReturnsReplyFromService()
    {
        var service = CreateService();
        var req = new AgentChatRequest { Message = "hello there" };

        var result = await AgentEndpoints.ChatAsync(service, req, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<SidecarAgentReply>>(result);
        Assert.Equal("canned reply", ok.Value!.Text);
        Assert.Equal("done", ok.Value.Status);
        Assert.False(ok.Value.Error);
    }

    // ── Streaming chat (SSE) ─────────────────────────────────────────────────

    [Fact]
    public async Task ChatStreamAsync_EmptyMessage_Returns400_AndNeverCallsModelClient()
    {
        var modelClient = new FakeAgentModelClient();
        var service = CreateService(modelClient);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var req = new AgentChatRequest { Message = "   " };

        await AgentEndpoints.ChatStreamAsync(httpContext, service, req, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Null(modelClient.LastRequest);
    }

    [Fact]
    public async Task ChatStreamAsync_ValidMessage_WritesEventStreamHeaders_AndOneSseLinePerEvent()
    {
        // Reuses SidecarAgentChatServiceStreamingTests' ScriptedStreamingModelClient rather than the
        // canned single-shot FakeAgentModelClient, so this exercises real multi-event forwarding.
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = AgentCapability.ChatOnly });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var service = new SidecarAgentChatService(
            new ScriptedStreamingModelClient(() => ScriptedStreamingModelClient.TokensThenDone("hi")),
            registry, profiles, settings, new DemoModeService());

        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        var req = new AgentChatRequest { Message = "hello" };

        await AgentEndpoints.ChatStreamAsync(httpContext, service, req, CancellationToken.None);

        Assert.Equal("text/event-stream", httpContext.Response.ContentType);

        body.Position = 0;
        var text = Encoding.UTF8.GetString(body.ToArray());
        var dataLines = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => chunk.Trim())
            .Where(chunk => chunk.StartsWith("data:", StringComparison.Ordinal))
            .Select(chunk => chunk["data:".Length..].Trim())
            .ToList();

        Assert.Equal(3, dataLines.Count); // 2 tokens ("h", "i") + 1 done
        using var first = JsonDocument.Parse(dataLines[0]);
        // Enum kinds must serialize as the camelCase wire strings the frontend's AgentStreamEvent
        // union expects (e.g. "token"), never as raw integers — this is what a missing
        // JsonStringEnumConverter on the manual serializer would silently get wrong.
        Assert.Equal("token", first.RootElement.GetProperty("kind").GetString());
        Assert.Equal("h", first.RootElement.GetProperty("token").GetString());

        using var last = JsonDocument.Parse(dataLines[2]);
        Assert.Equal("done", last.RootElement.GetProperty("kind").GetString());
        Assert.Equal("hi", last.RootElement.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ChatStreamAsync_DoneEvent_CarriesStepsAndContextUsagePercent_NotJustText()
    {
        // workspace-intelligence Module 5/6: SidecarAgentChatService.SendStreamAsync re-yields an
        // enriched Done event with session-level fields the low-level provider client's own event
        // never carries — this is the wire-serialization side of that, so a missing property
        // mapping in ToWireEvent can't silently drop them again.
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = AgentCapability.ChatOnly });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var service = new SidecarAgentChatService(
            new ScriptedStreamingModelClient(() => ScriptedStreamingModelClient.TokensThenDone("hi")),
            registry, profiles, settings, new DemoModeService());

        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        var req = new AgentChatRequest { Message = "hello" };

        await AgentEndpoints.ChatStreamAsync(httpContext, service, req, CancellationToken.None);

        body.Position = 0;
        var text = Encoding.UTF8.GetString(body.ToArray());
        var lastLine = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => chunk.Trim())
            .Last(chunk => chunk.StartsWith("data:", StringComparison.Ordinal))["data:".Length..].Trim();

        using var doc = JsonDocument.Parse(lastLine);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.TryGetProperty("steps", out _));
        Assert.True(result.TryGetProperty("contextUsagePercent", out var usage));
        Assert.True(usage.GetDouble() >= 0);
        Assert.True(result.TryGetProperty("summarized", out var summarized));
        Assert.False(summarized.GetBoolean());
    }

    // ── Per-session isolation ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_DifferentSessionIds_HaveIndependentHistory()
    {
        var service = CreateService();

        await service.SendAsync("session-a", "hello from a", ct: CancellationToken.None);
        await service.SendAsync("session-b", "hello from b", ct: CancellationToken.None);

        Assert.Equal(2, service.GetHistoryCount("session-a")); // user + assistant
        Assert.Equal(2, service.GetHistoryCount("session-b"));

        service.ClearHistory("session-a");

        Assert.Equal(0, service.GetHistoryCount("session-a"));
        Assert.Equal(2, service.GetHistoryCount("session-b"));
    }

    [Fact]
    public async Task SendAsync_OmittedSessionId_UsesTheSameGlobalSessionAsBeforePerSessionSupport()
    {
        var service = CreateService();

        // The overload with no sessionId (pre-Module-2 call shape) and an explicit null both must
        // land in the same global session, since AgentPage.tsx keeps calling it exactly like this.
        await service.SendAsync("first message");
        await service.SendAsync(null, "second message", ct: CancellationToken.None);

        Assert.Equal(4, service.HistoryCount); // 2 user + 2 assistant, one shared session
        Assert.Equal(service.HistoryCount, service.GetHistoryCount(null));
    }

    // ── Clear history ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearHistory_ResetsHistoryCountToZero()
    {
        var service = CreateService();
        await service.SendAsync("hello");
        Assert.True(service.HistoryCount > 0);

        var result = AgentEndpoints.ClearHistory(service);

        Assert.Equal(0, service.HistoryCount);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"cleared\":true", json);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReflectsCurrentHistoryCount()
    {
        var service = CreateService();
        await service.SendAsync("hello");

        var result = AgentEndpoints.GetStatus(service);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains($"\"historyCount\":{service.HistoryCount}", json);
    }

    [Fact]
    public async Task GetStatus_IncludesEstimatedTokens()
    {
        var service = CreateService();
        await service.SendAsync("hello");

        var result = AgentEndpoints.GetStatus(service);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains($"\"estimatedTokens\":{service.GetEstimatedTokens(null)}", json);
    }

    [Fact]
    public async Task GetStatus_IncludesContextUsagePercent()
    {
        var service = CreateService();
        await service.SendAsync("hello");

        var result = AgentEndpoints.GetStatus(service);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"contextUsagePercent\":", json);
    }

    [Fact]
    public async Task GetStatus_IncludesContextUsageWarningPercent()
    {
        var service = CreateService();
        await service.SendAsync("hello");

        var result = AgentEndpoints.GetStatus(service);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"contextUsageWarningPercent\":", json);
    }

    [Fact]
    public void GetEstimatedTokens_NoSession_ReturnsZero()
    {
        var service = CreateService();

        Assert.Equal(0, service.GetEstimatedTokens("never-used-session"));
    }

    [Fact]
    public async Task GetEstimatedTokens_UsesRoughlyFourCharactersPerToken()
    {
        var service = CreateService();
        // FakeAgentModelClient always replies "canned reply" (13 chars); the user message here is
        // "hello" (5 chars) — 18 total chars, ceil(18/4) = 5. Not exact tokenization (that needs a
        // per-model tokenizer this app deliberately doesn't carry), just the standard coarse
        // ~4-chars-per-token heuristic, verified against a known input/output pair here so a future
        // change to the formula doesn't silently drift.
        await service.SendAsync("hello");

        Assert.Equal(5, service.GetEstimatedTokens(null));
    }

    // ── Capability test ──────────────────────────────────────────────────────

    [Fact]
    public async Task TestProfileAsync_UnknownProfileId_ReturnsNotFound()
    {
        var settings = new UserSettingsRepository();
        var handler = new FakeHttpMessageHandler();
        var tester = new AgentCapabilityTester(new HttpClient(handler), new FakeCredentialStore());

        var result = await AgentEndpoints.TestProfileAsync("does-not-exist", null, tester, settings, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task TestProfileAsync_NoBody_FallsBackToPersistedProfile_ReturnsCapabilityResult_AndDoesNotPersistIt()
    {
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Local LM Studio",
            Provider = ProviderKind.LmStudio,
            BaseUrl = "http://localhost:1234/v1",
            Model = "test-model",
        });

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"data":[{"id":"test-model"}]}""");
        handler.EnqueueJson("""{"choices":[{"message":{"content":"OK"}}]}""");
        handler.EnqueueJson("""{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"function":{"name":"echo_test"}}]}}]}""");
        var tester = new AgentCapabilityTester(new HttpClient(handler), new FakeCredentialStore());

        var result = await AgentEndpoints.TestProfileAsync("p1", null, tester, settings, CancellationToken.None);

        var ok = Assert.IsType<Ok<CapabilityTestResult>>(result);
        Assert.Equal(AgentCapability.ToolCalling, ok.Value!.Capability);
        Assert.True(ok.Value.ToolCallingValid);
        // Stateless by design (see AgentEndpoints.TestProfileAsync's doc comment): the frontend
        // patches the result into its own state and saves via the existing user-settings endpoint
        // rather than this one owning persistence.
        Assert.Equal(AgentCapability.Unknown, settings.Settings.Agent.Profiles[0].Capability);
    }

    [Fact]
    public async Task TestProfileAsync_ProfileInRequestBody_TestsItDirectly_EvenIfNeverPersisted()
    {
        var settings = new UserSettingsRepository(); // deliberately no profiles saved at all
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"data":[{"id":"test-model"}]}""");
        handler.EnqueueJson("""{"choices":[{"message":{"content":"OK"}}]}""");
        handler.EnqueueJson("""{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"function":{"name":"echo_test"}}]}}]}""");
        var tester = new AgentCapabilityTester(new HttpClient(handler), new FakeCredentialStore());
        var unsavedProfile = new AgentProfile
        {
            Id = "unsaved",
            DisplayName = "Not yet saved",
            Provider = ProviderKind.LmStudio,
            BaseUrl = "http://localhost:1234/v1",
            Model = "test-model",
        };

        var result = await AgentEndpoints.TestProfileAsync("unsaved", unsavedProfile, tester, settings, CancellationToken.None);

        var ok = Assert.IsType<Ok<CapabilityTestResult>>(result);
        Assert.Equal(AgentCapability.ToolCalling, ok.Value!.Capability);
    }

    [Fact]
    public async Task TestProfileAsync_ProfileInRequestBody_TakesPrecedenceOverAStalePersistedCopy()
    {
        // Regression coverage for the race this signature was changed to close: the settings form
        // saves on every keystroke via a fire-and-forget PUT the UI never awaits, so clicking
        // "Test connection" right after an edit could previously test whatever was last persisted
        // instead of what's actually on screen. Confirms the endpoint honors the body over a
        // same-id lookup, verified by inspecting the actual outgoing request URL — not just that
        // the result looks right, which a coincidentally-matching canned response could mask.
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Stale",
            Provider = ProviderKind.LmStudio,
            BaseUrl = "http://stale-not-yet-overwritten.example:9999/v1",
            Model = "stale-model",
        });
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"data":[{"id":"test-model"}]}""");
        handler.EnqueueJson("""{"choices":[{"message":{"content":"OK"}}]}""");
        handler.EnqueueJson("""{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"function":{"name":"echo_test"}}]}}]}""");
        var tester = new AgentCapabilityTester(new HttpClient(handler), new FakeCredentialStore());
        var justTypedProfile = new AgentProfile
        {
            Id = "p1",
            DisplayName = "Just typed",
            Provider = ProviderKind.LmStudio,
            BaseUrl = "http://localhost:1234/v1",
            Model = "test-model",
        };

        var result = await AgentEndpoints.TestProfileAsync("p1", justTypedProfile, tester, settings, CancellationToken.None);

        Assert.All(handler.Requests, r => Assert.StartsWith("http://localhost:1234/v1/", r.RequestUri!.ToString()));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.ToString().Contains("stale-not-yet-overwritten"));
        var ok = Assert.IsType<Ok<CapabilityTestResult>>(result);
        Assert.Equal(AgentCapability.ToolCalling, ok.Value!.Capability);
    }
}
