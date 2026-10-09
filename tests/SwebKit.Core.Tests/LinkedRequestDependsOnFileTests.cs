using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class LinkedRequestDependsOnFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-linked-deps-tests", Guid.NewGuid().ToString("N"));
    private readonly LinkedCollectionFileService _fileService = new(new LinkedGitService());

    [Fact]
    public async Task SaveRequestAsync_DependsOnRequestIds_RoundTrips()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "get-token.swebreq.json"), """
            {
              "method": "Get",
              "url": "/token"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var tokenRequest = Assert.Single(collection.Nodes).Request!;

        var dependent = new HttpRequestEntry
        {
            Id = "list-orders",
            Name = "List orders",
            Method = ApiRequestMethod.Get,
            Url = "/orders",
            DependsOnRequestIds = [tokenRequest.Id],
        };

        await _fileService.SaveRequestAsync(result.ApiRootPath, collection, dependent);

        var json = await File.ReadAllTextAsync(Path.Combine(collectionPath, "list-orders.swebreq.json"));
        Assert.Contains("dependsOnRequestIds", json);
        Assert.Contains(tokenRequest.Id, json);

        var reloaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var reloadedCollection = Assert.Single(reloaded.Collections);
        // Request ids are path-derived for linked files — locate the dependent by its URL.
        var reloadedRequest = Assert.Single(reloadedCollection.Nodes, n => n.Request?.Url == "/orders").Request!;
        Assert.Equal([tokenRequest.Id], reloadedRequest.DependsOnRequestIds);
    }

    [Fact]
    public async Task SaveRequestAsync_NoDependencies_OmitsField()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);

        var collection = new ApiCollection { Id = "orders", Name = "orders" };
        var request = new HttpRequestEntry
        {
            Id = "plain",
            Name = "Plain",
            Method = ApiRequestMethod.Get,
            Url = "/orders",
        };

        await _fileService.SaveRequestAsync(apiRoot, collection, request);

        var json = await File.ReadAllTextAsync(Path.Combine(collectionPath, "plain.swebreq.json"));
        Assert.DoesNotContain("dependsOnRequestIds", json);
    }

    [Fact]
    public async Task LoadRootAsync_RequestWithoutDependsOn_DefaultsToEmpty()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "get-order.swebreq.json"), """
            {
              "method": "Get",
              "url": "/orders/1"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var request = Assert.Single(Assert.Single(result.Collections).Nodes).Request!;
        Assert.Empty(request.DependsOnRequestIds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
