using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Endpoint-level coverage for <c>/api/linked-roots</c> (api-client-workspace Slice B): the thin
/// adapter behavior — status codes, error envelopes, the content-stamp conflict path — exercised
/// against real repositories + the real <see cref="LinkedCollectionFileService"/>, all hermetic:
/// the root registry lands in a temp <c>SWEBKIT_APPDATA_ROOT</c> sandbox and every linked root
/// tree lives in a per-test temp folder.
/// </summary>
public class LinkedRootsEndpointsTests : IDisposable
{
    private readonly AppDataSandbox _sandbox = new();
    private readonly LinkedCollectionRootRepository _roots = new();
    private readonly LinkedCollectionFileService _files = new(new LinkedGitService());
    private readonly DemoModeService _demo = new();
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "swebkit-linkedroot-tests", Guid.NewGuid().ToString("N"));

    public LinkedRootsEndpointsTests() => Directory.CreateDirectory(_rootDir);

    public void Dispose()
    {
        _sandbox.Dispose();
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T Value<T>(IResult result) =>
        Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 200;

    /// <summary>Extracts the <c>roots</c> payload of the anonymous <c>{ roots }</c> response.</summary>
    private static List<LinkedCollectionRootSummary> RootsOf(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        return Assert.IsType<List<LinkedCollectionRootSummary>>(
            value.GetType().GetProperty("roots")!.GetValue(value));
    }

    private async Task<LinkedCollectionRootSummary> AddRootAsync(string? name = null)
    {
        var result = await LinkedRootsEndpoints.CreateRootAsync(
            new CreateLinkedRootRequest { Path = _rootDir, Name = name },
            _roots, _files, _demo, CancellationToken.None);
        return Value<LinkedCollectionRootSummary>(result);
    }

    private string ApiRoot => Path.Combine(_rootDir, LinkedCollectionFileService.RootFolderName);

    private async Task<(string CollectionId, LinkedCollectionRootSummary Root)> AddCollectionAsync(string name)
    {
        var root = await AddRootAsync();
        var created = await LinkedRootsEndpoints.CreateCollectionAsync(
            root.Id, new CreateLinkedNodeRequest { Name = name },
            _roots, _files, _demo, CancellationToken.None);
        var mutation = Value<LinkedCollectionMutationResult>(created);
        return (mutation.CollectionId, mutation.Root);
    }

    private async Task<LinkedCollectionRootSummary> ReloadAsync(string rootId) =>
        Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.ReloadRootAsync(
            rootId, _roots, _files, _demo, CancellationToken.None));

    // ── Roots ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRootAsync_CreatesSwebKitApiTree_AndReturnsLoadedRoot()
    {
        var root = await AddRootAsync("My APIs");

        Assert.True(Directory.Exists(ApiRoot));
        Assert.True(File.Exists(Path.Combine(ApiRoot, "swebkit.json")));
        Assert.True(Directory.Exists(Path.Combine(ApiRoot, "collections")));
        Assert.Equal("My APIs", root.Name);
        Assert.Equal(ApiRoot, root.ApiRootPath);
        Assert.True(root.IsEnabled);
        Assert.True(root.IsValid);
        Assert.Single(_roots.Roots);
    }

    [Fact]
    public async Task CreateRootAsync_DefaultsNameToFolderName()
    {
        var root = await AddRootAsync();

        Assert.Equal(new DirectoryInfo(_rootDir).Name, root.Name);
    }

    [Fact]
    public async Task CreateRootAsync_AdoptsExistingApiRoot_WithoutOverwritingManifest()
    {
        var manifestPath = Path.Combine(ApiRoot, "swebkit.json");
        Directory.CreateDirectory(ApiRoot);
        await File.WriteAllTextAsync(manifestPath, """{ "schemaVersion": 1, "format": "swebkit-api-root", "name": "Preexisting" }""");

        var root = await AddRootAsync();

        Assert.Contains("Preexisting", await File.ReadAllTextAsync(manifestPath));
        Assert.True(root.IsValid);
    }

    [Fact]
    public async Task CreateRootAsync_MissingOrInvalidPath_Returns400()
    {
        var missing = await LinkedRootsEndpoints.CreateRootAsync(
            new CreateLinkedRootRequest { Path = Path.Combine(_rootDir, "does-not-exist") },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(missing));

        var blank = await LinkedRootsEndpoints.CreateRootAsync(
            new CreateLinkedRootRequest { Path = "  " },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(blank));
        Assert.Empty(_roots.Roots);
    }

    [Fact]
    public async Task CreateRootAsync_AlreadyRegistered_Returns409()
    {
        await AddRootAsync();

        var samePath = await LinkedRootsEndpoints.CreateRootAsync(
            new CreateLinkedRootRequest { Path = _rootDir },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(samePath));

        // The .swebkit-api child path addresses the same root — also a conflict.
        var apiChild = await LinkedRootsEndpoints.CreateRootAsync(
            new CreateLinkedRootRequest { Path = ApiRoot },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(apiChild));
        Assert.Single(_roots.Roots);
    }

    [Fact]
    public async Task GetRootsAsync_ListsRegisteredRoots()
    {
        Assert.Empty(RootsOf(await LinkedRootsEndpoints.GetRootsAsync(_roots, _files, _demo, CancellationToken.None)));

        var added = await AddRootAsync("Team APIs");
        var roots = RootsOf(await LinkedRootsEndpoints.GetRootsAsync(_roots, _files, _demo, CancellationToken.None));

        var root = Assert.Single(roots);
        Assert.Equal(added.Id, root.Id);
        Assert.Equal("Team APIs", root.Name);
    }

    [Fact]
    public async Task UpdateRootAsync_Renames()
    {
        var root = await AddRootAsync();

        var updated = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.UpdateRootAsync(
            root.Id, new UpdateLinkedRootRequest { Name = "Renamed Root" },
            _roots, _files, _demo, CancellationToken.None));

        Assert.Equal("Renamed Root", updated.Name);
        Assert.Equal("Renamed Root", _roots.Roots[0].Name);
    }

    [Fact]
    public async Task UpdateRootAsync_Disable_KeepsListedButSkipsLoading()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        Assert.NotEmpty(root.Collections);

        var updated = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.UpdateRootAsync(
            root.Id, new UpdateLinkedRootRequest { IsEnabled = false },
            _roots, _files, _demo, CancellationToken.None));

        Assert.False(updated.IsEnabled);
        Assert.Empty(updated.Collections);

        var listed = Assert.Single(RootsOf(await LinkedRootsEndpoints.GetRootsAsync(_roots, _files, _demo, CancellationToken.None)));
        Assert.False(listed.IsEnabled);
        Assert.Empty(listed.Collections);

        // Re-enable brings the collections back without re-registering.
        var reenabled = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.UpdateRootAsync(
            root.Id, new UpdateLinkedRootRequest { IsEnabled = true },
            _roots, _files, _demo, CancellationToken.None));
        Assert.Contains(reenabled.Collections, c => c.Id == collectionId);
    }

    [Fact]
    public async Task UpdateRootAsync_UnknownRoot_Returns404()
    {
        var result = await LinkedRootsEndpoints.UpdateRootAsync(
            "nope", new UpdateLinkedRootRequest { Name = "x" },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task DeleteRootAsync_Unregisters_WithoutDeletingFiles()
    {
        var root = await AddRootAsync();

        var deleted = await LinkedRootsEndpoints.DeleteRootAsync(root.Id, _roots, _demo);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(deleted));
        Assert.Empty(_roots.Roots);
        // Unregister only — the .swebkit-api tree is the user's data and stays on disk.
        Assert.True(Directory.Exists(ApiRoot));

        var again = await LinkedRootsEndpoints.DeleteRootAsync(root.Id, _roots, _demo);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(again));
    }

    [Fact]
    public async Task ReloadRootAsync_PicksUpExternalEdits()
    {
        var root = await AddRootAsync();
        var collectionDir = Path.Combine(ApiRoot, "collections", "external");
        Directory.CreateDirectory(collectionDir);
        await File.WriteAllTextAsync(Path.Combine(collectionDir, "ping.swebreq.json"), """{ "method": "Get", "url": "/ping" }""");

        var reloaded = await ReloadAsync(root.Id);

        var collection = Assert.Single(reloaded.Collections);
        Assert.Equal("External", collection.Name);
        Assert.Single(collection.Nodes);
    }

    // ── Collections ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCollectionAsync_WritesManifest_AndReturnsIt()
    {
        var root = await AddRootAsync();

        var created = await LinkedRootsEndpoints.CreateCollectionAsync(
            root.Id, new CreateLinkedNodeRequest { Name = "Orders" },
            _roots, _files, _demo, CancellationToken.None);

        var mutation = Value<LinkedCollectionMutationResult>(created);
        var collection = Assert.Single(mutation.Root.Collections);
        Assert.Equal("Orders", collection.Name);
        Assert.Equal(mutation.CollectionId, collection.Id);
        Assert.True(File.Exists(Path.Combine(ApiRoot, "collections", "orders", "collection.json")));
    }

    [Fact]
    public async Task CollectionEndpoints_UnknownIds_Return404()
    {
        var create = await LinkedRootsEndpoints.CreateCollectionAsync(
            "nope", new CreateLinkedNodeRequest { Name = "x" },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(create));

        var root = await AddRootAsync();
        var rename = await LinkedRootsEndpoints.RenameCollectionAsync(
            root.Id, "nope", new RenameLinkedNodeRequest { Name = "x" },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(rename));
    }

    [Fact]
    public async Task RenameAndDeleteCollection_RenameAndRemoveDirectory()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        var oldDir = Path.Combine(ApiRoot, "collections", "orders");

        var renamed = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.RenameCollectionAsync(
            root.Id, collectionId, new RenameLinkedNodeRequest { Name = "Order APIs" },
            _roots, _files, _demo, CancellationToken.None));
        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(Path.Combine(ApiRoot, "collections", "order-apis")));
        Assert.Equal("Order APIs", Assert.Single(renamed.Collections).Name);

        // The directory move changes the stable id — use the fresh one.
        var freshId = Assert.Single(renamed.Collections).Id;
        var afterDelete = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.DeleteCollectionAsync(
            root.Id, freshId, _roots, _files, _demo, CancellationToken.None));
        Assert.Empty(afterDelete.Collections);
        Assert.False(Directory.Exists(Path.Combine(ApiRoot, "collections", "order-apis")));
    }

    // ── Requests ─────────────────────────────────────────────────────────────

    private async Task<(string RootId, string CollectionId, LinkedRequestMutationResult Created)> CreateRequestAsync(
        string requestName = "Get Order", string? parentFolderId = null)
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        var created = await LinkedRootsEndpoints.CreateRequestAsync(
            root.Id, collectionId,
            new CreateLinkedRequestRequest
            {
                Name = requestName,
                ParentFolderId = parentFolderId,
                Request = new HttpRequestEntry { Method = ApiRequestMethod.Get, Url = "/orders/1" },
            },
            _roots, _files, _demo, CancellationToken.None);
        return (root.Id, collectionId, Value<LinkedRequestMutationResult>(created));
    }

    [Fact]
    public async Task CreateRequestAsync_WritesFile_AndReturnsFileState()
    {
        var (_, _, created) = await CreateRequestAsync();

        var requestPath = Path.Combine(ApiRoot, "collections", "orders", "get-order.swebreq.json");
        Assert.True(File.Exists(requestPath));
        Assert.Equal(requestPath, created.RequestFilePath);
        Assert.False(string.IsNullOrWhiteSpace(created.ContentStamp));
        Assert.Contains(created.Root.RequestFiles, f => f.RequestId == created.RequestId);
    }

    [Fact]
    public async Task CreateRequestAsync_InsideFolder_WritesUnderFolderDirectory()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        var rootId = root.Id;
        await LinkedRootsEndpoints.CreateFolderAsync(
            rootId, collectionId, new CreateLinkedFolderRequest { Name = "Admin" },
            _roots, _files, _demo, CancellationToken.None);
        var folderId = Assert.Single(
            (await ReloadAsync(rootId)).Collections[0].Nodes,
            n => n.Type == ApiCollectionNodeType.Folder).Id;

        var created = await LinkedRootsEndpoints.CreateRequestAsync(
            rootId, collectionId,
            new CreateLinkedRequestRequest { Name = "List", ParentFolderId = folderId },
            _roots, _files, _demo, CancellationToken.None);
        var mutation = Value<LinkedRequestMutationResult>(created);

        Assert.True(File.Exists(Path.Combine(ApiRoot, "collections", "orders", "admin", "list.swebreq.json")));
        var folder = Assert.Single(mutation.Root.Collections[0].Nodes, n => n.Type == ApiCollectionNodeType.Folder);
        Assert.Single(folder.Children);
    }

    [Fact]
    public async Task SaveRequestAsync_Persists_AndRotateStamp()
    {
        var (rootId, collectionId, created) = await CreateRequestAsync();

        var saved = await LinkedRootsEndpoints.SaveRequestAsync(
            rootId, collectionId, created.RequestId,
            new SaveLinkedRequestRequest
            {
                ContentStamp = created.ContentStamp,
                Request = new HttpRequestEntry { Name = "Get Order", Method = ApiRequestMethod.Post, Url = "/orders/2" },
            },
            _roots, _files, _demo, CancellationToken.None);

        var mutation = Value<LinkedRequestMutationResult>(saved);
        Assert.NotEqual(created.ContentStamp, mutation.ContentStamp);
        var json = await File.ReadAllTextAsync(mutation.RequestFilePath);
        Assert.Contains("/orders/2", json);
        Assert.Contains("Post", json);
    }

    [Fact]
    public async Task SaveRequestAsync_WrongStamp_Returns409_WithCurrentStamp()
    {
        var (rootId, collectionId, created) = await CreateRequestAsync();

        var conflict = await LinkedRootsEndpoints.SaveRequestAsync(
            rootId, collectionId, created.RequestId,
            new SaveLinkedRequestRequest
            {
                ContentStamp = "stale-stamp",
                Request = new HttpRequestEntry { Name = "Get Order", Method = ApiRequestMethod.Get, Url = "/changed" },
            },
            _roots, _files, _demo, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(conflict));
        var payload = JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<IValueHttpResult>(conflict).Value);
        Assert.Equal(created.ContentStamp, payload.GetProperty("currentContentStamp").GetString());
        Assert.Equal(created.RequestFilePath, payload.GetProperty("requestFilePath").GetString());
        // The file on disk is untouched by the rejected save.
        Assert.Contains("/orders/1", await File.ReadAllTextAsync(created.RequestFilePath));
    }

    [Fact]
    public async Task SaveRequestAsync_UnknownRequest_Returns404()
    {
        var (rootId, collectionId, _) = await CreateRequestAsync();

        var result = await LinkedRootsEndpoints.SaveRequestAsync(
            rootId, collectionId, "missing",
            new SaveLinkedRequestRequest { Request = new HttpRequestEntry { Name = "x" } },
            _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task DeleteRequestAsync_RemovesFile()
    {
        var (rootId, collectionId, created) = await CreateRequestAsync();

        var after = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.DeleteRequestAsync(
            rootId, collectionId, created.RequestId, _roots, _files, _demo, CancellationToken.None));

        Assert.False(File.Exists(created.RequestFilePath));
        Assert.Empty(Assert.Single(after.Collections).Nodes);
    }

    // ── Folders, move, order ─────────────────────────────────────────────────

    [Fact]
    public async Task FolderRenameAndDelete_MoveDirectoryOnDisk()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        var folderDir = Path.Combine(ApiRoot, "collections", "orders", "admin");
        await LinkedRootsEndpoints.CreateFolderAsync(
            root.Id, collectionId, new CreateLinkedFolderRequest { Name = "Admin" },
            _roots, _files, _demo, CancellationToken.None);
        var folderId = Assert.Single((await ReloadAsync(root.Id)).Collections[0].Nodes).Id;

        var renamed = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.RenameFolderAsync(
            root.Id, collectionId, folderId, new RenameLinkedNodeRequest { Name = "Backoffice" },
            _roots, _files, _demo, CancellationToken.None));
        Assert.False(Directory.Exists(folderDir));
        Assert.True(Directory.Exists(Path.Combine(ApiRoot, "collections", "orders", "backoffice")));

        var freshFolderId = Assert.Single(renamed.Collections[0].Nodes).Id;
        var afterDelete = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.DeleteFolderAsync(
            root.Id, collectionId, freshFolderId, _roots, _files, _demo, CancellationToken.None));
        Assert.Empty(Assert.Single(afterDelete.Collections).Nodes);
        Assert.False(Directory.Exists(Path.Combine(ApiRoot, "collections", "orders", "backoffice")));
    }

    [Fact]
    public async Task MoveNodeAsync_RequestIntoFolder_MovesFile()
    {
        var (rootId, collectionId, created) = await CreateRequestAsync();
        await LinkedRootsEndpoints.CreateFolderAsync(
            rootId, collectionId, new CreateLinkedFolderRequest { Name = "Admin" },
            _roots, _files, _demo, CancellationToken.None);
        var reloaded = await ReloadAsync(rootId);
        var folderId = Assert.Single(
            reloaded.Collections[0].Nodes, n => n.Type == ApiCollectionNodeType.Folder).Id;
        var nodeId = Assert.Single(
            reloaded.Collections[0].Nodes, n => n.Type == ApiCollectionNodeType.Request).Id;

        var moved = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.MoveNodeAsync(
            rootId, collectionId, nodeId, new MoveLinkedNodeRequest { ParentFolderId = folderId },
            _roots, _files, _demo, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(ApiRoot, "collections", "orders", "get-order.swebreq.json")));
        Assert.True(File.Exists(Path.Combine(ApiRoot, "collections", "orders", "admin", "get-order.swebreq.json")));
        var folder = Assert.Single(moved.Collections[0].Nodes, n => n.Type == ApiCollectionNodeType.Folder);
        Assert.Single(folder.Children, n => n.Type == ApiCollectionNodeType.Request);
    }

    [Fact]
    public async Task SetChildOrderAsync_ReordersTopLevelNodes()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");
        var rootId = root.Id;
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            await LinkedRootsEndpoints.CreateRequestAsync(
                rootId, collectionId, new CreateLinkedRequestRequest { Name = name },
                _roots, _files, _demo, CancellationToken.None);
        }

        var before = await ReloadAsync(rootId);
        Assert.Equal("Alpha", before.Collections[0].Nodes[0].Name);
        var reversed = before.Collections[0].Nodes.Select(n => n.Id).Reverse().ToList();

        var reordered = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.SetChildOrderAsync(
            rootId, collectionId,
            new SetLinkedChildOrderRequest { OrderedChildIds = reversed },
            _roots, _files, _demo, CancellationToken.None));

        Assert.Equal("Beta", reordered.Collections[0].Nodes[0].Name);
        var manifest = await File.ReadAllTextAsync(Path.Combine(ApiRoot, "collections", "orders", "collection.json"));
        Assert.Contains("beta.swebreq.json", manifest);
    }

    // ── Environments ─────────────────────────────────────────────────────────

    private static SaveLinkedEnvironmentRequest EnvRequest(string name, string? collectionId = null) => new()
    {
        CollectionId = collectionId,
        Environment = new ApiEnvironment
        {
            Name = name,
            Variables = [new EnvironmentVariable { Key = "baseUrl", Value = "https://dev.example.com" }],
        },
    };

    [Fact]
    public async Task Environments_CreateUpdateDelete_RoundTripsOnDisk()
    {
        var root = await AddRootAsync();

        var created = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.CreateEnvironmentAsync(
            root.Id, EnvRequest("dev"), _roots, _files, _demo, CancellationToken.None));
        var env = Assert.Single(created.Environments);
        Assert.Equal("dev", env.Name);
        var envFile = Assert.Single(created.EnvironmentFiles);
        Assert.True(File.Exists(envFile.EnvironmentFilePath));

        var updated = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.UpdateEnvironmentAsync(
            root.Id, env.Id, EnvRequest("dev-renamed"), _roots, _files, _demo, CancellationToken.None));
        Assert.Equal("dev-renamed", Assert.Single(updated.Environments).Name);
        Assert.Contains("dev-renamed", await File.ReadAllTextAsync(envFile.EnvironmentFilePath));

        var afterDelete = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.DeleteEnvironmentAsync(
            root.Id, env.Id, _roots, _files, _demo, CancellationToken.None));
        Assert.Empty(afterDelete.Environments);
        Assert.False(File.Exists(envFile.EnvironmentFilePath));
    }

    [Fact]
    public async Task CreateEnvironmentAsync_WithCollectionId_WritesCollectionScopedFile()
    {
        var (collectionId, root) = await AddCollectionAsync("Orders");

        var created = Value<LinkedCollectionRootSummary>(await LinkedRootsEndpoints.CreateEnvironmentAsync(
            root.Id, EnvRequest("orders-env", collectionId), _roots, _files, _demo, CancellationToken.None));

        var env = Assert.Single(created.Environments);
        Assert.Equal(collectionId, env.CollectionId);
        Assert.True(File.Exists(Path.Combine(ApiRoot, "collections", "orders", "environments", "orders-env.swebenv.json")));
    }

    [Fact]
    public async Task EnvironmentEndpoints_UnknownEnv_Returns404()
    {
        var root = await AddRootAsync();

        var update = await LinkedRootsEndpoints.UpdateEnvironmentAsync(
            root.Id, "nope", EnvRequest("x"), _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(update));

        var delete = await LinkedRootsEndpoints.DeleteEnvironmentAsync(
            root.Id, "nope", _roots, _files, _demo, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(delete));
    }

    // ── Store merge + import target ──────────────────────────────────────────

    [Fact]
    public async Task GetCollectionsStore_MergesLinkedRoots()
    {
        var (collectionId, _) = await AddCollectionAsync("Orders");
        var repo = new CollectionRepository();

        var result = await ConfigEndpoints.GetCollectionsStore(
            repo, _demo, _roots, _files, CancellationToken.None);
        var store = Value<CollectionsStoreResponse>(result);

        var linked = Assert.Single(store.LinkedRoots!);
        Assert.True(linked.IsValid);
        Assert.Contains(linked.Collections, c => c.Id == collectionId && c.Name == "Orders");
        Assert.Equal(ApiRoot, linked.ApiRootPath);
    }

    [Fact]
    public async Task GetCollectionsStore_InvalidRoot_StillListedWithDiagnostics()
    {
        var root = await AddRootAsync();
        Directory.Delete(ApiRoot, recursive: true);

        var result = await ConfigEndpoints.GetCollectionsStore(
            new CollectionRepository(), _demo, _roots, _files, CancellationToken.None);
        var store = Value<CollectionsStoreResponse>(result);

        var linked = Assert.Single(store.LinkedRoots!);
        Assert.Equal(root.Id, linked.Id);
        Assert.False(linked.IsValid);
        Assert.NotEmpty(linked.Diagnostics);
        Assert.Empty(linked.Collections);
    }

    [Fact]
    public async Task ImportCollectionAsync_WithLinkedRootId_WritesIntoLinkedRoot()
    {
        var root = await AddRootAsync();
        var collections = new CollectionRepository();
        var importer = new CollectionImportService(
            collections, new EnvironmentRepository(),
            new SwebKitCollectionImporter(), new PostmanCollectionImporter(),
            new SwebKitEnvironmentImporter(), new BrunoFolderImporter(), _files);
        var bundle = """
            {
              "schemaVersion": 1,
              "collection": {
                "id": "c1",
                "name": "Imported",
                "nodes": [
                  { "id": "n1", "type": "Request", "name": "Ping",
                    "request": { "id": "r1", "name": "Ping", "method": "Get", "url": "/ping" } }
                ]
              }
            }
            """;

        var result = await ConfigEndpoints.ImportCollectionAsync(
            new ConfigEndpoints.ImportCollectionRequest
            {
                PayloadBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(bundle)),
                LinkedRootId = root.Id,
            },
            importer, _demo, _roots, _files, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        // The import lands on disk in the linked root — never in the internal store.
        Assert.Empty(collections.Collections);
        var requestFile = Path.Combine(ApiRoot, "collections", "imported", "ping.swebreq.json");
        Assert.True(File.Exists(requestFile));
        var reloaded = await ReloadAsync(root.Id);
        Assert.Contains(reloaded.Collections, c => c.Name == "Imported");
    }

    [Fact]
    public async Task ImportCollectionAsync_UnknownLinkedRoot_Returns404()
    {
        var importer = new CollectionImportService(
            new CollectionRepository(), new EnvironmentRepository(),
            new SwebKitCollectionImporter(), new PostmanCollectionImporter(),
            new SwebKitEnvironmentImporter(), new BrunoFolderImporter(), _files);

        var result = await ConfigEndpoints.ImportCollectionAsync(
            new ConfigEndpoints.ImportCollectionRequest { PayloadBase64 = "e30=", LinkedRootId = "nope" },
            importer, _demo, _roots, _files, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    // ── Demo mode ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DemoMode_RejectsEveryEndpoint()
    {
        _demo.IsDemoMode = true;
        var ct = CancellationToken.None;

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.GetRootsAsync(_roots, _files, _demo, ct)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.CreateRootAsync(new CreateLinkedRootRequest { Path = _rootDir }, _roots, _files, _demo, ct)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.UpdateRootAsync("r", new UpdateLinkedRootRequest(), _roots, _files, _demo, ct)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.DeleteRootAsync("r", _roots, _demo)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.ReloadRootAsync("r", _roots, _files, _demo, ct)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.CreateCollectionAsync("r", new CreateLinkedNodeRequest { Name = "x" }, _roots, _files, _demo, ct)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(
            await LinkedRootsEndpoints.CreateEnvironmentAsync("r", EnvRequest("x"), _roots, _files, _demo, ct)));

        // Demo mode never writes — nothing registered, nothing on disk.
        Assert.Empty(_roots.Roots);
        Assert.False(Directory.Exists(ApiRoot));
    }
}
