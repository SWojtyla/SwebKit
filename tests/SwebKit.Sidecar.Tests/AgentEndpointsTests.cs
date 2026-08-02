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
}
