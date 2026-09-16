using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class SqlQueryRepositoryTests
{
    [Fact]
    public async Task AddQueryAsync_Then_GetQueriesAsync_RoundTrips()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        await repo.AddQueryAsync(new SavedSqlQuery { Name = "Top customers", Sql = "SELECT * FROM c", ConnectionId = "conn-1", Folder = "reports" });
        await repo.AddQueryAsync(new SavedSqlQuery { Name = "Global health", Sql = "SELECT 1", ConnectionId = null });

        var all = await repo.GetQueriesAsync();
        Assert.Equal(2, all.Count);

        var scoped = await repo.GetQueriesAsync("conn-1");
        Assert.Equal(2, scoped.Count); // connection-scoped + global (null ConnectionId) both match

        var other = await repo.GetQueriesAsync("conn-2");
        Assert.Single(other);
        Assert.Equal("Global health", other[0].Name);
    }

    [Fact]
    public async Task GetQueriesAsync_SortsByFolderThenName()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        await repo.AddQueryAsync(new SavedSqlQuery { Name = "b", Sql = "s", Folder = "z" });
        await repo.AddQueryAsync(new SavedSqlQuery { Name = "a", Sql = "s", Folder = "z" });
        await repo.AddQueryAsync(new SavedSqlQuery { Name = "c", Sql = "s", Folder = "a" });

        var queries = await repo.GetQueriesAsync();
        Assert.Equal(["c", "a", "b"], queries.Select(q => q.Name).ToArray());
    }

    [Fact]
    public async Task DeleteQueryAsync_RemovesAndPersists()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        var saved = await repo.AddQueryAsync(new SavedSqlQuery { Name = "q", Sql = "s" });
        Assert.True(await repo.DeleteQueryAsync(saved.Id));
        Assert.False(await repo.DeleteQueryAsync(saved.Id));
        Assert.Empty(await repo.GetQueriesAsync());
    }

    [Fact]
    public async Task SavedQueries_PersistAcrossRepositoryInstances()
    {
        using var _ = new AppDataSandbox();
        using (var repo = new SqlQueryRepository())
            await repo.AddQueryAsync(new SavedSqlQuery { Name = "persisted", Sql = "SELECT 1" });

        using var repo2 = new SqlQueryRepository();
        var queries = await repo2.GetQueriesAsync();
        Assert.Single(queries);
        Assert.Equal("persisted", queries[0].Name);
    }

    [Fact]
    public async Task AddHistoryEntryAsync_NewestFirst_AndConnectionFiltered()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        await repo.AddHistoryEntryAsync(new SqlHistoryEntry { Sql = "first", ConnectionId = "c1", ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-2) });
        await repo.AddHistoryEntryAsync(new SqlHistoryEntry { Sql = "second", ConnectionId = "c2", ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await repo.AddHistoryEntryAsync(new SqlHistoryEntry { Sql = "third", ConnectionId = "c1", ExecutedAt = DateTimeOffset.UtcNow });

        var all = await repo.GetHistoryAsync();
        Assert.Equal(["third", "second", "first"], all.Select(h => h.Sql).ToArray());

        var c1 = await repo.GetHistoryAsync("c1");
        Assert.Equal(["third", "first"], c1.Select(h => h.Sql).ToArray());
    }

    [Fact]
    public async Task History_IsCapped_AtMaxEntries()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        for (var i = 0; i < SqlQueryRepository.MaxHistoryEntries + 10; i++)
            await repo.AddHistoryEntryAsync(new SqlHistoryEntry { Sql = $"q{i}", ConnectionId = "c" });

        var history = await repo.GetHistoryAsync();
        Assert.Equal(SqlQueryRepository.MaxHistoryEntries, history.Count);
        // Oldest entries evicted — the first remaining is q10.
        Assert.Equal($"q{SqlQueryRepository.MaxHistoryEntries + 9}", history[0].Sql);
    }

    [Fact]
    public async Task ClearHistoryAsync_EmptiesHistory_ButKeepsQueries()
    {
        using var _ = new AppDataSandbox();
        using var repo = new SqlQueryRepository();

        await repo.AddQueryAsync(new SavedSqlQuery { Name = "keep", Sql = "s" });
        await repo.AddHistoryEntryAsync(new SqlHistoryEntry { Sql = "h", ConnectionId = "c" });

        await repo.ClearHistoryAsync();

        Assert.Empty(await repo.GetHistoryAsync());
        Assert.Single(await repo.GetQueriesAsync());
    }
}
