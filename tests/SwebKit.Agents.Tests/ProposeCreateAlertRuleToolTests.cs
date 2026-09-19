using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.Monitoring;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ProposeCreateAlertRuleToolTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_ValidAksRule_RegistersPendingAction_NotCreated()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCreateAlertRuleTool(coordinator);

        var result = await tool.ExecuteAsync(Args("""
            {"name":"prod pods","source":"AksPodHealth","severity":"Critical","aks_namespace":"prod","ai_investigation_enabled":false}
            """), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());

        var pending = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.CreateAlertRule, pending.Type);
        Assert.Equal(AgentActionRisk.Low, pending.Risk);
        Assert.Contains("prod pods", pending.Summary);
        // The flat payload travels verbatim — the executor maps it onto the param bags.
        Assert.Equal("prod", pending.Payload!.Value.GetProperty("aks_namespace").GetString());
        Assert.False(pending.Payload.Value.GetProperty("ai_investigation_enabled").GetBoolean());
    }

    [Fact]
    public async Task Execute_MissingName_ReturnsError()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCreateAlertRuleTool(coordinator);

        var result = await tool.ExecuteAsync(Args("""{"source":"AksPodHealth","aks_namespace":"prod"}"""), CancellationToken.None);

        Assert.Contains("name", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public async Task Execute_InvalidSource_ReturnsError()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCreateAlertRuleTool(coordinator);

        var result = await tool.ExecuteAsync(Args("""{"name":"x","source":"NotASource"}"""), CancellationToken.None);

        Assert.Contains("source", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Theory]
    [InlineData("AksPodHealth", "aks_namespace")]
    [InlineData("AksPodRestartRate", "aks_namespace")]
    [InlineData("ServiceBusDlqDepth", "servicebus_entity_path")]
    [InlineData("StorageBlobCount", "storage_container_name")]
    public async Task Execute_MissingSourceRequiredParam_ReturnsErrorNamingIt(string source, string expectedParam)
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCreateAlertRuleTool(coordinator);

        var result = await tool.ExecuteAsync(Args($$"""{"name":"x","source":"{{source}}"}"""), CancellationToken.None);

        Assert.Contains(expectedParam, result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public void Metadata_IsMutate_LowRisk_MonitoringArea()
    {
        var tool = new ProposeCreateAlertRuleTool(new AgentActionCoordinator());
        Assert.Equal("propose_create_alert_rule", tool.Name);
        Assert.Equal(ToolKind.Mutate, tool.Kind);
        Assert.Equal(ToolRisk.Low, tool.Risk);
        Assert.Equal(FeatureArea.Monitoring, tool.FeatureArea);
    }
}
