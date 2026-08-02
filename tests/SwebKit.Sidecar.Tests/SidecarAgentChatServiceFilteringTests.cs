using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Read-only test tool tagged to a specific area, so filtering tests can assert on which
/// of a mixed set survives each gate.</summary>
internal sealed class FakeReadTool(string name, FeatureArea area) : IAgentTool
{
    public string Name => name;
    public string Description => "fake read tool";
    public FeatureArea FeatureArea => area;
    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""{"type":"object","properties":{}}""");
    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct) => Task.FromResult("{}");
}

/// <summary>Mutating test tool tagged to a specific area.</summary>
internal sealed class FakeMutateTool(string name, FeatureArea area) : IAgentTool
{
    public string Name => name;
    public string Description => "fake mutate tool";
    public FeatureArea FeatureArea => area;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;
    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""{"type":"object","properties":{}}""");
    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct) => Task.FromResult("{}");
}

public class SidecarAgentChatServiceFilteringTests
{
    private static (SidecarAgentChatService Service, FakeAgentModelClient ModelClient) CreateService(AgentCapability capability)
    {
        var registry = new AgentToolRegistry([
            new FakeReadTool("read_aks", FeatureArea.Aks),
            new FakeMutateTool("mutate_aks", FeatureArea.Aks),
            new FakeReadTool("read_redis", FeatureArea.Redis),
        ]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile { Id = "p1", DisplayName = "Test", Capability = capability });
        settings.Settings.Agent.ActiveProfileId = "p1";
        var demo = new DemoModeService();
        var modelClient = new FakeAgentModelClient();

        return (new SidecarAgentChatService(modelClient, registry, profiles, settings, demo), modelClient);
    }

    private static List<string> ToolNames(FakeAgentModelClient client) =>
        client.LastRequest!.Tools.Select(t => t.Name).OrderBy(n => n).ToList();

    [Fact]
    public async Task ChatOnlyCapability_SendsNoTools_RegardlessOfModeOrContext()
    {
        var (service, model) = CreateService(AgentCapability.ChatOnly);

        await service.SendAsync(null, "hi", context: new AgentChatContext { FeatureArea = "Aks" }, mode: "ask_and_do");

        Assert.Empty(model.LastRequest!.Tools);
    }

    [Fact]
    public async Task AskMode_Omitted_KeepsOnlyReadTools_FromEveryArea_WhenNoContext()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        // No mode, no context — the pre-Module-5 global-page call shape.
        await service.SendAsync(null, "hi");

        Assert.Equal(["read_aks", "read_redis"], ToolNames(model));
    }

    [Fact]
    public async Task AskAndDoMode_NoContext_KeepsEveryToolFromEveryArea()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: null, mode: "ask_and_do");

        Assert.Equal(["mutate_aks", "read_aks", "read_redis"], ToolNames(model));
    }

    [Fact]
    public async Task AskAndDoMode_WithAksContext_KeepsOnlyAksTools_MutateIncluded()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: new AgentChatContext { FeatureArea = "Aks" }, mode: "ask_and_do");

        Assert.Equal(["mutate_aks", "read_aks"], ToolNames(model));
    }

    [Fact]
    public async Task AskMode_WithAksContext_KeepsOnlyReadAksTool()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: new AgentChatContext { FeatureArea = "Aks" }, mode: "ask");

        Assert.Equal(["read_aks"], ToolNames(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-mode")]
    public async Task UnrecognizedOrMissingMode_DefaultsToAsk_NeverGrantsMutateTools(string? mode)
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: null, mode: mode);

        Assert.DoesNotContain("mutate_aks", ToolNames(model));
    }

    [Fact]
    public async Task UnrecognizedFeatureAreaName_IsIgnored_NotTreatedAsAnEmptySet()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: new AgentChatContext { FeatureArea = "NotARealArea" }, mode: "ask_and_do");

        // An unparseable area name falls through to "no area filter" rather than silently
        // matching nothing — same principle as the mode gate: fail safe, not fail closed-to-empty
        // in a way that looks like a bug rather than a deliberate restriction.
        Assert.Equal(["mutate_aks", "read_aks", "read_redis"], ToolNames(model));
    }

    [Fact]
    public async Task Context_AddsCurrentFocusSection_ToTheSystemPrompt()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi", context: new AgentChatContext
        {
            FeatureArea = "Aks",
            Selection = new Dictionary<string, string> { ["namespace"] = "prod", ["pod"] = "api-7c9f" },
        }, mode: "ask");

        var prompt = model.LastRequest!.SystemPrompt;
        Assert.Contains("## Current focus", prompt);
        Assert.Contains("Area: Aks", prompt);
        Assert.Contains("namespace: prod", prompt);
        Assert.Contains("pod: api-7c9f", prompt);
    }

    [Fact]
    public async Task NoContext_OmitsCurrentFocusSection_FromTheSystemPrompt()
    {
        var (service, model) = CreateService(AgentCapability.ToolCalling);

        await service.SendAsync(null, "hi");

        Assert.DoesNotContain("## Current focus", model.LastRequest!.SystemPrompt);
    }
}
