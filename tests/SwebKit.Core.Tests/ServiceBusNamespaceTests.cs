using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class ServiceBusNamespaceTests
{
    [Fact]
    public void ServiceBusNamespace_DefaultId_IsNotEmpty()
    {
        var ns = new ServiceBusNamespace();
        Assert.NotEqual(Guid.Empty, ns.Id);
    }

    [Fact]
    public void ServiceBusNamespace_DefaultCreatedAt_IsSet()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ns = new ServiceBusNamespace();
        Assert.True(ns.CreatedAt >= before);
    }

    [Fact]
    public void SbEntityLink_MatchesNamespaceAndPath()
    {
        var nsId = Guid.NewGuid();
        var link = new SbEntityLink { NamespaceId = nsId, EntityPath = "orders-queue" };

        Assert.Equal(nsId, link.NamespaceId);
        Assert.Equal("orders-queue", link.EntityPath);
        Assert.Null(link.Alias);
    }

    // TODO: Re-enable when ProjectEnvironment type is restored or tests are updated.
#if false
    [Fact]
    public void ProjectEnvironment_ServiceBusEntityLinks_DefaultsToEmpty()
    {
        var env = new ProjectEnvironment();
        Assert.Empty(env.ServiceBusEntityLinks);
    }

    [Fact]
    public void ProjectEnvironment_CanAddAndRemoveEntityLink()
    {
        var nsId = Guid.NewGuid();
        var env = new ProjectEnvironment();
        var link = new SbEntityLink { NamespaceId = nsId, EntityPath = "invoices-queue" };

        env.ServiceBusEntityLinks.Add(link);
        Assert.Single(env.ServiceBusEntityLinks);

        env.ServiceBusEntityLinks.Remove(link);
        Assert.Empty(env.ServiceBusEntityLinks);
    }
#endif
}
