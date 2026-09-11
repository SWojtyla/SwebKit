using SwebKit.Core.Configuration;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the system-prompt seam extracted out of <see cref="SidecarAgentChatService"/>:
/// the tool-policy section each capability/mode combination gets, and the additive "current focus"
/// block a contextual panel adds without replacing the coarse workspace summary.</summary>
public class AgentSystemPromptBuilderTests
{
    private static AgentSystemPromptBuilder CreateBuilder() =>
        new(new ProfileRepository(), new DemoModeService());

    [Fact]
    public void Build_NoToolCallingCapability_SaysToolsAreUnavailable()
    {
        var prompt = CreateBuilder().Build(context: null, "ask_and_do", hasToolCalling: false);

        Assert.Contains("Tool calling is not available with the current model.", prompt);
        Assert.DoesNotContain("Tool policy (Ask & do mode)", prompt);
    }

    [Fact]
    public void Build_AskMode_UsesTheReadOnlyToolPolicy()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", hasToolCalling: true);

        Assert.Contains("## Tool policy (Ask mode)", prompt);
        Assert.Contains("no mutating tools available in this mode", prompt);
    }

    [Fact]
    public void Build_AskAndDoMode_UsesThePropseOnlyMutationPolicy()
    {
        var prompt = CreateBuilder().Build(context: null, "ask_and_do", hasToolCalling: true);

        Assert.Contains("## Tool policy (Ask & do mode)", prompt);
        Assert.Contains("never changes anything by itself", prompt);
    }

    [Fact]
    public void Build_NoContext_OmitsTheCurrentFocusSection()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", hasToolCalling: true);

        Assert.DoesNotContain("## Current focus", prompt);
        Assert.Contains("## Current workspace context", prompt);
    }

    [Fact]
    public void Build_ContextWithSelection_AddsCurrentFocusWithoutReplacingTheWorkspaceSummary()
    {
        var context = new AgentChatContext
        {
            FeatureArea = "Aks",
            Selection = new Dictionary<string, string> { ["namespace"] = "prod", ["pod"] = "api-7c9f" },
        };

        var prompt = CreateBuilder().Build(context, "ask", hasToolCalling: true);

        Assert.Contains("## Current focus", prompt);
        Assert.Contains("Area: Aks", prompt);
        Assert.Contains("namespace: prod", prompt);
        Assert.Contains("pod: api-7c9f", prompt);
        Assert.Contains("## Current workspace context", prompt);
        // The focus block precedes the coarse summary it supplements.
        Assert.True(prompt.IndexOf("## Current focus", StringComparison.Ordinal)
            < prompt.IndexOf("## Current workspace context", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ContextWithNoFeatureArea_OmitsTheCurrentFocusSection()
    {
        var context = new AgentChatContext { Selection = new Dictionary<string, string> { ["pod"] = "api" } };

        var prompt = CreateBuilder().Build(context, "ask", hasToolCalling: true);

        Assert.DoesNotContain("## Current focus", prompt);
    }

    [Fact]
    public void Build_UnconfiguredWorkspace_StillReportsKubernetesAsNotConfigured()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", hasToolCalling: true);

        Assert.Contains("Kubernetes: (not configured)", prompt);
    }
}
