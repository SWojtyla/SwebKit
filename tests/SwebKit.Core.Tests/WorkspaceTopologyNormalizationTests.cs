using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class WorkspaceTopologyNormalizationTests
{
    [Fact]
    public async Task LoadAsync_LegacyJsonWithNoTopologyKeyAtAll_NormalizesToEmptyNonNullLists()
    {
        using var _ = new AppDataSandbox();

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(
            AppDataPaths.ProfilesJson,
            """{"config":{"name":"legacy"},"serviceBusNamespaces":[],"messageTemplates":[],"schemaVersion":2}""");

        var repository = new ProfileRepository();
        await repository.LoadAsync();

        Assert.NotNull(repository.Config.Topology);
        Assert.Empty(repository.Config.Topology.Nodes);
        Assert.Empty(repository.Config.Topology.Relationships);
    }

    [Fact]
    public async Task SaveThenLoad_TopologyNodesAndRelationships_RoundTrip()
    {
        using var _ = new AppDataSandbox();

        var repository = new ProfileRepository();
        var node1 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api (prod)" };
        var node2 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        repository.Config.Topology.Nodes.Add(node1);
        repository.Config.Topology.Nodes.Add(node2);
        repository.Config.Topology.Relationships.Add(new WorkspaceResourceRelationship
        {
            FromNodeId = node1.Id,
            ToNodeId = node2.Id,
            Label = "consumes",
        });

        await repository.SaveAsync();

        var reloaded = new ProfileRepository();
        await reloaded.LoadAsync();

        Assert.Equal(2, reloaded.Config.Topology.Nodes.Count);
        var relationship = Assert.Single(reloaded.Config.Topology.Relationships);
        Assert.Equal(node1.Id, relationship.FromNodeId);
        Assert.Equal(node2.Id, relationship.ToNodeId);
        Assert.Equal("consumes", relationship.Label);
    }
}
