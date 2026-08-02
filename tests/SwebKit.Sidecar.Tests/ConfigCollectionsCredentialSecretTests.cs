using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Regression coverage for the "CredentialSecret" structural guard (test-plan.md §1 item 7 /
/// technical-plan.md §3.6): <see cref="ConfigEndpoints.SaveCollectionsAsync"/> must never let a
/// populated <see cref="AuthConfig.CredentialSecret"/> reach <c>collections.json</c>, regardless of
/// where in the collection tree it appears (collection-level default auth, folder-level default
/// auth, or a single request's own auth).
/// </summary>
public class ConfigCollectionsCredentialSecretTests
{
    private const string SecretValue = "super-secret-transient-value";

    [Fact]
    public async Task SaveCollectionsAsync_StripsCredentialSecret_FromCollectionDefaultAuth()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new CollectionRepository();
        var demo = new DemoModeService();
        var store = new CollectionsStore
        {
            Collections =
            [
                new ApiCollection
                {
                    Id = "c1",
                    Name = "Collection with secret",
                    DefaultAuth = new AuthConfig { Type = AuthType.BearerToken, CredentialSecret = SecretValue },
                },
            ],
        };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, demo);

        var ok = Assert.IsAssignableFrom<Ok<CollectionsStoreResponse>>(result);
        Assert.Null(ok.Value!.Collections[0].DefaultAuth!.CredentialSecret);

        // Reload from disk into a fresh repository to prove it was never persisted, not just
        // stripped from the in-memory object the handler happened to return.
        var reloaded = new CollectionRepository();
        await reloaded.LoadAsync();
        Assert.Null(reloaded.Collections[0].DefaultAuth!.CredentialSecret);
    }

    [Fact]
    public async Task SaveCollectionsAsync_StripsCredentialSecret_FromFolderDefaultAuth_AndRequestAuth_DeeplyNested()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new CollectionRepository();
        var demo = new DemoModeService();
        var leafRequest = new ApiCollectionNode
        {
            Id = "req1",
            Type = ApiCollectionNodeType.Request,
            Name = "Leaf request",
            Request = new HttpRequestEntry
            {
                Id = "r1",
                Auth = new AuthConfig { Type = AuthType.ApiKey, CredentialSecret = SecretValue },
            },
        };
        var nestedFolder = new ApiCollectionNode
        {
            Id = "folder2",
            Type = ApiCollectionNodeType.Folder,
            Name = "Nested folder",
            DefaultAuth = new AuthConfig { Type = AuthType.Basic, CredentialSecret = SecretValue },
            Children = [leafRequest],
        };
        var topFolder = new ApiCollectionNode
        {
            Id = "folder1",
            Type = ApiCollectionNodeType.Folder,
            Name = "Top folder",
            Children = [nestedFolder],
        };
        var store = new CollectionsStore
        {
            Collections =
            [
                new ApiCollection { Id = "c1", Name = "Deep collection", Nodes = [topFolder] },
            ],
        };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, demo);

        var ok = Assert.IsAssignableFrom<Ok<CollectionsStoreResponse>>(result);
        var savedTopFolder = ok.Value!.Collections[0].Nodes[0];
        var savedNestedFolder = savedTopFolder.Children[0];
        var savedLeaf = savedNestedFolder.Children[0];
        Assert.Null(savedNestedFolder.DefaultAuth!.CredentialSecret);
        Assert.Null(savedLeaf.Request!.Auth!.CredentialSecret);

        var reloaded = new CollectionRepository();
        await reloaded.LoadAsync();
        var diskTopFolder = reloaded.Collections[0].Nodes[0];
        var diskNestedFolder = diskTopFolder.Children[0];
        var diskLeaf = diskNestedFolder.Children[0];
        Assert.Null(diskNestedFolder.DefaultAuth!.CredentialSecret);
        Assert.Null(diskLeaf.Request!.Auth!.CredentialSecret);
    }

    [Fact]
    public async Task SaveCollectionsAsync_NonSecretAuthFields_ArePreserved()
    {
        // Guards against an overly-broad strip implementation that nulls the whole AuthConfig
        // instead of just CredentialSecret.
        using var sandbox = new AppDataSandbox();
        var repo = new CollectionRepository();
        var demo = new DemoModeService();
        var store = new CollectionsStore
        {
            Collections =
            [
                new ApiCollection
                {
                    Id = "c1",
                    Name = "Collection",
                    DefaultAuth = new AuthConfig
                    {
                        Type = AuthType.Basic,
                        CredentialSecret = SecretValue,
                        BasicUsername = "alice",
                        CredentialKey = "sw-secret:abc123",
                    },
                },
            ],
        };

        var result = await ConfigEndpoints.SaveCollectionsAsync(repo, store, demo);

        var ok = Assert.IsAssignableFrom<Ok<CollectionsStoreResponse>>(result);
        var auth = ok.Value!.Collections[0].DefaultAuth!;
        Assert.Null(auth.CredentialSecret);
        Assert.Equal("alice", auth.BasicUsername);
        Assert.Equal("sw-secret:abc123", auth.CredentialKey);
    }
}
