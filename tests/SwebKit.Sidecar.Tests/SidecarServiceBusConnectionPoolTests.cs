using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Wraps <see cref="DemoServiceBusClient"/> so the pool has a real <see cref="IServiceBusClient"/> to cache
/// while the test tracks disposal. <see cref="IServiceBusClient"/> does not declare a disposal contract, but
/// the concrete Azure client implements <see cref="IAsyncDisposable"/> and <c>ClientCache</c> disposes on that
/// at runtime — so this double implements it too, to pin exactly that behaviour.
/// </summary>
internal sealed class TrackingServiceBusClient : IServiceBusClient, IAsyncDisposable
{
    private readonly IServiceBusClient _inner = DemoServiceBusClient.OrdersDev();

    public int DisposeAsyncCallCount { get; private set; }
    public bool WasDisposedAsync => DisposeAsyncCallCount > 0;

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCallCount++;
        return ValueTask.CompletedTask;
    }

    public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => _inner.GetNamespaceInfoAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => _inner.ListQueuesAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => _inner.ListTopicsAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
        _inner.ListSubscriptionsAsync(topicName, ct);
    public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) =>
        _inner.SetQueueEnabledAsync(queueName, enabled, ct);
    public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) =>
        _inner.SetTopicEnabledAsync(topicName, enabled, ct);
    public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) =>
        _inner.SetSubscriptionEnabledAsync(topicName, subscriptionName, enabled, ct);
    public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
        _inner.GetEntityStatsAsync(entityPath, ct);
    public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        _inner.PeekMessagesAsync(entityPath, count, ct, fromSequenceNumber);
    public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        _inner.PeekDeadLetterAsync(entityPath, count, ct, fromSequenceNumber);
    public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
        _inner.CompleteMessagesAsync(entityPath, sequenceNumbers, ct);
    public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) =>
        _inner.PurgeMessagesAsync(entityPath, deadLetter, ct);
    public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) =>
        _inner.SendMessageAsync(entityPath, message, ct);
    public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) =>
        _inner.SendBatchAsync(entityPath, messages, ct);
    public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
        _inner.ScheduleMessageAsync(entityPath, message, scheduledEnqueueTime, ct);
    public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
        _inner.CancelScheduledMessageAsync(entityPath, sequenceNumber, ct);
    public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
        _inner.ResubmitDeadLetterAsync(entityPath, sequenceNumbers, targetEntityPath, remapRules, ct);
    public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
        _inner.CompleteDeadLetterAsync(entityPath, sequenceNumbers, ct);
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
}

internal sealed class TrackingServiceBusClientFactory : IServiceBusClientFactory
{
    public List<string> ConnectionStringCalls { get; } = [];
    public List<string> EntraCalls { get; } = [];

    public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp)
    {
        ConnectionStringCalls.Add(connectionString);
        return new TrackingServiceBusClient();
    }

    public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp)
    {
        EntraCalls.Add(fullyQualifiedNamespace);
        return new TrackingServiceBusClient();
    }

    public string ParseFullyQualifiedNamespace(string connectionString) => connectionString;

    public ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource) =>
        new(connectionString, null, "SharedAccessKey", credentialSource);

    public ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace) =>
        new(fullyQualifiedNamespace, null, "Entra", "DefaultAzureCredential");

    public int TotalCalls => ConnectionStringCalls.Count + EntraCalls.Count;
}

/// <summary>
/// Covers the per-namespace caching this pool adds in front of <see cref="IServiceBusClientFactory"/>. Every
/// one of the sixteen endpoint handlers used to build a fresh client — and therefore a fresh AMQP connection,
/// management pipeline and <c>DefaultAzureCredential</c> with an empty token cache — on every request, and
/// disposed none of them. Opening a namespace fires one request per topic, so that multiplied.
/// </summary>
public class SidecarServiceBusConnectionPoolTests
{
    private static ServiceBusNamespace Namespace(string alias, SbAuthMode mode = SbAuthMode.ConnectionString) => new()
    {
        Id = Guid.NewGuid(),
        Alias = alias,
        FullyQualifiedNamespace = $"{alias}.servicebus.windows.net",
        CredentialKey = $"Endpoint=sb://{alias}/;SharedAccessKeyName=k;SharedAccessKey=v",
        AuthMode = mode,
    };

    private static SidecarServiceBusConnectionPool Build(TrackingServiceBusClientFactory factory, bool demoMode = false) =>
        new(factory, new DemoModeService { IsDemoMode = demoMode });

    [Fact]
    public void GetOrCreate_CachesByNamespaceId_DoesNotRebuildOnRepeatedCalls()
    {
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);
        var ns = Namespace("sb-dev");

        var first = pool.GetOrCreate(ns);
        var second = pool.GetOrCreate(ns);

        Assert.Same(first, second);
        Assert.Equal(1, factory.TotalCalls);
    }

    [Fact]
    public void GetOrCreate_DifferentNamespaces_CreatesSeparateClients()
    {
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);

        var a = pool.GetOrCreate(Namespace("sb-a"));
        var b = pool.GetOrCreate(Namespace("sb-b"));

        Assert.NotSame(a, b);
        Assert.Equal(2, factory.TotalCalls);
    }

    [Fact]
    public void GetOrCreate_EntraNamespace_UsesTheEntraFactoryPath()
    {
        // Pitfall AZ-4: Entra clients must keep going through AzureCredentialFactory. Caching the client
        // is what caches the credential with it — the auth path itself must not change.
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);

        pool.GetOrCreate(Namespace("sb-entra", SbAuthMode.DefaultAzureCredential));

        Assert.Single(factory.EntraCalls);
        Assert.Empty(factory.ConnectionStringCalls);
        Assert.Equal("sb-entra.servicebus.windows.net", factory.EntraCalls[0]);
    }

    [Fact]
    public void Evict_DisposesClient_AndForcesRebuildOnNextCall()
    {
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);
        var ns = Namespace("sb-dev");

        var first = (TrackingServiceBusClient)pool.GetOrCreate(ns);
        pool.Evict(ns.Id.ToString());

        Assert.True(first.WasDisposedAsync);
        var second = pool.GetOrCreate(ns);
        Assert.NotSame(first, second);
        Assert.Equal(2, factory.TotalCalls);
    }

    [Fact]
    public void InvalidateAll_DisposesEveryCachedClient_ButPoolStaysUsable()
    {
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);
        var nsA = Namespace("sb-a");
        var a = (TrackingServiceBusClient)pool.GetOrCreate(nsA);
        var b = (TrackingServiceBusClient)pool.GetOrCreate(Namespace("sb-b"));

        // Simulates a profile save: a namespace's connection string or auth mode may have changed.
        pool.InvalidateAll();

        Assert.True(a.WasDisposedAsync);
        Assert.True(b.WasDisposedAsync);

        var rebuilt = pool.GetOrCreate(nsA);
        Assert.NotSame(a, rebuilt);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllCachedClients()
    {
        var factory = new TrackingServiceBusClientFactory();
        var pool = Build(factory);
        var a = (TrackingServiceBusClient)pool.GetOrCreate(Namespace("sb-a"));

        await pool.DisposeAsync();

        Assert.True(a.WasDisposedAsync);
    }

    [Fact]
    public void DemoMode_NeverCallsTheFactory_AndNeverDisposesTheBorrowedClient()
    {
        // DemoModeService hands out long-lived singletons it disposes itself. The pool returns them
        // without caching, so invalidation can never dispose a client other requests still hold.
        var factory = new TrackingServiceBusClientFactory();
        var demo = new DemoModeService { IsDemoMode = true };
        var pool = new SidecarServiceBusConnectionPool(factory, demo);
        var ns = demo.GetDemoNamespaces().First();

        var client = pool.GetOrCreate(ns);
        pool.InvalidateAll();

        Assert.Equal(0, factory.TotalCalls);
        Assert.Same(client, pool.GetOrCreate(ns));
    }

    [Fact]
    public void DemoModeRequest_NeverGetsAClientCachedUnderTheSameId()
    {
        // A save made while demo mode was on persists the demo namespace's id into the real profile,
        // so a demo-off request caches a real client under it. Demo-mode requests must still get the
        // demo singleton, not that cached entry.
        var factory = new TrackingServiceBusClientFactory();
        var demo = new DemoModeService();
        var pool = new SidecarServiceBusConnectionPool(factory, demo);
        var persistedDemoNs = new ServiceBusNamespace
        {
            Id = DemoModeService.DemoNamespaceId1,
            Alias = "orders-dev",
            FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
            CredentialKey = "Endpoint=sb://orders-dev/;SharedAccessKeyName=k;SharedAccessKey=v",
        };

        var realClient = pool.GetOrCreate(persistedDemoNs);
        Assert.IsType<TrackingServiceBusClient>(realClient);

        demo.IsDemoMode = true;
        var demoClient = pool.GetOrCreate(demo.GetDemoNamespaces().First(n => n.Id == DemoModeService.DemoNamespaceId1));

        Assert.NotSame(realClient, demoClient);
        Assert.Equal(1, factory.TotalCalls);
    }

    [Fact]
    public void DemoModeClient_IsNotCachedForLaterNonDemoRequests()
    {
        // The mirror hazard: if the demo singleton were cached under the namespace id, a later
        // non-demo request for that id would silently get the demo client.
        var factory = new TrackingServiceBusClientFactory();
        var demo = new DemoModeService { IsDemoMode = true };
        var pool = new SidecarServiceBusConnectionPool(factory, demo);
        var ns = demo.GetDemoNamespaces().First();

        var demoClient = pool.GetOrCreate(ns);

        demo.IsDemoMode = false;
        ns.CredentialKey = "Endpoint=sb://x/;SharedAccessKeyName=k;SharedAccessKey=v";
        var realClient = pool.GetOrCreate(ns);

        Assert.NotSame(demoClient, realClient);
        Assert.IsType<TrackingServiceBusClient>(realClient);
        Assert.Equal(1, factory.TotalCalls);
    }
}
