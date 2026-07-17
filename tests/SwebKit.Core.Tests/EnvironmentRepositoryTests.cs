using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public sealed class EnvironmentRepositoryTests
{
    // ── Load: empty file store ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_ReturnsEmptyEnvironments()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();

        await repo.LoadAsync();

        Assert.Empty(repo.Environments);
    }

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_UiStateIsDefault()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();

        await repo.LoadAsync();

        Assert.Null(repo.UiState.ActiveEnvironmentId);
        Assert.Empty(repo.UiState.LastSelectedRequestIdByCollection);
    }

    // ── Save / load round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_CreatesJsonAndBackupFile()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        await repo.AddEnvironmentAsync("Production");

        Assert.True(File.Exists(AppDataPaths.EnvironmentsJson));
        Assert.True(File.Exists($"{AppDataPaths.EnvironmentsJson}.bak"));
    }

    [Fact]
    public async Task AddEnvironmentAsync_PersistsAndReturnsEnvironmentWithId()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        var env = await repo.AddEnvironmentAsync("Staging");

        Assert.NotEmpty(env.Id);
        Assert.Equal("Staging", env.Name);
        Assert.Single(repo.Environments);
    }

    [Fact]
    public async Task LoadAsync_AfterSave_RestoresEnvironments()
    {
        using var _ = new AppDataSandbox();

        var writer = new EnvironmentRepository();
        await writer.LoadAsync();
        var added = await writer.AddEnvironmentAsync("Dev");

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();

        Assert.Single(reader.Environments);
        Assert.Equal(added.Id, reader.Environments[0].Id);
        Assert.Equal("Dev", reader.Environments[0].Name);
    }

    // ── Backup recovery ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WithCorruptedPrimary_RecoverFromBackup()
    {
        using var _ = new AppDataSandbox();

        var writer = new EnvironmentRepository();
        await writer.LoadAsync();
        await writer.AddEnvironmentAsync("Backup Test");

        await File.WriteAllTextAsync(AppDataPaths.EnvironmentsJson, "not valid json {{");

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();

        Assert.Single(reader.Environments);
        Assert.Equal("Backup Test", reader.Environments[0].Name);
    }

    // ── UpdateEnvironmentAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEnvironmentAsync_ReturnsTrueAndPersistsChange()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("Old Name");

        env.Name = "New Name";
        var result = await repo.UpdateEnvironmentAsync(env);

        Assert.True(result);
        Assert.Equal("New Name", repo.Environments[0].Name);
    }

    [Fact]
    public async Task UpdateEnvironmentAsync_ForUnknownId_ReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        var phantom = new ApiEnvironment { Id = Guid.NewGuid().ToString("N"), Name = "Ghost" };
        var result = await repo.UpdateEnvironmentAsync(phantom);

        Assert.False(result);
    }

    // ── DeleteEnvironmentAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEnvironmentAsync_RemovesEnvironment()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("ToDelete");

        var result = await repo.DeleteEnvironmentAsync(env.Id);

        Assert.True(result);
        Assert.Empty(repo.Environments);
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_ClearsActiveEnvironmentIdWhenDeleted()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("Active");
        await repo.SetActiveEnvironmentAsync(env.Id);

        await repo.DeleteEnvironmentAsync(env.Id);

        Assert.Null(repo.UiState.ActiveEnvironmentId);
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_ForUnknownId_ReturnsFalse()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        var result = await repo.DeleteEnvironmentAsync("nonexistent");

        Assert.False(result);
    }

    // ── SetActiveEnvironmentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SetActiveEnvironmentAsync_PersistsActiveEnvironmentId()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("Prod");

        await repo.SetActiveEnvironmentAsync(env.Id);

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();
        Assert.Equal(env.Id, reader.UiState.ActiveEnvironmentId);
    }

    [Fact]
    public async Task SetActiveEnvironmentAsync_ToNull_ClearsActiveEnvironment()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("Prod");
        await repo.SetActiveEnvironmentAsync(env.Id);

        await repo.SetActiveEnvironmentAsync(null);

        Assert.Null(repo.UiState.ActiveEnvironmentId);
    }

    // ── SetLastSelectedRequestAsync ────────────────────────────────────────────

    [Fact]
    public async Task SetLastSelectedRequestAsync_PersistsLastSelectedId()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        await repo.SetLastSelectedRequestAsync("col-1", "req-abc");

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();
        Assert.True(reader.UiState.LastSelectedRequestIdByCollection.TryGetValue("col-1", out var id));
        Assert.Equal("req-abc", id);
    }

    [Fact]
    public async Task SetLastSelectedRequestAsync_OverwritesPreviousSelection()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        await repo.SetLastSelectedRequestAsync("col-1", "req-first");
        await repo.SetLastSelectedRequestAsync("col-1", "req-second");

        Assert.Equal("req-second", repo.UiState.LastSelectedRequestIdByCollection["col-1"]);
    }

    // ── Collection scoping ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddEnvironmentAsync_WithCollectionId_ScopesAndRoundTrips()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        var scoped = await repo.AddEnvironmentAsync("DEV", "collection-1");
        var global = await repo.AddEnvironmentAsync("Shared");

        Assert.Equal("collection-1", scoped.CollectionId);
        Assert.Null(global.CollectionId);

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();
        Assert.Equal("collection-1", reader.Environments.Single(e => e.Name == "DEV").CollectionId);
        Assert.Null(reader.Environments.Single(e => e.Name == "Shared").CollectionId);
    }

    [Fact]
    public async Task SetActiveEnvironmentForCollectionAsync_PersistsPerCollection()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("DEV", "collection-1");

        await repo.SetActiveEnvironmentForCollectionAsync("collection-1", env.Id);

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();
        Assert.Equal(env.Id, reader.UiState.ActiveEnvironmentIdByCollection["collection-1"]);
    }

    [Fact]
    public async Task SetActiveEnvironmentForCollectionAsync_WithNull_ClearsPerCollectionSelection()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("DEV", "collection-1");
        await repo.SetActiveEnvironmentForCollectionAsync("collection-1", env.Id);

        await repo.SetActiveEnvironmentForCollectionAsync("collection-1", null);

        Assert.False(repo.UiState.ActiveEnvironmentIdByCollection.ContainsKey("collection-1"));
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_ClearsPerCollectionActiveReferences()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();
        var env = await repo.AddEnvironmentAsync("DEV", "collection-1");
        await repo.SetActiveEnvironmentForCollectionAsync("collection-1", env.Id);
        await repo.SetActiveEnvironmentForCollectionAsync("collection-2", env.Id);

        await repo.DeleteEnvironmentAsync(env.Id);

        Assert.Empty(repo.UiState.ActiveEnvironmentIdByCollection);
    }

    // ── Variables round-trip ───────────────────────────────────────────────────

    [Fact]
    public async Task Environment_WithVariables_RoundTripsCorrectly()
    {
        using var _ = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        await repo.LoadAsync();

        var env = await repo.AddEnvironmentAsync("WithVars");
        env.Variables.Add(new EnvironmentVariable
        {
            Key = "base_url",
            Value = "https://api.example.com",
            SecretSource = EnvironmentVariableSecretSource.Plain,
            IsEnabled = true,
        });
        env.Variables.Add(new EnvironmentVariable
        {
            Key = "token",
            SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
            CredentialKey = "my-token-key",
            IsEnabled = true,
        });
        await repo.UpdateEnvironmentAsync(env);

        var reader = new EnvironmentRepository();
        await reader.LoadAsync();
        var loaded = reader.Environments[0];

        Assert.Equal(2, loaded.Variables.Count);
        Assert.Equal("base_url", loaded.Variables[0].Key);
        Assert.Equal("https://api.example.com", loaded.Variables[0].Value);
        Assert.Equal(EnvironmentVariableSecretSource.WindowsCredentialStore, loaded.Variables[1].SecretSource);
        Assert.Equal("my-token-key", loaded.Variables[1].CredentialKey);
    }
}
