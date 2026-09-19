using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Exercises the store-PUT partition in <see cref="ConfigEndpoints.SaveCollectionsAsync"/>:
/// linked collections reconcile into their folders while local ones persist to collections.json —
/// including the conflict/force contract and the "writes nothing when a conflict exists" guarantee.
/// </summary>
public sealed class LinkedCollectionsPartitionTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-partition-tests", Guid.NewGuid().ToString("N"));

    private static HttpRequestEntry Request(string id, string name, string url = "/orders") => new()
    {
        Id = id,
        Name = name,
        Method = ApiRequestMethod.Get,
        Url = url,
    };

    private static ApiCollectionNode RequestNode(HttpRequestEntry request) => new()
    {
        Id = request.Id,
        Type = ApiCollectionNodeType.Request,
        Name = request.Name,
        Request = request,
    };

    private static ApiCollection Collection(string id, string name, params ApiCollectionNode[] nodes) => new()
    {
        Id = id,
        Name = name,
        Nodes = nodes.ToList(),
    };

    private async Task<(LinkedCollectionsService Linked, CollectionRepository Repo, string ApiRoot)> SetupAsync(
        AppDataSandbox sandbox)
    {
        var roots = new LinkedCollectionRootRepository();
        await roots.LoadAsync();
        var fileService = new LinkedCollectionFileService(new LinkedGitService());
        var linked = new LinkedCollectionsService(roots, fileService, new BrunoSyncService(), NullLogger<LinkedCollectionsService>.Instance);
        var apiRoot = await fileService.EnsureRootAsync(_root, "Project APIs");
        await roots.AddRootAsync(_root, "Project APIs");
        await linked.ReloadAsync();

        var repo = new CollectionRepository();
        await repo.LoadAsync();
        return (linked, repo, apiRoot);
    }

    /// <summary>Seeds a collection into the linked root via the service's own sync path.</summary>
    private static async Task SeedLinkedAsync(LinkedCollectionsService linked, ApiCollection collection)
    {
        collection.LinkedRootId = linked.Roots[0].Config.Id;
        var result = await linked.SyncCollectionAsync(collection, force: false);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SaveCollections_LinkedCollection_RoutesToFilesNotCollectionsJson()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, apiRoot) = await SetupAsync(sandbox);

        // Seed a linked collection on disk, then reload so the service knows it.
        await SeedLinkedAsync(linked, Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order"))));
        var linkedFile = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        Assert.True(File.Exists(linkedFile));

        // The store PUT carries the same collection (linked) plus a new local one.
        var incoming = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/v2")));
        incoming.LinkedRootId = linked.Roots[0].Config.Id;
        var store = new CollectionsStore
        {
            Collections = [incoming, Collection("local-1", "Local Stuff")],
        };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, new DemoModeService(), linked, CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Contains("/orders/v2", await File.ReadAllTextAsync(linkedFile));
        Assert.Single(repo.Collections);
        Assert.Equal("local-1", repo.Collections[0].Id);
    }

    [Fact]
    public async Task SaveCollections_NewCollectionTargetingRoot_WritesIntoLinkedFolder()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, apiRoot) = await SetupAsync(sandbox);

        var incoming = Collection("col-new", "Payments", RequestNode(Request("req-9", "Pay")));
        incoming.LinkedRootId = linked.Roots[0].Config.Id;
        var store = new CollectionsStore { Collections = [incoming] };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, new DemoModeService(), linked, CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.True(File.Exists(Path.Combine(apiRoot, "collections", "payments", "collection.json")));
        Assert.True(File.Exists(Path.Combine(apiRoot, "collections", "payments", "pay.swebreq.json")));
        Assert.Empty(repo.Collections);
    }

    [Fact]
    public async Task SaveCollections_UnknownLinkedRootMarker_FallsBackToLocal()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, _) = await SetupAsync(sandbox);

        // A linkedRootId the server doesn't know is ignored rather than dropping the collection.
        var incoming = Collection("col-x", "Mystery");
        incoming.LinkedRootId = "root-that-does-not-exist";
        var store = new CollectionsStore { Collections = [incoming] };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, new DemoModeService(), linked, CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var persisted = Assert.Single(repo.Collections);
        Assert.Null(persisted.LinkedRootId);
    }

    [Fact]
    public async Task SaveCollections_DeletedLinkedCollection_RemovesItsDirectory()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, apiRoot) = await SetupAsync(sandbox);

        await SeedLinkedAsync(linked, Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order"))));
        var collectionDir = Path.Combine(apiRoot, "collections", "orders");
        Assert.True(Directory.Exists(collectionDir));

        // Incoming store no longer contains col-1 — the linked directory must go.
        var store = new CollectionsStore { Collections = [Collection("local-1", "Local")] };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, new DemoModeService(), linked, CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.False(Directory.Exists(collectionDir));
    }

    [Fact]
    public async Task SaveCollections_ExternalEdit_Returns409AndWritesNothing()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, apiRoot) = await SetupAsync(sandbox);

        await SeedLinkedAsync(linked, Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order"))));
        var linkedFile = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        await File.WriteAllTextAsync(linkedFile, """{ "method": "Get", "url": "/orders/external-edit" }""");

        var incoming = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/mine")));
        incoming.LinkedRootId = linked.Roots[0].Config.Id;
        var store = new CollectionsStore { Collections = [incoming, Collection("local-1", "Local")] };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, new DemoModeService(), linked, CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        // Neither the linked file nor collections.json may have moved.
        Assert.Contains("external-edit", await File.ReadAllTextAsync(linkedFile));
        Assert.Empty(repo.Collections);
    }

    [Fact]
    public async Task SaveCollections_ExternalEdit_WithForce_Overwrites()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, apiRoot) = await SetupAsync(sandbox);

        await SeedLinkedAsync(linked, Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order"))));
        var linkedFile = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        await File.WriteAllTextAsync(linkedFile, """{ "method": "Get", "url": "/orders/external-edit" }""");

        var incoming = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/mine")));
        incoming.LinkedRootId = linked.Roots[0].Config.Id;
        var store = new CollectionsStore { Collections = [incoming] };

        var result = await ConfigEndpoints.SaveCollectionsAsync(
            repo, store, new DemoModeService(), linked, CancellationToken.None, force: true);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Contains("/orders/mine", await File.ReadAllTextAsync(linkedFile));
    }

    [Fact]
    public async Task SaveCollections_ConcurrencyTokenMismatch_Returns409()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, _) = await SetupAsync(sandbox);
        await repo.ReplaceStoreAsync(new CollectionsStore { Collections = [Collection("local-1", "Local")] });

        var store = new CollectionsStore { Collections = [Collection("local-2", "Other")] };
        var result = await ConfigEndpoints.SaveCollectionsAsync(
            repo, store, new DemoModeService(), linked, CancellationToken.None, concurrencyToken: "stale-token");

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Single(repo.Collections);
        Assert.Equal("local-1", repo.Collections[0].Id);
    }

    [Fact]
    public async Task GetCollectionsStore_MergesLinkedCollectionsIntoResponse()
    {
        using var sandbox = new AppDataSandbox();
        var (linked, repo, _) = await SetupAsync(sandbox);
        await SeedLinkedAsync(linked, Collection("col-1", "Orders"));
        await repo.ReplaceStoreAsync(new CollectionsStore { Collections = [Collection("local-1", "Local")] });

        var result = await ConfigEndpoints.GetCollectionsStoreAsync(repo, new DemoModeService(), linked, CancellationToken.None);

        var value = Assert.IsType<CollectionsStoreResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal(2, value.Collections.Count);
        var linkedCollection = Assert.Single(value.Collections, c => c.Id == "col-1");
        Assert.Equal(linked.Roots[0].Config.Id, linkedCollection.LinkedRootId);
        Assert.Null(Assert.Single(value.Collections, c => c.Id == "local-1").LinkedRootId);
    }
}
