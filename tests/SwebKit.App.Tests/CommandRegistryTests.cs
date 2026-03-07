using SwebKit.App.Services;

namespace SwebKit.App.Tests;

public class CommandRegistryTests
{
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

    private static CommandRegistry CreateRegistryWithCommands()
    {
        var registry = new CommandRegistry();
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
}
