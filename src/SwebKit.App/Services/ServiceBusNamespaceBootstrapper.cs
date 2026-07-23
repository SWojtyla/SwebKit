using SwebKit.Azure.ServiceBus;
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
                // Always attempt to reconnect real namespaces on load/refresh; a previous failure may
                // have been transient or the user may have fixed credentials. Cache the last error
                // only so the UI can show a quick hint while the fresh connection attempt runs.
                return new ServiceBusNamespaceBootstrapState(
                    Namespace: ns,
                    Client: null,
                    ShouldConnect: true,
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

    private static List<ServiceBusNamespaceBootstrapState> BuildDemoStates()
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
        ServiceBusConnectionDiagnostic? diagnostic = null;

        try
        {
            if (ns.AuthMode == SbAuthMode.DefaultAzureCredential)
            {
                diagnostic = _clientFactory.BuildEntraConnectionDiagnostic(ns.FullyQualifiedNamespace);
                client = _clientFactory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType);
            }
            else
            {
                var connectionString = _credentialStore.Get(ns.CredentialKey);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return new ServiceBusNamespaceConnectionResult(
                        Client: null,
                        ConnectionError: "Connection string not found in credential store.",
                        Diagnostic: new ServiceBusConnectionDiagnostic(
                            EndpointHost: ns.FullyQualifiedNamespace,
                            SharedAccessKeyName: null,
                            AuthMethod: "SAS key",
                            CredentialSource: string.IsNullOrWhiteSpace(ns.CredentialKey) ? "(unnamed credential)" : ns.CredentialKey),
                        IsAuthFailure: true);
                }

                // Build the non-secret diagnostic BEFORE creating the client, so the source label /
                // endpoint / key name are available even if the connection attempt throws.
                diagnostic = _clientFactory.BuildConnectionDiagnostic(connectionString, ns.CredentialKey);
                client = _clientFactory.Create(connectionString, ns.TransportType);
            }

            var ok = await client.TestConnectionAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (!ok)
            {
                if (client is IAsyncDisposable d) await d.DisposeAsync();
                return new ServiceBusNamespaceConnectionResult(
                    Client: null,
                    ConnectionError: "Connection test failed. Check the namespace configuration.",
                    Diagnostic: diagnostic);
            }

            ct.ThrowIfCancellationRequested();
            return new ServiceBusNamespaceConnectionResult(client, ConnectionError: null, Diagnostic: diagnostic);
        }
        catch (OperationCanceledException)
        {
            if (client is IAsyncDisposable d2) await d2.DisposeAsync();

            throw;
        }
        catch (Exception ex)
        {
            // Defensive: Azure.Identity can wrap a caller-side cancellation as an AuthenticationFailedException.
            // Treat that as cancellation so callers don't surface a misleading generic connection error.
            if (ServiceBusExceptionClassifier.IsCancellation(ex, ct))
            {
                if (client is IAsyncDisposable d3) await d3.DisposeAsync();
                throw new OperationCanceledException("Service Bus connection was cancelled.", ex);
            }

            if (client is IAsyncDisposable d4) await d4.DisposeAsync();

            var isAuthFailure = ServiceBusExceptionClassifier.IsAuthenticationFailure(ex);
            var source = diagnostic?.CredentialSource;
            var message = isAuthFailure
                ? $"Authentication/authorization failed for credential '{source ?? ns.CredentialKey}'. {ex.Message}"
                : ex.Message;

            return new ServiceBusNamespaceConnectionResult(
                Client: null,
                ConnectionError: message,
                Diagnostic: diagnostic,
                IsAuthFailure: isAuthFailure);
        }
    }
}