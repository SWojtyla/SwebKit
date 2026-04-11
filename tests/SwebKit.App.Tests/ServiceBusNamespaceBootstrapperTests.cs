using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public sealed class ServiceBusNamespaceBootstrapperTests
{
    [Fact]
    public void BuildInitialStates_RestoresSnapshots_AndAppendsDemoNamespaces()
    {
        var configuredNamespaces = new List<ServiceBusNamespace>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Alias = "orders-live",
                FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
                CredentialKey = "orders-live"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Alias = "payments-live",
                FullyQualifiedNamespace = "payments-live.servicebus.windows.net",
                CredentialKey = "payments-live"
            }
        };

        var snapshots = new Dictionary<Guid, ServiceBusNamespaceBootstrapSnapshot>
        {
            [configuredNamespaces[0].Id] = new(true, null),
            [configuredNamespaces[1].Id] = new(false, "Access denied")
        };

        var bootstrapper = new ServiceBusNamespaceBootstrapper(new FakeCredentialStore());

        var states = bootstrapper.BuildInitialStates(configuredNamespaces, snapshots, useDemoData: true);

        Assert.Equal(4, states.Count);
        Assert.True(states[0].ShouldConnect);
        Assert.Null(states[0].ConnectionError);
        Assert.False(states[0].IsDemo);
        Assert.False(states[1].ShouldConnect);
        Assert.Equal("Access denied", states[1].ConnectionError);
        Assert.Equal(2, states.Count(state => state.IsDemo));
        Assert.All(states.Where(state => state.IsDemo), state => Assert.NotNull(state.Client));
    }

    [Fact]
    public async Task ConnectAsync_MissingCredential_ReturnsFriendlyError()
    {
        var bootstrapper = new ServiceBusNamespaceBootstrapper(new FakeCredentialStore());

        var result = await bootstrapper.ConnectAsync(new ServiceBusNamespace
        {
            Alias = "orders-live",
            FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
            CredentialKey = "missing-secret"
        });

        Assert.Null(result.Client);
        Assert.Equal("Connection string not found in credential store.", result.ConnectionError);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public void Save(string key, string secret)
        {
        }

        public string? Get(string key) => null;

        public void Delete(string key)
        {
        }

        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}