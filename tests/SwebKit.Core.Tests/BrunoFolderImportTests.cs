using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="BrunoFolderImporter"/> — folder-tree parsing, multi-collection "workspace"
/// flattening, and per-collection environment discovery.
/// </summary>
public sealed class BrunoFolderImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-bruno-tests", Guid.NewGuid().ToString("N"));
    private readonly BrunoFolderImporter _importer = new();

    // ── Multi-collection workspace flattening ──────────────────────────────────

    [Fact]
    public async Task Import_WorkspaceFolder_ImportsEachChildAsSeparateCollection()
    {
        // A "workspace" folder that is NOT itself a collection (no bruno.json) but groups two.
        WriteCollection("Alpha APIs", "alpha");
        WriteCollection("Beta APIs", "beta");

        var result = await _importer.ImportFromFolderAsync(_root);

        Assert.Equal(2, result.Collections.Count);
        Assert.Contains(result.Collections, c => c.Name == "Alpha APIs");
        Assert.Contains(result.Collections, c => c.Name == "Beta APIs");
        // No wrapper collection named after the workspace folder.
        Assert.DoesNotContain(result.Collections, c => c.Name == Path.GetFileName(_root));
    }

    [Fact]
    public async Task Import_WorkspaceFolder_ImportsEachChildsEnvironments()
    {
        WriteCollection("Alpha APIs", "alpha", envName: "dev", envVar: ("BASE_URL", "https://alpha.dev"));
        WriteCollection("Beta APIs", "beta", envName: "prod", envVar: ("BASE_URL", "https://beta.prod"));

        var result = await _importer.ImportFromFolderAsync(_root);

        Assert.Equal(2, result.Environments.Count);
        var dev = Assert.Single(result.Environments, e => e.Name == "dev");
        Assert.Contains(dev.Variables, v => v.Key == "BASE_URL" && v.Value == "https://alpha.dev");
        var prod = Assert.Single(result.Environments, e => e.Name == "prod");
        Assert.Contains(prod.Variables, v => v.Key == "BASE_URL" && v.Value == "https://beta.prod");

        // Each environment is scoped to its own collection.
        var alpha = Assert.Single(result.Collections, c => c.Name == "Alpha APIs");
        var beta = Assert.Single(result.Collections, c => c.Name == "Beta APIs");
        Assert.Equal(alpha.Id, dev.CollectionId);
        Assert.Equal(beta.Id, prod.CollectionId);
    }

    [Fact]
    public async Task Import_SingleCollectionFolder_ImportsOneCollection()
    {
        // Point the importer straight at a collection folder (has its own bruno.json).
        WriteCollection("Alpha APIs", "alpha", envName: "dev", envVar: ("BASE_URL", "https://alpha.dev"));
        var collectionFolder = Path.Combine(_root, "alpha");

        var result = await _importer.ImportFromFolderAsync(collectionFolder);

        var collection = Assert.Single(result.Collections);
        Assert.Equal("Alpha APIs", collection.Name);
        Assert.Single(result.Environments);
        Assert.True(result.RequestCount >= 1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void WriteCollection(string name, string slug, string? envName = null, (string Key, string Value)? envVar = null)
    {
        var collectionDir = Path.Combine(_root, slug);
        Directory.CreateDirectory(collectionDir);
        File.WriteAllText(Path.Combine(collectionDir, "bruno.json"), $$"""
            {
              "version": "1",
              "name": "{{name}}",
              "type": "collection"
            }
            """);

        File.WriteAllText(Path.Combine(collectionDir, "get-users.bru"), """
            meta {
              name: Get Users
              type: http
              seq: 1
            }

            get {
              url: https://api.test/users
            }
            """);

        if (envName is not null && envVar is { } pair)
        {
            var envDir = Path.Combine(collectionDir, "environments");
            Directory.CreateDirectory(envDir);
            File.WriteAllText(Path.Combine(envDir, $"{envName}.bru"), $$"""
                vars {
                  {{pair.Key}}: {{pair.Value}}
                }
                """);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
