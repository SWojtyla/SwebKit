using SwebKit.Core.Configuration;

namespace SwebKit.Core.Tests;

public class UiStateFilterTests
{
    private const string ScopeKey = "ns-guid:my-queue";

    [Fact]
    public async Task SaveFilterAsync_FilterAppearsInGetFilters()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("errors", result[0].Name);
        Assert.Equal("level=error", result[0].Value);
    }

    [Fact]
    public async Task GetFilters_UnknownScope_ReturnsEmpty()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        var result = repo.GetFilters("no-such-scope");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveFilterAsync_SameName_Overwrites()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "old-value" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "new-value" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("new-value", result[0].Value);
    }

    [Fact]
    public async Task SaveFilterAsync_SameNameDifferentCase_Overwrites()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "Errors", Value = "v1" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "v2" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("v2", result[0].Value);
    }

    [Fact]
    public async Task DeleteFilterAsync_RemovesMatchingFilter()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "warnings", Value = "level=warn" });

        await repo.DeleteFilterAsync(ScopeKey, "errors");

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("warnings", result[0].Name);
    }

    [Fact]
    public async Task DeleteFilterAsync_UnknownScope_DoesNotThrow()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.DeleteFilterAsync("no-such-scope", "no-such-filter"); // should not throw
    }

    [Fact]
    public async Task SaveFilterAsync_IsolatedPerScope()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();
        const string scopeA = "ns-a:queue";
        const string scopeB = "ns-b:queue";

        await repo.SaveFilterAsync(scopeA, new SavedFilter { Name = "f1", Value = "v1" });
        await repo.SaveFilterAsync(scopeB, new SavedFilter { Name = "f2", Value = "v2" });

        Assert.Equal("v1", repo.GetFilters(scopeA)[0].Value);
        Assert.Equal("v2", repo.GetFilters(scopeB)[0].Value);
        Assert.Single(repo.GetFilters(scopeA));
        Assert.Single(repo.GetFilters(scopeB));
    }

    [Fact]
    public async Task PersistenceRoundtrip_FiltersRestoreAfterReload()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var result = reader.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("errors", result[0].Name);
        Assert.Equal("level=error", result[0].Value);
    }
}
