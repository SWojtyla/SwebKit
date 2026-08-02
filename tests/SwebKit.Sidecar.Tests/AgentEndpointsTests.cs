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

    // ── Capability test ──────────────────────────────────────────────────────

    [Fact]
    public async Task TestProfileAsync_UnknownProfileId_ReturnsNotFound()
    {
        var settings = new UserSettingsRepository();
        var handler = new FakeHttpMessageHandler();
        var tester = new AgentCapabilityTester(new HttpClient(handler), new FakeCredentialStore());

        var result = await AgentEndpoints.TestProfileAsync("does-not-exist", tester, settings, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task TestProfileAsync_KnownProfile_ReturnsCapabilityResult_AndDoesNotPersistIt()
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

        var result = await AgentEndpoints.TestProfileAsync("p1", tester, settings, CancellationToken.None);

        var ok = Assert.IsType<Ok<CapabilityTestResult>>(result);
        Assert.Equal(AgentCapability.ToolCalling, ok.Value!.Capability);
        Assert.True(ok.Value.ToolCallingValid);
        // Stateless by design (see AgentEndpoints.TestProfileAsync's doc comment): the frontend
        // patches the result into its own state and saves via the existing user-settings endpoint
        // rather than this one owning persistence.
        Assert.Equal(AgentCapability.Unknown, settings.Settings.Agent.Profiles[0].Capability);
    }
}
