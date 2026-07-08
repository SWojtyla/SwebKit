using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ServiceBusNamespaceBootstrapperTests
{
    [Fact]
    public void BuildInitialStates_RealMode_RestoresSnapshots()
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

        var bootstrapper = new ServiceBusNamespaceBootstrapper(new FakeCredentialStore(), new NullServiceBusClientFactory());

        var states = bootstrapper.BuildInitialStates(configuredNamespaces, snapshots, useDemoData: false);

        Assert.Equal(2, states.Count);
        Assert.True(states[0].ShouldConnect);
        Assert.Null(states[0].ConnectionError);
        Assert.False(states[0].IsDemo);
        Assert.False(states[1].ShouldConnect);
        Assert.Equal("Access denied", states[1].ConnectionError);
        Assert.DoesNotContain(states, state => state.IsDemo);
    }

    [Fact]
    public void BuildInitialStates_DemoMode_UsesOnlyDemoNamespaces()
    {
        var configuredNamespaces = new List<ServiceBusNamespace>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Alias = "orders-live",
                FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
                CredentialKey = "orders-live"
            }
        };

        var bootstrapper = new ServiceBusNamespaceBootstrapper(new FakeCredentialStore(), new NullServiceBusClientFactory());

        var states = bootstrapper.BuildInitialStates(configuredNamespaces, new Dictionary<Guid, ServiceBusNamespaceBootstrapSnapshot>(), useDemoData: true);

        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.True(state.IsDemo));
        Assert.All(states, state => Assert.NotNull(state.Client));
        Assert.DoesNotContain(states, state => state.Namespace.Alias == "orders-live");
    }

    [Fact]
    public async Task ConnectAsync_MissingCredential_ReturnsFriendlyError()
    {
        var bootstrapper = new ServiceBusNamespaceBootstrapper(new FakeCredentialStore(), new NullServiceBusClientFactory());

        var result = await bootstrapper.ConnectAsync(new ServiceBusNamespace
        {
            Alias = "orders-live",
            FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
            CredentialKey = "missing-secret"
        });

        Assert.Null(result.Client);
        Assert.Equal("Connection string not found in credential store.", result.ConnectionError);
    }

    [Fact]
    public async Task ConnectAsync_ValidCredential_UsesFactoryToCreateClient()
    {
        var store = new FakeCredentialStore { CredentialValue = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=" };
        var fakeClient = new FakeServiceBusClient();
        var factory = new CapturingServiceBusClientFactory(fakeClient);
        var bootstrapper = new ServiceBusNamespaceBootstrapper(store, factory);

        var result = await bootstrapper.ConnectAsync(new ServiceBusNamespace
        {
            Alias = "orders-live",
            FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
            CredentialKey = "orders-key"
        });

        Assert.Same(fakeClient, result.Client);
        Assert.Null(result.ConnectionError);
        Assert.Equal(store.CredentialValue, factory.LastConnectionString);
    }

    [Fact]
    public async Task ConnectAsync_AmqpWebSocketsTransport_PassesTransportTypeToFactory()
    {
        var store = new FakeCredentialStore { CredentialValue = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=" };
        var fakeClient = new FakeServiceBusClient();
        var factory = new CapturingServiceBusClientFactory(fakeClient);
        var bootstrapper = new ServiceBusNamespaceBootstrapper(store, factory);

        await bootstrapper.ConnectAsync(new ServiceBusNamespace
        {
            Alias = "orders-prd",
            FullyQualifiedNamespace = "orders-prd.servicebus.windows.net",
            CredentialKey = "orders-key",
            TransportType = SbTransportType.AmqpWebSockets
        });

        Assert.Equal(SbTransportType.AmqpWebSockets, factory.LastTransportType);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public string? CredentialValue { get; set; }

        public void Save(string key, string secret) { }

        public string? Get(string key) => CredentialValue;

        public void Delete(string key) { }

        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }

    private sealed class NullServiceBusClientFactory : IServiceBusClientFactory
    {
        public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp) =>
            throw new InvalidOperationException("Factory.Create should not be called in this test.");

        public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp) =>
            throw new InvalidOperationException("Factory.CreateWithEntra should not be called in this test.");

        public string ParseFullyQualifiedNamespace(string connectionString) =>
            throw new InvalidOperationException("Factory.ParseFullyQualifiedNamespace should not be called in this test.");
    }

    private sealed class CapturingServiceBusClientFactory : IServiceBusClientFactory
    {
        private readonly IServiceBusClient _client;
        public string? LastConnectionString { get; private set; }
        public SbTransportType? LastTransportType { get; private set; }

        public CapturingServiceBusClientFactory(IServiceBusClient client) => _client = client;

        public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp)
        {
            LastConnectionString = connectionString;
            LastTransportType = transportType;
            return _client;
        }

        public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp)
        {
            LastTransportType = transportType;
            return _client;
        }

        public string ParseFullyQualifiedNamespace(string connectionString) =>
            connectionString.Split(';')[0].Replace("Endpoint=sb://", string.Empty);
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => Task.FromResult(new SbNamespaceInfo { Name = "test", Endpoint = "test.servicebus.windows.net" });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => Task.FromResult(new SbEntityStats());
        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => Task.FromResult(0L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}