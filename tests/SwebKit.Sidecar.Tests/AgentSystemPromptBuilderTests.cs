using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
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
        var prompt = CreateBuilder().Build(context: null, "ask_and_do", "feature", hasToolCalling: false);

        Assert.Contains("Tool calling is not available with the current model.", prompt);
        Assert.DoesNotContain("Tool policy (Ask & do mode)", prompt);
    }

    [Fact]
    public void Build_AskMode_UsesTheReadOnlyToolPolicy()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Tool policy (Ask mode)", prompt);
        Assert.Contains("no mutating tools available in this mode", prompt);
    }

    [Fact]
    public void Build_AskAndDoMode_UsesThePropseOnlyMutationPolicy()
    {
        var prompt = CreateBuilder().Build(context: null, "ask_and_do", "feature", hasToolCalling: true);

        Assert.Contains("## Tool policy (Ask & do mode)", prompt);
        Assert.Contains("never changes anything by itself", prompt);
    }

    [Fact]
    public void Build_NoContext_OmitsTheCurrentFocusSection()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "feature", hasToolCalling: true);

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

        var prompt = CreateBuilder().Build(context, "ask", "feature", hasToolCalling: true);

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

        var prompt = CreateBuilder().Build(context, "ask", "feature", hasToolCalling: true);

        Assert.DoesNotContain("## Current focus", prompt);
    }

    [Fact]
    public void Build_UnconfiguredWorkspace_StillReportsKubernetesAsNotConfigured()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("Kubernetes: (not configured)", prompt);
    }

    // ── "Other configured areas" fence transparency (agent-correlation Module 2) ──

    private static AgentSystemPromptBuilder BuilderWith(Action<ProfileRepository> configure)
    {
        var profiles = new ProfileRepository();
        configure(profiles);
        return new AgentSystemPromptBuilder(profiles, new DemoModeService());
    }

    [Fact]
    public void Build_FeatureScopeWithContextArea_NamesConfiguredAreasOutsideTheScope()
    {
        var builder = BuilderWith(profiles =>
        {
            profiles.Config.AksConfig = new AksConfig();
            profiles.GetProfileData().ServiceBusNamespaces.Add(new ServiceBusNamespace { Alias = "orders" });
            profiles.Config.StorageAccounts.Add(new StorageConfig { AccountName = "mystorageacct", DisplayName = "My Storage" });
        });
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var prompt = builder.Build(context, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Other configured areas", prompt);
        Assert.Contains("Service Bus (orders)", prompt);
        Assert.Contains("Storage (1 account(s))", prompt);
        Assert.Contains("Search across my whole workspace", prompt);
        // The visible area's own name is not fenced off.
        Assert.DoesNotContain("Kubernetes (", prompt);
    }

    [Fact]
    public void Build_WorkspaceScope_OmitsTheFencedAreasSection()
    {
        var builder = BuilderWith(profiles =>
            profiles.GetProfileData().ServiceBusNamespaces.Add(new ServiceBusNamespace { Alias = "orders" }));
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var prompt = builder.Build(context, "ask", "workspace", hasToolCalling: true);

        Assert.DoesNotContain("## Other configured areas", prompt);
    }

    [Fact]
    public void Build_NoContext_OmitsTheFencedAreasSection()
    {
        var builder = BuilderWith(profiles =>
            profiles.GetProfileData().ServiceBusNamespaces.Add(new ServiceBusNamespace { Alias = "orders" }));

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.DoesNotContain("## Other configured areas", prompt);
    }

    [Fact]
    public void Build_NoToolCalling_OmitsTheFencedAreasSection()
    {
        var builder = BuilderWith(profiles =>
            profiles.GetProfileData().ServiceBusNamespaces.Add(new ServiceBusNamespace { Alias = "orders" }));
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var prompt = builder.Build(context, "ask", "feature", hasToolCalling: false);

        Assert.DoesNotContain("## Other configured areas", prompt);
    }

    [Fact]
    public void Build_FeatureScopeWithNothingElseConfigured_OmitsTheFencedAreasSection()
    {
        var builder = BuilderWith(profiles => profiles.Config.AksConfig = new AksConfig());
        var context = new AgentChatContext { FeatureArea = "Aks" };

        var prompt = builder.Build(context, "ask", "feature", hasToolCalling: true);

        Assert.DoesNotContain("## Other configured areas", prompt);
    }

    // ── Workspace map section (workspace-map-overhaul) ──

    private static WorkspaceResourceNode Node(string id, WorkspaceResourceArea area, string key, string label) =>
        new() { Id = id, Area = area, ResourceKey = key, DisplayLabel = label };

    [Fact]
    public void Build_TopologyWithNodesAndRelationships_RendersTheWorkspaceMapSection()
    {
        var builder = BuilderWith(profiles =>
        {
            profiles.Config.Topology.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            profiles.Config.Topology.Nodes.Add(Node("n2", WorkspaceResourceArea.ServiceBus, "orders.sb.net", "orders"));
            profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship
            {
                Id = "r1",
                FromNodeId = "n1",
                ToNodeId = "n2",
                Label = "consumes",
            });
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Workspace map", prompt);
        Assert.Contains("AKS: api (prod/api)", prompt);
        Assert.Contains("Service Bus: orders (orders.sb.net)", prompt);
        Assert.Contains("api → orders (consumes)", prompt);
        Assert.Contains("declared by the user", prompt);
    }

    [Fact]
    public void Build_EmptyTopology_OmitsTheWorkspaceMapSection()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.DoesNotContain("## Workspace map", prompt);
    }

    [Fact]
    public void Build_NodesWithoutRelationships_RendersResourcesWithNoEdgeLine()
    {
        var builder = BuilderWith(profiles =>
            profiles.Config.Topology.Nodes.Add(Node("n1", WorkspaceResourceArea.Redis, "cache-1", "sessions")));

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Workspace map", prompt);
        Assert.Contains("Redis: sessions (cache-1)", prompt);
        Assert.Contains("(none declared yet)", prompt);
    }

    [Fact]
    public void Build_UnlabeledRelationship_RendersEdgeWithoutTrailingParens()
    {
        var builder = BuilderWith(profiles =>
        {
            profiles.Config.Topology.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            profiles.Config.Topology.Nodes.Add(Node("n2", WorkspaceResourceArea.Storage, "mystorageacct", "blobs"));
            profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship
            {
                Id = "r1",
                FromNodeId = "n1",
                ToNodeId = "n2",
                Label = null,
            });
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("api → blobs", prompt);
        Assert.DoesNotContain("api → blobs (", prompt);
    }

    [Fact]
    public void Build_RelationshipWithMissingEndpoint_IsSkipped()
    {
        var builder = BuilderWith(profiles =>
        {
            profiles.Config.Topology.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship
            {
                Id = "r1",
                FromNodeId = "n1",
                ToNodeId = "ghost",
                Label = "consumes",
            });
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Workspace map", prompt);
        Assert.Contains("(none declared yet)", prompt);
        Assert.DoesNotContain("ghost", prompt);
    }

    [Fact]
    public void Build_MoreNodesThanTheCap_RendersAnOverflowMarkerPerArea()
    {
        var builder = BuilderWith(profiles =>
        {
            for (var i = 0; i < 35; i++)
                profiles.Config.Topology.Nodes.Add(Node($"n{i}", WorkspaceResourceArea.Aks, $"ns/dep{i}", $"dep{i}"));
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("(+5 more)", prompt);
        Assert.DoesNotContain("dep34", prompt);
    }

    [Fact]
    public void Build_MoreRelationshipsThanTheCap_RendersAnOverflowMarker()
    {
        var builder = BuilderWith(profiles =>
        {
            profiles.Config.Topology.Nodes.Add(Node("a", WorkspaceResourceArea.Aks, "ns/api", "api"));
            profiles.Config.Topology.Nodes.Add(Node("b", WorkspaceResourceArea.Redis, "cache", "cache"));
            for (var i = 0; i < 45; i++)
                profiles.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship
                {
                    Id = $"r{i}",
                    FromNodeId = "a",
                    ToNodeId = "b",
                    Label = $"rel{i}",
                });
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("(+5 more)", prompt);
        Assert.DoesNotContain("rel44", prompt);
    }

    [Fact]
    public void Build_NamedMaps_RenderUnderTheirOwnHeaders()
    {
        var builder = BuilderWith(profiles =>
        {
            var payments = new WorkspaceMap { Name = "Payments" };
            payments.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            var shipping = new WorkspaceMap { Name = "Shipping" };
            shipping.Nodes.Add(Node("n2", WorkspaceResourceArea.Redis, "cache-1", "sessions"));
            profiles.Config.Maps.AddRange([payments, shipping]);
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true);

        Assert.Contains("## Workspace maps", prompt);
        Assert.Contains("### Payments", prompt);
        Assert.Contains("### Shipping", prompt);
    }

    [Fact]
    public void Build_ScopedToOneMap_RendersOnlyThatMap()
    {
        // The proactive-investigation runner passes just the map the fired resource matched —
        // other projects' maps stay out of the model's context.
        WorkspaceMap? payments = null;
        var builder = BuilderWith(profiles =>
        {
            payments = new WorkspaceMap { Name = "Payments" };
            payments.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            var shipping = new WorkspaceMap { Name = "Shipping" };
            shipping.Nodes.Add(Node("n2", WorkspaceResourceArea.Redis, "cache-1", "sessions"));
            profiles.Config.Maps.AddRange([payments, shipping]);
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true, maps: [payments!]);

        Assert.Contains("### Payments", prompt);
        Assert.DoesNotContain("Shipping", prompt);
    }

    [Fact]
    public void Build_ScopedToAnEmptyList_OmitsTheMapSectionEntirely()
    {
        // The no-map investigation variant: an explicit empty list means "nothing matched" —
        // different from null (render everything), so the runner can suppress the section.
        var builder = BuilderWith(profiles =>
        {
            var m = new WorkspaceMap { Name = "Payments" };
            m.Nodes.Add(Node("n1", WorkspaceResourceArea.Aks, "prod/api", "api"));
            profiles.Config.Maps.Add(m);
        });

        var prompt = builder.Build(context: null, "ask", "feature", hasToolCalling: true, maps: []);

        Assert.DoesNotContain("## Workspace map", prompt);
        Assert.DoesNotContain("Payments", prompt);
    }

    // ── Background-investigation variant (ai-insight-reports) ──

    [Fact]
    public void Build_BackgroundInvestigation_DropsInteractiveOnlyGuidance_ButKeepsWorkspaceContext()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "workspace", hasToolCalling: true,
            forBackgroundInvestigation: true);

        // The interactive response-format block would contradict the runner's JSON-only contract.
        Assert.DoesNotContain("## Response format", prompt);
        Assert.DoesNotContain("bullet points and tables", prompt);
        // Nobody reads the reply live — "tell the user to switch modes" is dead guidance.
        Assert.DoesNotContain("Ask & do", prompt);
        // The investigation-scoped tool policy replaces the interactive one.
        Assert.Contains("## Tool policy (background investigation)", prompt);
        Assert.Contains("read-only", prompt);
        // Role + workspace context are what the investigation reasons over — they stay.
        Assert.Contains("SwebKit Assistant", prompt);
        Assert.Contains("## Current workspace context", prompt);
    }

    [Fact]
    public void Build_InteractiveTurn_UnchangedByTheNewParameter()
    {
        var prompt = CreateBuilder().Build(context: null, "ask", "workspace", hasToolCalling: true);

        Assert.Contains("## Response format", prompt);
        Assert.Contains("## Tool policy (Ask mode)", prompt);
    }
}
