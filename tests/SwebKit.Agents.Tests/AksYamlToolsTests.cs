using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.Aks;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class AksYamlToolsTests
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static (Mock<IAksClientFactory> Factory, Mock<IAksClient> Client, AppStateService State) Build()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        var state = TestSupport.CreateAppState(config => config.AksConfig = new AksConfig
        {
            DefaultNamespace = "configured",
        });
        return (factory, client, state);
    }

    [Fact]
    public async Task GetResourceYaml_UsesExplicitNamespaceAndReturnsYaml()
    {
        var (factory, client, state) = Build();
        client.Setup(c => c.GetResourceYamlAsync("team-a", "Deployment", "orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync("kind: Deployment");
        var tool = new GetAksResourceYamlTool(factory.Object, new DemoAksClient(), state);

        var result = await tool.ExecuteAsync(Args(new { kind = "Deployment", name = "orders", @namespace = "team-a" }), CancellationToken.None);

        var json = JsonDocument.Parse(result).RootElement;
        Assert.Equal("team-a", json.GetProperty("namespace_name").GetString());
        Assert.Contains("kind: Deployment", json.GetProperty("yaml").GetString());
    }

    [Fact]
    public async Task GetResourceYaml_UsesAmbientThenConfiguredNamespace()
    {
        var (factory, client, state) = Build();
        client.Setup(c => c.GetResourceYamlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("yaml");
        var tool = new GetAksResourceYamlTool(factory.Object, new DemoAksClient(), state);

        using (AgentExecutionContext.Push(new Dictionary<string, string> { ["namespace"] = "selected" }))
            await tool.ExecuteAsync(Args(new { kind = "Pod", name = "worker" }), CancellationToken.None);
        await tool.ExecuteAsync(Args(new { kind = "Pod", name = "worker" }), CancellationToken.None);

        client.Verify(c => c.GetResourceYamlAsync("selected", "Pod", "worker", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetResourceYamlAsync("configured", "Pod", "worker", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetResourceYaml_TruncatesOversizedManifest()
    {
        var (factory, client, state) = Build();
        client.Setup(c => c.GetResourceYamlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new string('x', 9000));
        var tool = new GetAksResourceYamlTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(Args(new { kind = "Pod", name = "worker" }), CancellationToken.None)).RootElement;

        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Contains("Truncated by SwebKit", result.GetProperty("yaml").GetString());
    }

    [Fact]
    public async Task ProposeApplyYaml_ValidatesThenRegistersHighRiskAction()
    {
        var (factory, client, state) = Build();
        client.Setup(c => c.ValidateResourceYamlAsync("team-a", "kind: Deployment", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeApplyAksYamlTool(factory.Object, new DemoAksClient(), state, coordinator);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { kind = "Deployment", name = "orders", @namespace = "team-a", yaml = "kind: Deployment" }),
            CancellationToken.None)).RootElement;

        Assert.Equal("pending_confirmation", result.GetProperty("status").GetString());
        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.ApplyAksYaml, action.Type);
        Assert.Equal(AgentActionRisk.High, action.Risk);
        Assert.Equal("team-a", action.Payload!.Value.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task ProposeApplyYaml_ValidationFailureDoesNotRegisterAction()
    {
        var (factory, client, state) = Build();
        client.Setup(c => c.ValidateResourceYamlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid replicas");
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeApplyAksYamlTool(factory.Object, new DemoAksClient(), state, coordinator);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { kind = "Deployment", name = "orders", yaml = "bad" }), CancellationToken.None)).RootElement;

        Assert.Contains("invalid replicas", result.GetProperty("error").GetString());
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public async Task AksActionExecutor_AppliesExactPayload()
    {
        var (factory, client, state) = Build();
        var executor = new AksActionExecutor(factory.Object, new DemoAksClient(), state);
        var action = new PendingAgentAction
        {
            Id = "apply-1",
            Type = AgentActionType.ApplyAksYaml,
            Summary = "apply",
            Target = "team-a/Deployment/orders",
            Risk = AgentActionRisk.High,
            Preview = "yaml",
            ExpectedFingerprint = null,
            Payload = Args(new { kind = "Deployment", name = "orders", @namespace = "team-a", yaml = "kind: Deployment" }),
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.ApplyResourceYamlAsync("team-a", "Deployment", "orders", "kind: Deployment", It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(executor.CanHandle(AgentActionType.ApplyAksYaml));
        Assert.False(executor.CanHandle(AgentActionType.CopyBlob));
    }
}
