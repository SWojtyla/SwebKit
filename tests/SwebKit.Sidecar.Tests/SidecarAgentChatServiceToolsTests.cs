using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Records the <see cref="AgentModelRequest"/> it was called with and returns a canned reply,
/// so tests can assert on what tools/history the chat service actually sent without a real LLM.
/// </summary>
internal sealed class FakeAgentModelClient : IAgentModelClient
{
    public AgentModelRequest? LastRequest { get; private set; }
    public Func<string, JsonElement, CancellationToken, Task<string>>? LastToolExecutor { get; private set; }

    public Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        LastRequest = request;
        LastToolExecutor = toolExecutor;
        return Task.FromResult(new AgentChatResult { Text = "canned reply", ToolsUsed = [], Elapsed = TimeSpan.Zero });
    }

    public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
        throw new NotSupportedException();
}

internal sealed class FakeAgentTool : IAgentTool
{
    public string Name => "fake_tool";
    public string Description => "A fake tool for testing.";
    public FeatureArea FeatureArea => FeatureArea.Aks;
    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""{"type":"object","properties":{}}""");

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
        Task.FromResult("{}");
}

public class SidecarAgentChatServiceToolsTests
{
    private static SidecarAgentChatService CreateService(
        IAgentModelClient modelClient,
        AgentCapability capability,
        out UserSettingsRepository settings)
    {
        var registry = new AgentToolRegistry([new FakeAgentTool()]);
        var profiles = new ProfileRepository();
        settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Test Profile",
            Capability = capability,
        });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();

        return new SidecarAgentChatService(modelClient, registry, profiles, settings, demo);
    }

    [Fact]
    public async Task SendAsync_WithToolCallingProfile_PassesToolsAndExecutorToModelClient()
    {
        var modelClient = new FakeAgentModelClient();
        var service = CreateService(modelClient, AgentCapability.ToolCalling, out _);

        await service.SendAsync("hello");

        Assert.NotNull(modelClient.LastRequest);
        Assert.Single(modelClient.LastRequest!.Tools);
        Assert.Equal("fake_tool", modelClient.LastRequest.Tools[0].Name);
        Assert.NotNull(modelClient.LastToolExecutor);
    }

    [Theory]
    [InlineData(AgentCapability.ChatOnly)]
    [InlineData(AgentCapability.Unknown)]
    public async Task SendAsync_WithoutToolCallingCapability_SendsNoToolsAndNoExecutor(AgentCapability capability)
    {
        var modelClient = new FakeAgentModelClient();
        var service = CreateService(modelClient, capability, out _);

        await service.SendAsync("hello");

        Assert.NotNull(modelClient.LastRequest);
        Assert.Empty(modelClient.LastRequest!.Tools);
        Assert.Null(modelClient.LastToolExecutor);
    }

    [Fact]
    public async Task SendAsync_ReturnsToolsUsedFromModelResult()
    {
        var modelClient = new RecordingToolsUsedModelClient(["fake_tool"]);
        var service = CreateService(modelClient, AgentCapability.ToolCalling, out _);

        var reply = await service.SendAsync("hello");

        Assert.Equal(["fake_tool"], reply.ToolsUsed);
    }

    private sealed class RecordingToolsUsedModelClient(IReadOnlyList<string> toolsUsed) : IAgentModelClient
    {
        public Task<AgentChatResult> ChatAsync(
            AgentModelRequest request,
            Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
            CancellationToken ct) =>
            Task.FromResult(new AgentChatResult { Text = "done", ToolsUsed = toolsUsed, Elapsed = TimeSpan.Zero });

        public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
