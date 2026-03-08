using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class ProfileRepositoryNamespaceTests
{
    private static ProfileRepository CreateRepo()
    {
        // Use a temp file to avoid touching real AppData
        var repo = new ProfileRepository();
        return repo;
    }

    [Fact]
    public void ServiceBusNamespaces_DefaultsToEmpty()
    {
        var repo = CreateRepo();
        Assert.Empty(repo.ServiceBusNamespaces);
    }

    [Fact]
    public void AddServiceBusNamespace_AppearsInList()
    {
        var repo = CreateRepo();
        var ns = new ServiceBusNamespace
        {
            Alias = "dev-sb",
            FullyQualifiedNamespace = "dev-sb.servicebus.windows.net",
            CredentialKey = "sb:ns:test"
        };

        repo.AddServiceBusNamespace(ns);

        Assert.Single(repo.ServiceBusNamespaces);
        Assert.Equal("dev-sb", repo.ServiceBusNamespaces[0].Alias);
    }

    [Fact]
    public void RemoveServiceBusNamespace_RemovesCorrectEntry()
    {
        var repo = CreateRepo();
        var ns1 = new ServiceBusNamespace { Alias = "ns1", FullyQualifiedNamespace = "ns1.servicebus.windows.net", CredentialKey = "k1" };
        var ns2 = new ServiceBusNamespace { Alias = "ns2", FullyQualifiedNamespace = "ns2.servicebus.windows.net", CredentialKey = "k2" };

        repo.AddServiceBusNamespace(ns1);
        repo.AddServiceBusNamespace(ns2);
        repo.RemoveServiceBusNamespace(ns1.Id);

        Assert.Single(repo.ServiceBusNamespaces);
        Assert.Equal("ns2", repo.ServiceBusNamespaces[0].Alias);
    }

    [Fact]
    public void FindServiceBusNamespace_ReturnsNullForUnknownId()
    {
        var repo = CreateRepo();
        Assert.Null(repo.FindServiceBusNamespace(Guid.NewGuid()));
    }

    [Fact]
    public void FindServiceBusNamespace_ReturnsCorrectEntry()
    {
        var repo = CreateRepo();
        var ns = new ServiceBusNamespace
        {
            Alias = "shared-sb",
            FullyQualifiedNamespace = "shared-sb.servicebus.windows.net",
            CredentialKey = "sb:ns:shared"
        };
        repo.AddServiceBusNamespace(ns);

        var found = repo.FindServiceBusNamespace(ns.Id);

        Assert.NotNull(found);
        Assert.Equal("shared-sb", found.Alias);
    }
}
