using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class WorkspaceMapLookupTests
{
    private static WorkspaceMap Map(string name, params WorkspaceResourceNode[] nodes)
    {
        var map = new WorkspaceMap { Name = name };
        map.Nodes.AddRange(nodes);
        return map;
    }

    private static WorkspaceResourceNode Node(string key, string label = "", string? ctx = null) =>
        new() { Area = WorkspaceResourceArea.Aks, ResourceKey = key, DisplayLabel = label, KubeconfigContext = ctx };

    [Fact]
    public void FindNode_NoMaps_ReturnsNull()
    {
        Assert.Null(WorkspaceMapLookup.FindNode([], WorkspaceResourceArea.Aks, "prod"));
    }

    [Fact]
    public void FindNode_MatchingNode_ReturnsTheOwningMap()
    {
        var m1 = Map("first", Node("other/thing", "thing"));
        var m2 = Map("second", Node("prod/api", "api"));

        var match = WorkspaceMapLookup.FindNode([m1, m2], WorkspaceResourceArea.Aks, "prod");

        Assert.NotNull(match);
        Assert.Same(m2, match!.Value.Map);
    }

    [Fact]
    public void FindNode_NoContextGiven_MatchesAnyNode()
    {
        var m = Map("m", Node("prod/api", "api", ctx: "ctx-a"));

        var match = WorkspaceMapLookup.FindNode([m], WorkspaceResourceArea.Aks, "prod");

        Assert.NotNull(match);
    }

    [Fact]
    public void FindNode_ContextGiven_PrefersTheExactContextMatchOverUnscoped()
    {
        var unscoped = Map("unscoped", Node("prod/api", "api-unscoped"));
        var pinned = Map("pinned", Node("prod/api", "api-pinned", ctx: "ctx-b"));

        var match = WorkspaceMapLookup.FindNode([unscoped, pinned], WorkspaceResourceArea.Aks, "prod", "ctx-b");

        Assert.Same(pinned, match!.Value.Map);
        Assert.Equal("api-pinned", match.Value.Node.DisplayLabel);
    }

    [Fact]
    public void FindNode_ContextGiven_FallsBackToAnUnscopedNode()
    {
        // A node without a context follows whatever the config resolves — still a valid match.
        var m = Map("m", Node("prod/api", "api"));

        var match = WorkspaceMapLookup.FindNode([m], WorkspaceResourceArea.Aks, "prod", "ctx-b");

        Assert.Same(m, match!.Value.Map);
    }

    [Fact]
    public void FindNode_ContextGiven_NodePinnedToADifferentContext_DoesNotMatch()
    {
        var m = Map("m", Node("prod/api", "api", ctx: "ctx-a"));

        Assert.Null(WorkspaceMapLookup.FindNode([m], WorkspaceResourceArea.Aks, "prod", "ctx-b"));
    }

    [Fact]
    public void FindNode_SameKeyOnTwoClusters_PicksTheRightMap()
    {
        var mapA = Map("Project A", Node("prod/api", "api", ctx: "ctx-a"));
        var mapB = Map("Project B", Node("prod/api", "api", ctx: "ctx-b"));

        var match = WorkspaceMapLookup.FindNode([mapA, mapB], WorkspaceResourceArea.Aks, "prod", "ctx-b");

        Assert.Same(mapB, match!.Value.Map);
    }

    [Fact]
    public void EffectiveMaps_NoLegacyContent_ReturnsMapsAsIs()
    {
        var config = new AppConfig();
        config.Maps.Add(Map("only"));

        Assert.Same(config.Maps[0], Assert.Single(config.EffectiveMaps()));
    }

    [Fact]
    public void EffectiveMaps_LegacyTopologyStillPopulated_WrapsItAsAPseudoMap()
    {
        var config = new AppConfig { Name = "Legacy Profile" };
        config.Topology.Nodes.Add(Node("prod/api", "api"));

        var map = Assert.Single(config.EffectiveMaps());
        Assert.Equal("legacy", map.Id);
        Assert.Equal("Legacy Profile", map.Name);
        Assert.Single(map.Nodes);
    }

    [Fact]
    public void EffectiveMaps_MapsPlusLegacyContent_AppendsThePseudoMap()
    {
        var config = new AppConfig { Name = "P" };
        config.Maps.Add(Map("real"));
        config.Topology.Nodes.Add(Node("prod/api", "api"));

        Assert.Equal(2, config.EffectiveMaps().Count());
    }
}
