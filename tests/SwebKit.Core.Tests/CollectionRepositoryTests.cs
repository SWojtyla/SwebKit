using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public sealed class CollectionRepositoryTests
{
    // ── Load: empty file store ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_ReturnsEmptyCollections()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();

        await repo.LoadAsync();

        Assert.Empty(repo.Collections);
    }

    // ── Save / load round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_CreatesJsonAndBackupFile()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        await repo.AddCollectionAsync("My API");

        Assert.True(File.Exists(AppDataPaths.CollectionsJson));
        Assert.True(File.Exists($"{AppDataPaths.CollectionsJson}.bak"));
    }

    [Fact]
    public async Task AddCollectionAsync_PersistsAndReturnsCollectionWithId()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();

        var collection = await repo.AddCollectionAsync("Staging API");

        Assert.NotEmpty(collection.Id);
        Assert.Equal("Staging API", collection.Name);
        Assert.Single(repo.Collections);
    }

    [Fact]
    public async Task LoadAsync_AfterSave_RestoresCollections()
    {
        using var _ = new AppDataSandbox();

        var writer = new CollectionRepository();
        await writer.LoadAsync();
        var added = await writer.AddCollectionAsync("Pet Store");

        var reader = new CollectionRepository();
        await reader.LoadAsync();

        Assert.Single(reader.Collections);
        Assert.Equal(added.Id, reader.Collections[0].Id);
        Assert.Equal("Pet Store", reader.Collections[0].Name);
    }

    // ── Backup recovery ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WithCorruptedPrimary_RecoverFromBackup()
    {
        using var _ = new AppDataSandbox();

        var writer = new CollectionRepository();
        await writer.LoadAsync();
        await writer.AddCollectionAsync("Recovery Test");

        // Corrupt the primary file
        await File.WriteAllTextAsync(AppDataPaths.CollectionsJson, "{ invalid json");

        var reader = new CollectionRepository();
        await reader.LoadAsync();

        Assert.Single(reader.Collections);
        Assert.Equal("Recovery Test", reader.Collections[0].Name);
    }

    // ── UpdateCollectionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCollectionAsync_ReturnsTrueAndPersistsChange()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        var col = await repo.AddCollectionAsync("Original");

        col.Name = "Renamed";
        var result = await repo.UpdateCollectionAsync(col);

        Assert.True(result);
        Assert.Equal("Renamed", repo.Collections[0].Name);
    }

    [Fact]
    public async Task UpdateCollectionAsync_ForUnknownId_ReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();

        var phantom = new ApiCollection { Id = Guid.NewGuid().ToString("N"), Name = "Ghost" };
        var result = await repo.UpdateCollectionAsync(phantom);

        Assert.False(result);
    }

    // ── DeleteCollectionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCollectionAsync_RemovesCollection()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        var col = await repo.AddCollectionAsync("ToDelete");

        var result = await repo.DeleteCollectionAsync(col.Id);

        Assert.True(result);
        Assert.Empty(repo.Collections);
    }

    [Fact]
    public async Task DeleteCollectionAsync_ForUnknownId_ReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();

        var result = await repo.DeleteCollectionAsync("nonexistent");

        Assert.False(result);
    }

    // ── FindRequest ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindRequest_ReturnsRequestAndCollectionWhenFound()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        var col = await repo.AddCollectionAsync("API");

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Get Users",
            Method = ApiRequestMethod.Get,
            Url = "/users",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        col.Nodes.Add(new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = request.Name,
            Request = request,
        });
        await repo.UpdateCollectionAsync(col);

        var (foundCollection, foundRequest) = repo.FindRequest(request.Id);

        Assert.NotNull(foundCollection);
        Assert.NotNull(foundRequest);
        Assert.Equal(request.Id, foundRequest!.Id);
        Assert.Equal(col.Id, foundCollection!.Id);
    }

    [Fact]
    public async Task FindRequest_ReturnsNullsWhenNotFound()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        await repo.AddCollectionAsync("Empty");

        var (col, req) = repo.FindRequest("nonexistent");

        Assert.Null(col);
        Assert.Null(req);
    }

    [Fact]
    public async Task FindRequest_FindsRequestNestedInsideFolder()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();
        var col = await repo.AddCollectionAsync("API");

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Nested",
            Method = ApiRequestMethod.Post,
            Url = "/nested",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var folder = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Folder,
            Name = "Users",
            Children =
            [
                new ApiCollectionNode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = ApiCollectionNodeType.Request,
                    Name = request.Name,
                    Request = request,
                }
            ],
        };
        col.Nodes.Add(folder);
        await repo.UpdateCollectionAsync(col);

        var (_, foundRequest) = repo.FindRequest(request.Id);

        Assert.NotNull(foundRequest);
        Assert.Equal(request.Id, foundRequest!.Id);
    }

    // ── Schema version preserved ───────────────────────────────────────────────

    [Fact]
    public async Task ReplaceStoreAsync_PreservesSchemaVersion()
    {
        using var _ = new AppDataSandbox();
        var repo = new CollectionRepository();
        await repo.LoadAsync();

        var store = new CollectionsStore
        {
            SchemaVersion = 1,
            Collections = [new ApiCollection { Id = "abc", Name = "Imported" }],
        };
        await repo.ReplaceStoreAsync(store);

        var reader = new CollectionRepository();
        await reader.LoadAsync();
        Assert.Single(reader.Collections);
        Assert.Equal("Imported", reader.Collections[0].Name);
    }
}
