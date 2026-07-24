using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="BrunoSyncService"/> — best-effort .bru write-back for linked collections.
/// Exercises save/rename/delete against a real temp Bruno folder, with emphasis on folder-scoped
/// matching so same-named requests in different folders never clobber each other.
/// </summary>
public sealed class BrunoSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-bruno-sync-tests", Guid.NewGuid().ToString("N"));
    private readonly BrunoSyncService _sync = new();

    public BrunoSyncServiceTests() => Directory.CreateDirectory(_root);

    // ── Save ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_OverwritesExistingFileMatchedByMetaName()
    {
        WriteBru(_root, "get-users.bru", "Get Users", "https://old.test");
        var request = Request("r1", "Get Users", "https://new.test");
        var collection = Collection(Node(request));

        var error = await _sync.SyncRequestSaveAsync(_root, collection, request);

        Assert.Null(error);
        // Same file reused (matched by meta name), not a duplicate.
        Assert.Single(Directory.GetFiles(_root, "*.bru"));
        Assert.Contains("https://new.test", await File.ReadAllTextAsync(Path.Combine(_root, "get-users.bru")));
    }

    [Fact]
    public async Task Save_CreatesFileInResolvedFolder_WhenNoneExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Admin"));
        var request = Request("r1", "Purge Cache", "https://api.test/purge");
        var collection = Collection(Folder("Admin", request));

        var error = await _sync.SyncRequestSaveAsync(_root, collection, request);

        Assert.Null(error);
        var created = Path.Combine(_root, "Admin", "Purge Cache.bru");
        Assert.True(File.Exists(created));
        Assert.Equal("Purge Cache", await ReadMetaNameAsync(created));
    }

    [Fact]
    public async Task Save_DisambiguatesSameNameAcrossFolders_UpdatingOnlyTheRequestsOwnFolder()
    {
        // Two requests named "Health" in different folders, each with its own .bru file.
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "Beta"));
        WriteBru(Path.Combine(_root, "Alpha"), "Health.bru", "Health", "https://alpha.test/health");
        WriteBru(Path.Combine(_root, "Beta"), "Health.bru", "Health", "https://beta.test/health");

        var betaRequest = Request("beta-health", "Health", "https://beta.test/health/v2");
        var collection = Collection(
            Folder("Alpha", Request("alpha-health", "Health", "https://alpha.test/health")),
            Folder("Beta", betaRequest));

        var error = await _sync.SyncRequestSaveAsync(_root, collection, betaRequest);

        Assert.Null(error);
        // Beta's file updated; Alpha's left untouched.
        Assert.Contains("https://beta.test/health/v2", await File.ReadAllTextAsync(Path.Combine(_root, "Beta", "Health.bru")));
        Assert.Contains("https://alpha.test/health", await File.ReadAllTextAsync(Path.Combine(_root, "Alpha", "Health.bru")));
        Assert.DoesNotContain("v2", await File.ReadAllTextAsync(Path.Combine(_root, "Alpha", "Health.bru")));
    }

    [Fact]
    public async Task Save_ReturnsError_WhenFolderMissing()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        var request = Request("r1", "Get", "https://api.test");

        var error = await _sync.SyncRequestSaveAsync(missing, Collection(Node(request)), request);

        Assert.NotNull(error);
        Assert.Contains("not found", error);
    }

    // ── Rename ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rename_MovesFileAndUpdatesMetaName()
    {
        WriteBru(_root, "old-name.bru", "Old Name", "https://api.test");
        var renamed = Request("r1", "New Name", "https://api.test");
        var collection = Collection(Node(renamed));

        var error = await _sync.SyncRequestRenameAsync(_root, collection, renamed, oldName: "Old Name");

        Assert.Null(error);
        Assert.False(File.Exists(Path.Combine(_root, "old-name.bru")));
        var newFile = Path.Combine(_root, "New Name.bru");
        Assert.True(File.Exists(newFile));
        Assert.Equal("New Name", await ReadMetaNameAsync(newFile));
    }

    [Fact]
    public async Task Rename_CreatesFile_WhenOldNameNotFound()
    {
        var renamed = Request("r1", "Fresh", "https://api.test");
        var collection = Collection(Node(renamed));

        var error = await _sync.SyncRequestRenameAsync(_root, collection, renamed, oldName: "Nonexistent");

        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(_root, "Fresh.bru")));
    }

    // ── Delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesFileMatchedByMetaName()
    {
        WriteBru(_root, "get-users.bru", "Get Users", "https://api.test");
        var request = Request("r1", "Get Users", "https://api.test");

        var error = await _sync.SyncRequestDeleteAsync(_root, Collection(Node(request)), request);

        Assert.Null(error);
        Assert.False(File.Exists(Path.Combine(_root, "get-users.bru")));
    }

    [Fact]
    public async Task Delete_OnlyRemovesTheRequestsOwnFile_WhenNameRepeatsAcrossFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "Beta"));
        WriteBru(Path.Combine(_root, "Alpha"), "Health.bru", "Health", "https://alpha.test");
        WriteBru(Path.Combine(_root, "Beta"), "Health.bru", "Health", "https://beta.test");

        var betaRequest = Request("beta-health", "Health", "https://beta.test");
        var collection = Collection(
            Folder("Alpha", Request("alpha-health", "Health", "https://alpha.test")),
            Folder("Beta", betaRequest));

        var error = await _sync.SyncRequestDeleteAsync(_root, collection, betaRequest);

        Assert.Null(error);
        Assert.False(File.Exists(Path.Combine(_root, "Beta", "Health.bru")));
        Assert.True(File.Exists(Path.Combine(_root, "Alpha", "Health.bru")));
    }

    [Fact]
    public async Task Delete_SkipsStructuralAndEnvironmentFiles()
    {
        // A meta.bru with a matching name must never be treated as a request file.
        File.WriteAllText(Path.Combine(_root, "meta.bru"), "meta {\n  name: Health\n  seq: 1\n}\n");
        Directory.CreateDirectory(Path.Combine(_root, "environments"));
        WriteBru(Path.Combine(_root, "environments"), "Health.bru", "Health", "https://env.test");

        var request = Request("r1", "Health", "https://api.test");

        var error = await _sync.SyncRequestDeleteAsync(_root, Collection(Node(request)), request);

        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(_root, "meta.bru")));
        Assert.True(File.Exists(Path.Combine(_root, "environments", "Health.bru")));
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────────

    private static ApiCollection Collection(params ApiCollectionNode[] nodes) =>
        new() { Id = "c1", Name = "Test", Nodes = [.. nodes] };

    private static ApiCollectionNode Node(HttpRequestEntry request) =>
        new() { Id = "n-" + request.Id, Type = ApiCollectionNodeType.Request, Name = request.Name, Request = request };

    private static ApiCollectionNode Folder(string name, params HttpRequestEntry[] requests) =>
        new()
        {
            Id = "f-" + name,
            Type = ApiCollectionNodeType.Folder,
            Name = name,
            Children = [.. requests.Select(Node)],
        };

    private static HttpRequestEntry Request(string id, string name, string url) =>
        new() { Id = id, Name = name, Method = ApiRequestMethod.Get, Url = url };

    private static void WriteBru(string dir, string fileName, string metaName, string url) =>
        File.WriteAllText(Path.Combine(dir, fileName), $$"""
            meta {
              name: {{metaName}}
              type: http
              seq: 1
            }

            get {
              url: {{url}}
            }
            """);

    private static async Task<string?> ReadMetaNameAsync(string bruFilePath)
    {
        foreach (var line in await File.ReadAllLinesAsync(bruFilePath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                return trimmed["name:".Length..].Trim();
        }
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
