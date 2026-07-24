using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="BrunoCollectionExporter.ExportToFolderAsync"/> — filesystem export used by
/// "Export to Bruno folder". Covers orphan cleanup and same-name sibling disambiguation.
/// </summary>
public sealed class BrunoCollectionExporterFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-bruno-export-tests", Guid.NewGuid().ToString("N"));
    private readonly BrunoCollectionExporter _exporter = new();

    public BrunoCollectionExporterFolderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Export_WritesManifestAndRequestFiles()
    {
        var collection = Collection("My API", Node(Request("r1", "Get Users", "https://api.test/users")));

        await _exporter.ExportToFolderAsync(collection, [], _root);

        Assert.True(File.Exists(Path.Combine(_root, "bruno.json")));
        Assert.True(File.Exists(Path.Combine(_root, "Get Users.bru")));
    }

    [Fact]
    public async Task Export_RemovesOrphanedBruFilesFromPreviousExport()
    {
        // Simulate a prior export that contained a request later deleted in SwebKit.
        File.WriteAllText(Path.Combine(_root, "Stale Request.bru"), "meta {\n  name: Stale Request\n}\n");
        Directory.CreateDirectory(Path.Combine(_root, "Old Folder"));
        File.WriteAllText(Path.Combine(_root, "Old Folder", "Also Stale.bru"), "meta {\n  name: Also Stale\n}\n");

        var collection = Collection("My API", Node(Request("r1", "Kept", "https://api.test/kept")));

        await _exporter.ExportToFolderAsync(collection, [], _root);

        Assert.False(File.Exists(Path.Combine(_root, "Stale Request.bru")));
        Assert.False(File.Exists(Path.Combine(_root, "Old Folder", "Also Stale.bru")));
        Assert.True(File.Exists(Path.Combine(_root, "Kept.bru")));
    }

    [Fact]
    public async Task Export_LeavesNonBruUserFilesUntouched()
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), "keep me");
        var collection = Collection("My API", Node(Request("r1", "Get", "https://api.test")));

        await _exporter.ExportToFolderAsync(collection, [], _root);

        Assert.True(File.Exists(Path.Combine(_root, "README.md")));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public async Task Export_DisambiguatesSameNamedSiblingRequests()
    {
        var collection = Collection("My API",
            Node(Request("r1", "Duplicate", "https://api.test/a")),
            Node(Request("r2", "Duplicate", "https://api.test/b")));

        await _exporter.ExportToFolderAsync(collection, [], _root);

        // Both requests survive as distinct files rather than one overwriting the other.
        Assert.True(File.Exists(Path.Combine(_root, "Duplicate.bru")));
        Assert.True(File.Exists(Path.Combine(_root, "Duplicate-2.bru")));
        Assert.Equal(2, Directory.GetFiles(_root, "Duplicate*.bru").Length);
    }

    [Fact]
    public async Task Export_WritesEnvironmentsIntoEnvironmentsFolder()
    {
        var collection = Collection("My API", Node(Request("r1", "Get", "https://api.test")));
        var env = new ApiEnvironment
        {
            Id = "e1",
            Name = "dev",
            Variables = [new EnvironmentVariable { Key = "BASE_URL", Value = "https://dev.test", IsEnabled = true }],
        };

        await _exporter.ExportToFolderAsync(collection, [env], _root);

        var envFile = Path.Combine(_root, "environments", "dev.bru");
        Assert.True(File.Exists(envFile));
        Assert.Contains("BASE_URL", await File.ReadAllTextAsync(envFile));
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────────

    private static ApiCollection Collection(string name, params ApiCollectionNode[] nodes) =>
        new() { Id = "c1", Name = name, Nodes = [.. nodes] };

    private static ApiCollectionNode Node(HttpRequestEntry request) =>
        new() { Id = "n-" + request.Id, Type = ApiCollectionNodeType.Request, Name = request.Name, Request = request };

    private static HttpRequestEntry Request(string id, string name, string url) =>
        new() { Id = id, Name = name, Method = ApiRequestMethod.Get, Url = url };

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
