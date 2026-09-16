using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ServiceBusToolContextTests
{
    private static ServiceBusNamespace Namespace(string alias) => new()
    {
        Id = Guid.NewGuid(),
        Alias = alias,
        FullyQualifiedNamespace = $"{alias}.servicebus.windows.net",
        CredentialKey = alias,
    };

    [Fact]
    public void ResolveNamespace_ExplicitAliasFqdnAndId()
    {
        var dev = Namespace("dev");
        var prod = Namespace("prod");
        var state = TestSupport.CreateAppState(serviceBusNamespaces: [dev, prod]);

        Assert.Same(dev, ServiceBusToolContext.ResolveNamespace(state, "DEV").Namespace);
        Assert.Same(prod, ServiceBusToolContext.ResolveNamespace(state, prod.FullyQualifiedNamespace).Namespace);
        Assert.Same(prod, ServiceBusToolContext.ResolveNamespace(state, prod.Id.ToString()).Namespace);
    }

    [Fact]
    public void ResolveNamespace_ExplicitWinsOverAmbientSelection()
    {
        var dev = Namespace("dev");
        var prod = Namespace("prod");
        var state = TestSupport.CreateAppState(serviceBusNamespaces: [dev, prod]);
        using var context = AgentExecutionContext.Push(new Dictionary<string, string> { ["nsId"] = dev.Id.ToString() });

        Assert.Same(prod, ServiceBusToolContext.ResolveNamespace(state, "prod").Namespace);
    }

    [Fact]
    public void ResolveNamespace_UsesAmbientNsId()
    {
        var dev = Namespace("dev");
        var prod = Namespace("prod");
        var state = TestSupport.CreateAppState(serviceBusNamespaces: [prod, dev]);
        using var context = AgentExecutionContext.Push(new Dictionary<string, string> { ["nsId"] = dev.Id.ToString() });

        Assert.Same(dev, ServiceBusToolContext.ResolveNamespace(state, null).Namespace);
    }

    [Fact]
    public void ResolveNamespace_MultipleWithoutSelectionReturnsAliasesInsteadOfFirst()
    {
        var state = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace("prod"), Namespace("dev")]);

        var result = ServiceBusToolContext.ResolveNamespace(state, null);

        Assert.Null(result.Namespace);
        Assert.Contains("dev, prod", result.Error);
    }

    [Fact]
    public void ResolveNamespace_UnknownExplicitDoesNotFallBack()
    {
        var state = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace("dev")]);

        var result = ServiceBusToolContext.ResolveNamespace(state, "missing");

        Assert.Null(result.Namespace);
        Assert.Contains("missing", result.Error);
        Assert.Contains("dev", result.Error);
    }
}
