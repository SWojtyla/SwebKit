using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class ServiceBusNamespaceBootstrapper : IServiceBusNamespaceBootstrapper
{
    private static readonly Guid DemoNamespaceId1 = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoNamespaceId2 = new("00000000-0000-0000-0000-000000000002");

    private readonly ICredentialStore _credentialStore;
    private readonly IServiceBusClientFactory _clientFactory;

    public ServiceBusNamespaceBootstrapper(ICredentialStore credentialStore, IServiceBusClientFactory clientFactory)
    {
        _credentialStore = credentialStore;
        _clientFactory = clientFactory;
    }

    public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
        IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
        IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
        bool useDemoData)
    {
        if (useDemoData)
        {
            return BuildDemoStates();
        }

        var states = configuredNamespaces.Select(ns =>
        {
            if (cachedSnapshots.TryGetValue(ns.Id, out var snapshot))
            {
                return new ServiceBusNamespaceBootstrapState(
                    Namespace: ns,
                    Client: null,
                    ShouldConnect: snapshot.WasConnected,
                    ConnectionError: snapshot.WasConnected ? null : snapshot.Error,
                    IsDemo: false);
            }

            return new ServiceBusNamespaceBootstrapState(
                Namespace: ns,
                Client: null,
                ShouldConnect: true,
                ConnectionError: null,
                IsDemo: false);
        }).ToList();

        return states;
    }

    private static IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildDemoStates()
    {
        var states = new List<ServiceBusNamespaceBootstrapState>();
        states.Add(new ServiceBusNamespaceBootstrapState(
            Namespace: new ServiceBusNamespace
            {
                Id = DemoNamespaceId1,
                Alias = "orders-dev",
                FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
                CredentialKey = string.Empty
            },
            Client: DemoServiceBusClient.OrdersDev(),
            ShouldConnect: false,
            ConnectionError: null,
            IsDemo: true));

        states.Add(new ServiceBusNamespaceBootstrapState(
            Namespace: new ServiceBusNamespace
            {
                Id = DemoNamespaceId2,
                Alias = "payments-dev",
                FullyQualifiedNamespace = "payments-dev.servicebus.windows.net",
                CredentialKey = string.Empty
            },
            Client: DemoServiceBusClient.PaymentsDev(),
            ShouldConnect: false,
            ConnectionError: null,
            IsDemo: true));

        return states;
    }

    public async Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default)
    {
        IServiceBusClient? client = null;

        try
        {
            if (ns.AuthMode == SbAuthMode.DefaultAzureCredential)
            {
                client = _clientFactory.CreateWithEntra(ns.FullyQualifiedNamespace);
            }
            else
            {
                var connectionString = _credentialStore.Get(ns.CredentialKey);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return new ServiceBusNamespaceConnectionResult(
                        Client: null,
                        ConnectionError: "Connection string not found in credential store.");
                }

                client = _clientFactory.Create(connectionString);
            }

            var ok = await client.TestConnectionAsync(ct);
            if (!ok)
            {
                if (client is IAsyncDisposable d) await d.DisposeAsync();
                return new ServiceBusNamespaceConnectionResult(
                    Client: null,
                    ConnectionError: "Connection test failed. Check the namespace configuration.");
            }

            return new ServiceBusNamespaceConnectionResult(client, ConnectionError: null);
        }
        catch (OperationCanceledException)
        {
            if (client is IAsyncDisposable d2) await d2.DisposeAsync();

            throw;
        }
        catch (Exception ex)
        {
            if (client is IAsyncDisposable d3) await d3.DisposeAsync();

            return new ServiceBusNamespaceConnectionResult(Client: null, ConnectionError: ex.Message);
        }
    }
}