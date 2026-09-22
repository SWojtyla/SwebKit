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
        Assert.Empty(repository.Config.Maps);
    }

    [Fact]
    public async Task SaveThenLoad_MapNodesAndRelationships_RoundTrip()
    {
        using var _ = new AppDataSandbox();

        var repository = new ProfileRepository();
        var node1 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.Aks, ResourceKey = "prod/api", DisplayLabel = "api (prod)", KubeconfigContext = "ctx-a" };
        var node2 = new WorkspaceResourceNode { Area = WorkspaceResourceArea.ServiceBus, ResourceKey = "orders.servicebus.windows.net", DisplayLabel = "orders" };
        var map = new WorkspaceMap { Name = "Payments" };
        map.Nodes.Add(node1);
        map.Nodes.Add(node2);
        map.Relationships.Add(new WorkspaceResourceRelationship
        {
            FromNodeId = node1.Id,
            ToNodeId = node2.Id,
            Label = "consumes",
        });
        repository.Config.Maps.Add(map);

        await repository.SaveAsync();

        var reloaded = new ProfileRepository();
        await reloaded.LoadAsync();

        var loadedMap = Assert.Single(reloaded.Config.Maps);
        Assert.Equal("Payments", loadedMap.Name);
        Assert.Equal(map.Id, loadedMap.Id);
        Assert.Equal(2, loadedMap.Nodes.Count);
        Assert.Equal("ctx-a", loadedMap.Nodes[0].KubeconfigContext);
        var relationship = Assert.Single(loadedMap.Relationships);
        Assert.Equal(node1.Id, relationship.FromNodeId);
        Assert.Equal(node2.Id, relationship.ToNodeId);
        Assert.Equal("consumes", relationship.Label);
    }

    [Fact]
    public async Task LoadAsync_LegacyTopologyJson_MigratesIntoANamedMap_WithoutDuplicatingOnReload()
    {
        using var _ = new AppDataSandbox();

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(
            AppDataPaths.ProfilesJson,
            """{"config":{"name":"legacy","topology":{"nodes":[{"id":"n1","area":"Aks","resourceKey":"prod/api","displayLabel":"api"}],"relationships":[]}},"serviceBusNamespaces":[],"messageTemplates":[],"schemaVersion":2}""");

        var repository = new ProfileRepository();
        await repository.LoadAsync();

        var map = Assert.Single(repository.Config.Maps);
        Assert.Equal("legacy", map.Name); // named after the profile
        var node = Assert.Single(map.Nodes);
        Assert.Equal("n1", node.Id);
        Assert.Equal("prod/api", node.ResourceKey);
        Assert.Empty(repository.Config.Topology.Nodes); // drained — can't migrate twice

        // Persist and reload — the migrated map must survive as-is, not spawn a second copy.
        await repository.SaveAsync();
        var reloaded = new ProfileRepository();
        await reloaded.LoadAsync();

        var reloadedMap = Assert.Single(reloaded.Config.Maps);
        Assert.Equal(map.Id, reloadedMap.Id);
        Assert.Single(reloadedMap.Nodes);
    }
}
