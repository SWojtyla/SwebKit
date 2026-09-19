using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Coverage for <c>LinkedCollectionFileService.SyncCollectionAsync</c> / environment sync —
/// the whole-collection reconcile the sidecar's store PUT partitions into. Identity is carried
/// by persisted file Ids so renames and moves keep the link, and content stamps guard external
/// edits from being clobbered.
/// </summary>
public sealed class LinkedCollectionSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-sync-tests", Guid.NewGuid().ToString("N"));
    private readonly LinkedCollectionFileService _fileService = new(new LinkedGitService());

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

    [Fact]
    public async Task SyncCollection_NewCollection_WritesDirectoryManifestAndRequestFiles()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order")));

        var result = await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);

        Assert.True(result.IsSuccess);
        Assert.Single(result.WrittenRequests);
        var collectionDir = Path.Combine(apiRoot, "collections", "orders");
        Assert.True(File.Exists(Path.Combine(collectionDir, "collection.json")));
        var requestFile = Assert.Single(Directory.GetFiles(collectionDir, "*.swebreq.json"));
        Assert.Equal("get-order.swebreq.json", Path.GetFileName(requestFile));
        Assert.Contains("\"id\": \"req-1\"", await File.ReadAllTextAsync(requestFile));
    }

    [Fact]
    public async Task SyncCollection_RenamedRequest_RenamesFileAndKeepsPersistedId()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);

        // Reload captures the persisted id + content stamps the next sync checks against.
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var renamed = Collection("col-1", "Orders", RequestNode(Request("req-1", "Fetch Order")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, renamed, loaded.RequestFiles);

        Assert.True(result.IsSuccess);
        var collectionDir = Path.Combine(apiRoot, "collections", "orders");
        Assert.False(File.Exists(Path.Combine(collectionDir, "get-order.swebreq.json")));
        var newFile = Path.Combine(collectionDir, "fetch-order.swebreq.json");
        Assert.True(File.Exists(newFile));
        // Identity survives the rename — the file still belongs to req-1.
        Assert.Contains("\"id\": \"req-1\"", await File.ReadAllTextAsync(newFile));
    }

    [Fact]
    public async Task SyncCollection_DeletedRequest_RemovesFileButKeepsForeignFiles()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders",
            RequestNode(Request("req-1", "Get Order")),
            RequestNode(Request("req-2", "List Orders")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        // A file SwebKit never loaded (teammate's uncommitted work, or mid-merge) must survive.
        var collectionDir = Path.Combine(apiRoot, "collections", "orders");
        var foreignFile = Path.Combine(collectionDir, "teammates-request.swebreq.json");
        await File.WriteAllTextAsync(foreignFile, """{ "method": "Get", "url": "/theirs" }""");

        var trimmed = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, trimmed, loaded.RequestFiles);

        Assert.True(result.IsSuccess);
        Assert.Single(result.RemovedRequests);
        Assert.False(File.Exists(Path.Combine(collectionDir, "list-orders.swebreq.json")));
        Assert.True(File.Exists(foreignFile));
    }

    [Fact]
    public async Task SyncCollection_ExternalEdit_ReturnsConflictAndWritesNothing()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/1")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var file = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        await File.WriteAllTextAsync(file, """{ "method": "Get", "url": "/orders/external-edit" }""");

        var incoming = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/mine")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, incoming, loaded.RequestFiles);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Conflicts);
        Assert.Contains("external-edit", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task SyncCollection_ExternalEditPlusCollectionRename_StillConflicts()
    {
        // Regression: the stamp check must run against the paths captured at load, before the
        // collection directory is renamed — otherwise a rename silently bypasses conflict
        // detection and clobbers the external edit.
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/1")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var file = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        await File.WriteAllTextAsync(file, """{ "method": "Get", "url": "/orders/external-edit" }""");

        var renamed = Collection("col-1", "Order Service", RequestNode(Request("req-1", "Get Order", "/orders/mine")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, renamed, loaded.RequestFiles);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Conflicts);
        Assert.Contains("external-edit", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task SyncCollection_ExternalEdit_WithForce_Overwrites()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/1")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var file = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        await File.WriteAllTextAsync(file, """{ "method": "Get", "url": "/orders/external-edit" }""");

        var incoming = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order", "/orders/mine")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, incoming, loaded.RequestFiles, force: true);

        Assert.True(result.IsSuccess);
        Assert.Contains("/orders/mine", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task SyncCollection_MovedIntoFolder_MovesFileAndCleansEmptyDirectory()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var moved = Collection("col-1", "Orders", new ApiCollectionNode
        {
            Id = "folder-1",
            Type = ApiCollectionNodeType.Folder,
            Name = "Admin",
            Children = [RequestNode(Request("req-1", "Get Order"))],
        });
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, moved, loaded.RequestFiles);

        Assert.True(result.IsSuccess);
        var collectionDir = Path.Combine(apiRoot, "collections", "orders");
        Assert.False(File.Exists(Path.Combine(collectionDir, "get-order.swebreq.json")));
        Assert.True(File.Exists(Path.Combine(collectionDir, "admin", "get-order.swebreq.json")));
        Assert.True(File.Exists(Path.Combine(collectionDir, "admin", "folder.json")));
    }

    [Fact]
    public async Task SyncCollection_RenamedCollection_RenamesDirectory()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = Collection("col-1", "Orders", RequestNode(Request("req-1", "Get Order")));
        await _fileService.SyncCollectionAsync(apiRoot, known: null, collection, []);
        var loaded = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Path = _root });
        var known = Assert.Single(loaded.Collections);

        var renamed = Collection("col-1", "Order Service", RequestNode(Request("req-1", "Get Order")));
        var result = await _fileService.SyncCollectionAsync(apiRoot, known, renamed, loaded.RequestFiles);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(apiRoot, "collections", "orders")));
        var newDir = Path.Combine(apiRoot, "collections", "order-service");
        Assert.True(Directory.Exists(newDir));
        // The manifest keeps the stable id — a directory rename must not change identity.
        Assert.Contains("\"id\": \"col-1\"", await File.ReadAllTextAsync(Path.Combine(newDir, "collection.json")));
    }

    [Fact]
    public async Task SyncEnvironment_Rename_MovesFile()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envsDir = Path.Combine(apiRoot, "environments");
        var env = new ApiEnvironment { Id = "env-1", Name = "Dev", Variables = [] };
        var written = await _fileService.SyncEnvironmentAsync(apiRoot, envsDir, env);

        var renamed = new ApiEnvironment { Id = "env-1", Name = "Dev Shared", Variables = [] };
        await _fileService.SyncEnvironmentAsync(apiRoot, envsDir, renamed, knownFilePath: written);

        Assert.False(File.Exists(written));
        Assert.True(File.Exists(Path.Combine(envsDir, "dev-shared.swebenv.json")));
    }

    [Fact]
    public async Task SyncEnvironment_IdPersistedInFile()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envsDir = Path.Combine(apiRoot, "environments");
        var env = new ApiEnvironment { Id = "env-42", Name = "Staging", Variables = [] };

        var written = await _fileService.SyncEnvironmentAsync(apiRoot, envsDir, env);

        Assert.Contains("env-42", await File.ReadAllTextAsync(written));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
