using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Core.Tests;

/// <summary>
/// Covers the collection/folder resolution that confirmed agent mutations rely on:
/// resolve by ID or name, create the collection when only a name is known, and
/// materialize missing folder segments instead of failing.
/// </summary>
public sealed class ApiClientAgentServiceTests : IDisposable
{
    private readonly AppDataSandbox _sandbox = new();
    private readonly CollectionRepository _repo = new();
    private readonly ApiClientAgentService _service;

    public ApiClientAgentServiceTests()
    {
        _repo.LoadAsync().GetAwaiter().GetResult();
        _service = new ApiClientAgentService(
            _repo,
            new LinkedCollectionRootRepository(),
            new LinkedCollectionFileService(new LinkedGitService()),
            new AppEventBus(Microsoft.Extensions.Logging.Abstractions.NullLogger<AppEventBus>.Instance));
    }

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task CreateRequest_ById_PutsRequestAtCollectionRoot()
    {
        var collection = await _repo.AddCollectionAsync("Auth API");

        var result = await _service.CreateRequestAsync(
            collection.Id, null, "Get token", ApiRequestMethod.Post, "https://x.test/token");

        Assert.True(result.IsSuccess);
        Assert.Equal(collection.Id, result.CollectionId);
        var node = Assert.Single(_repo.Collections.Single(c => c.Id == collection.Id).Nodes);
        Assert.Equal("Get token", node.Request?.Name);
    }

    [Fact]
    public async Task CreateRequest_ByName_ResolvesExistingCollection()
    {
        var collection = await _repo.AddCollectionAsync("Phone Notification");

        var result = await _service.CreateRequestAsync(
            "phone notification", null, "Send sms", ApiRequestMethod.Post, "https://x.test/sms");

        Assert.True(result.IsSuccess);
        Assert.Equal(collection.Id, result.CollectionId);
        Assert.Single(_repo.Collections); // resolved, not duplicated
    }

    [Fact]
    public async Task CreateRequest_UnknownName_CreatesCollectionAndFolders()
    {
        var result = await _service.CreateRequestAsync(
            "Phone Notification", "Signing/Onboarding", "1. Create package",
            ApiRequestMethod.Post, "https://x.test/packages");

        Assert.True(result.IsSuccess);
        var collection = Assert.Single(_repo.Collections);
        Assert.Equal("Phone Notification", collection.Name);
        Assert.Equal(result.CollectionId, collection.Id);

        var signing = Assert.Single(collection.Nodes);
        Assert.Equal(ApiCollectionNodeType.Folder, signing.Type);
        Assert.Equal("Signing", signing.Name);

        var onboarding = Assert.Single(signing.Children);
        Assert.Equal(ApiCollectionNodeType.Folder, onboarding.Type);
        Assert.Equal("Onboarding", onboarding.Name);

        var request = Assert.Single(onboarding.Children);
        Assert.Equal("1. Create package", request.Request?.Name);
        Assert.Equal(result.RequestId, request.Request?.Id);
    }

    [Fact]
    public async Task CreateRequest_ExistingCollection_MaterializesOnlyMissingFolderSegments()
    {
        var collection = await _repo.AddCollectionAsync("Phone Notification");
        collection.Nodes.Add(new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Folder,
            Name = "Signing",
        });
        await _repo.UpdateCollectionAsync(collection);

        var result = await _service.CreateRequestAsync(
            collection.Id, "Signing/Onboarding", "r", ApiRequestMethod.Get, "https://x.test");

        Assert.True(result.IsSuccess);
        var signing = Assert.Single(_repo.Collections.Single(c => c.Id == collection.Id).Nodes);
        Assert.Equal("Signing", signing.Name);
        Assert.Equal("Onboarding", Assert.Single(signing.Children).Name);
    }

    [Fact]
    public async Task CreateRequest_StaleGeneratedId_FailsInsteadOfNamingACollectionAfterIt()
    {
        var result = await _service.CreateRequestAsync(
            new string('a', 32), null, "r", ApiRequestMethod.Get, "https://x.test");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage);
        Assert.Empty(_repo.Collections);
    }

    [Fact]
    public async Task GetCollections_ListsIdsFoldersAndRequestCounts()
    {
        var collection = await _repo.AddCollectionAsync("Auth API");
        await _service.CreateRequestAsync(collection.Id, "Tokens", "r", ApiRequestMethod.Get, "https://x.test");

        var summaries = await _service.GetCollectionsAsync();

        var summary = Assert.Single(summaries);
        Assert.Equal(collection.Id, summary.Id);
        Assert.Equal("Auth API", summary.Name);
        Assert.Equal("local", summary.Origin);
        Assert.Equal(["Tokens"], summary.FolderPaths);
        Assert.Equal(1, summary.RequestCount);
    }
}
