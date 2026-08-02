using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ToolMetadataTests
{
    [Fact]
    public void ToolDefinition_Defaults_ToReadAndNone()
    {
        var def = new ToolDefinition
        {
            Name = "test",
            Description = "test tool",
            ParametersSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
        };

        Assert.Equal(ToolKind.Read, def.Kind);
        Assert.Equal(ToolRisk.None, def.Risk);
        Assert.Equal(AgentCapability.ToolCalling, def.RequiredCapability);
    }

    [Fact]
    public void ToolDefinition_CanSetMutationMetadata()
    {
        var def = new ToolDefinition
        {
            Name = "delete_thing",
            Description = "Deletes a thing",
            ParametersSchema = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            Kind = ToolKind.Mutate,
            Risk = ToolRisk.High,
            RequiredCapability = AgentCapability.ToolCalling
        };

        Assert.Equal(ToolKind.Mutate, def.Kind);
        Assert.Equal(ToolRisk.High, def.Risk);
    }

    [Fact]
    public void IAgentTool_DefaultKind_IsRead()
    {
        // Verify default interface implementation
        IAgentTool tool = new TestTool();
        Assert.Equal(ToolKind.Read, tool.Kind);
        Assert.Equal(ToolRisk.None, tool.Risk);
        Assert.Equal(AgentCapability.ToolCalling, tool.RequiredCapability);
    }

    [Fact]
    public void IAgentTool_CanOverrideKind()
    {
        IAgentTool tool = new MutatingTestTool();
        Assert.Equal(ToolKind.Mutate, tool.Kind);
        Assert.Equal(ToolRisk.High, tool.Risk);
    }

    private sealed class TestTool : IAgentTool
    {
        public string Name => "test";
        public string Description => "Test tool";
        public FeatureArea FeatureArea => FeatureArea.Aks;
        public System.Text.Json.JsonElement ParametersSchema =>
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        public Task<string> ExecuteAsync(System.Text.Json.JsonElement arguments, CancellationToken ct) =>
            Task.FromResult("{\"ok\":true}");
    }

    private sealed class MutatingTestTool : IAgentTool
    {
        public string Name => "delete";
        public string Description => "Delete tool";
        public FeatureArea FeatureArea => FeatureArea.Aks;
        public System.Text.Json.JsonElement ParametersSchema =>
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        public ToolKind Kind => ToolKind.Mutate;
        public ToolRisk Risk => ToolRisk.High;
        public Task<string> ExecuteAsync(System.Text.Json.JsonElement arguments, CancellationToken ct) =>
            Task.FromResult("{\"deleted\":true}");
    }
}
