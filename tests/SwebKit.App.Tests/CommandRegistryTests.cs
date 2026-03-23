using SwebKit.App.Services;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Tests;

public class CommandRegistryTests
{
    #region Search tests (legacy)

    [Fact]
    public void Register_CommandIsReturnedBySearch()
    {
        var registry = CreateRegistryWithCommands();

        var results = registry.Search("");

        Assert.Contains(results, c => c.Id == "nav-service-bus");
    }

    [Fact]
    public void Search_FiltersOnLabel()
    {
        var registry = CreateRegistryWithCommands();

        var results = registry.Search("AKS");

        Assert.Single(results);
        Assert.Equal("nav-aks", results[0].Id);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var registry = CreateRegistryWithCommands();

        var results = registry.Search("service");

        Assert.Contains(results, c => c.Label == "Navigate to Service Bus");
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        var registry = CreateRegistryWithCommands();

        var results = registry.Search("");

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var registry = CreateRegistryWithCommands();

        var results = registry.Search("does-not-exist");

        Assert.Empty(results);
    }

    #endregion

    #region GetAvailable tests

    [Fact]
    public void GetAvailable_ReturnsGlobalCommands_WhenAreaIsNull()
    {
        var registry = CreateRegistry();
        registry.Register(new AppCommand
        {
            Id = "global-cmd",
            Label = "Global Command",
            AreaScope = null,
            Execute = () => Task.CompletedTask
        });

        var results = registry.GetAvailable(null);

        Assert.Single(results);
        Assert.Equal("global-cmd", results[0].Id);
    }

    [Fact]
    public void GetAvailable_FiltersCommandsByArea()
    {
        var registry = CreateRegistry();
        registry.Register(new AppCommand
        {
            Id = "aks-only",
            Label = "AKS Command",
            AreaScope = "aks",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "sb-only",
            Label = "Service Bus Command",
            AreaScope = "servicebus",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "global",
            Label = "Global",
            AreaScope = null,
            Execute = () => Task.CompletedTask
        });

        var results = registry.GetAvailable("aks");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Id == "aks-only");
        Assert.Contains(results, c => c.Id == "global");
        Assert.DoesNotContain(results, c => c.Id == "sb-only");
    }

    [Fact]
    public void GetAvailable_HidesUnavailableCommands()
    {
        var registry = CreateRegistry();
        registry.Register(new AppCommand
        {
            Id = "hidden",
            Label = "Hidden Command",
            IsAvailable = () => false,
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "visible",
            Label = "Visible Command",
            IsAvailable = () => true,
            Execute = () => Task.CompletedTask
        });

        var results = registry.GetAvailable(null);

        Assert.Single(results);
        Assert.Equal("visible", results[0].Id);
    }

    [Fact]
    public void GetAvailable_ShowsCommandsWithNullIsAvailable()
    {
        var registry = CreateRegistry();
        registry.Register(new AppCommand
        {
            Id = "no-predicate",
            Label = "No Predicate",
            IsAvailable = null,
            Execute = () => Task.CompletedTask
        });

        var results = registry.GetAvailable(null);

        Assert.Single(results);
        Assert.Equal("no-predicate", results[0].Id);
    }

    #endregion

    #region RecordUsedAsync tests

    [Fact]
    public async Task RecordUsedAsync_AddsCommandToRecentList()
    {
        var (registry, _) = CreateRegistryWithState();

        await CallRecordUsedAsync(registry, "cmd-a");

        Assert.Contains("cmd-a", registry.RecentCommandIds);
    }

    [Fact]
    public async Task RecordUsedAsync_MostRecentFirst()
    {
        var (registry, _) = CreateRegistryWithState();

        await CallRecordUsedAsync(registry, "cmd-a");
        await CallRecordUsedAsync(registry, "cmd-b");

        Assert.Equal("cmd-b", registry.RecentCommandIds[0]);
        Assert.Equal("cmd-a", registry.RecentCommandIds[1]);
    }

    [Fact]
    public async Task RecordUsedAsync_LimitsToFive()
    {
        var (registry, _) = CreateRegistryWithState();

        for (int i = 1; i <= 7; i++)
            await CallRecordUsedAsync(registry, $"cmd-{i}");

        Assert.Equal(5, registry.RecentCommandIds.Count);
        Assert.Equal("cmd-7", registry.RecentCommandIds[0]);
        Assert.DoesNotContain("cmd-1", registry.RecentCommandIds);
        Assert.DoesNotContain("cmd-2", registry.RecentCommandIds);
    }

    [Fact]
    public async Task RecordUsedAsync_DeduplicatesExistingEntry()
    {
        var (registry, _) = CreateRegistryWithState();

        await CallRecordUsedAsync(registry, "cmd-a");
        await CallRecordUsedAsync(registry, "cmd-b");
        await CallRecordUsedAsync(registry, "cmd-a"); // re-record

        Assert.Equal(2, registry.RecentCommandIds.Count);
        Assert.Equal("cmd-a", registry.RecentCommandIds[0]);
        Assert.Equal("cmd-b", registry.RecentCommandIds[1]);
    }

    #endregion

    #region Helpers

    /// <summary>Calls RecordUsedAsync, swallowing IOException from SaveAsync (no AppDataPaths in test).</summary>
    private static async Task CallRecordUsedAsync(CommandRegistry registry, string commandId)
    {
        try
        {
            await registry.RecordUsedAsync(commandId);
        }
        catch (IOException) { }
    }

    private static CommandRegistry CreateRegistry()
    {
        var uiState = new UiStateRepository();
        return new CommandRegistry(uiState);
    }

    private static (CommandRegistry Registry, UiStateRepository UiState) CreateRegistryWithState()
    {
        var uiState = new UiStateRepository();
        var registry = new CommandRegistry(uiState);
        return (registry, uiState);
    }

    private static CommandRegistry CreateRegistryWithCommands()
    {
        var registry = CreateRegistry();
        registry.Register(new AppCommand
        {
            Id = "nav-service-bus",
            Label = "Navigate to Service Bus",
            Category = "Navigation",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "nav-aks",
            Label = "Navigate to AKS",
            Category = "Navigation",
            Execute = () => Task.CompletedTask
        });
        registry.Register(new AppCommand
        {
            Id = "refresh",
            Label = "Refresh Current Area",
            Category = "Actions",
            Execute = () => Task.CompletedTask
        });

        return registry;
    }

    #endregion
}
