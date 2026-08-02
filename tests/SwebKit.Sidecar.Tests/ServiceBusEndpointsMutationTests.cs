using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Delegates every call to a real <see cref="DemoServiceBusClient"/> (sealed, so composition rather
/// than inheritance) while counting invocations of the mutation methods under test and optionally
/// injecting a fault into one of them. The call counts are the regression guard for the
/// production-readiness review's finding that a React key-collision bug causing duplicate-rendered
/// notifications might indicate the underlying complete/resubmit action fires twice, not just renders
/// twice (see ux-plan.md Phase 0.1) â€” these tests prove the sidecar handler itself only calls the
/// underlying client once per invocation, regardless of what the frontend does with the response.
/// </summary>
internal sealed class CountingServiceBusClient : IServiceBusClient
{
    private readonly IServiceBusClient _inner;

    public CountingServiceBusClient(IServiceBusClient inner) => _inner = inner;

    public int CompleteMessagesCallCount { get; private set; }
    public int PurgeMessagesCallCount { get; private set; }
    public int ResubmitDeadLetterCallCount { get; private set; }

    public Exception? ThrowOnPeekMessages { get; set; }
    public Exception? ThrowOnPeekDeadLetter { get; set; }
    public Exception? ThrowOnComplete { get; set; }
    public Exception? ThrowOnPurge { get; set; }
    public Exception? ThrowOnResubmit { get; set; }

    public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => _inner.GetNamespaceInfoAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => _inner.ListQueuesAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => _inner.ListTopicsAsync(ct);
    public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => _inner.ListSubscriptionsAsync(topicName, ct);
    public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => _inner.SetQueueEnabledAsync(queueName, enabled, ct);
    public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => _inner.SetTopicEnabledAsync(topicName, enabled, ct);
    public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => _inner.SetSubscriptionEnabledAsync(topicName, subscriptionName, enabled, ct);
    public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => _inner.GetEntityStatsAsync(entityPath, ct);

    public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        ThrowOnPeekMessages is not null ? Task.FromException<IReadOnlyList<SbMessage>>(ThrowOnPeekMessages) : _inner.PeekMessagesAsync(entityPath, count, ct, fromSequenceNumber);

    public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) =>
        ThrowOnPeekDeadLetter is not null ? Task.FromException<IReadOnlyList<SbMessage>>(ThrowOnPeekDeadLetter) : _inner.PeekDeadLetterAsync(entityPath, count, ct, fromSequenceNumber);

    public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
    {
        CompleteMessagesCallCount++;
        return ThrowOnComplete is not null ? Task.FromException<int>(ThrowOnComplete) : _inner.CompleteMessagesAsync(entityPath, sequenceNumbers, ct);
    }

    public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default)
    {
        PurgeMessagesCallCount++;
        return ThrowOnPurge is not null ? Task.FromException<int>(ThrowOnPurge) : _inner.PurgeMessagesAsync(entityPath, deadLetter, ct);
    }

    public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => _inner.SendMessageAsync(entityPath, message, ct);
    public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => _inner.SendBatchAsync(entityPath, messages, ct);
    public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => _inner.ScheduleMessageAsync(entityPath, message, scheduledEnqueueTime, ct);
    public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => _inner.CancelScheduledMessageAsync(entityPath, sequenceNumber, ct);

    public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default)
    {
        ResubmitDeadLetterCallCount++;
        return ThrowOnResubmit is not null ? Task.FromException(ThrowOnResubmit) : _inner.ResubmitDeadLetterAsync(entityPath, sequenceNumbers, targetEntityPath, remapRules, ct);
    }

    public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => _inner.CompleteDeadLetterAsync(entityPath, sequenceNumbers, ct);
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
}

/// <summary>Records the connection-string/namespace passed to each creation call and returns a configurable client.</summary>
internal sealed class FakeServiceBusClientFactory : IServiceBusClientFactory
{
    public IServiceBusClient Client { get; set; } = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
    public List<string> CreateCalls { get; } = [];
    public List<string> CreateWithEntraCalls { get; } = [];

    public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp)
    {
        CreateCalls.Add(connectionString);
        return Client;
    }

    public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp)
    {
        CreateWithEntraCalls.Add(fullyQualifiedNamespace);
        return Client;
    }

    public string ParseFullyQualifiedNamespace(string connectionString) => throw new NotSupportedException();
    public ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource) => throw new NotSupportedException();
    public ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace) => throw new NotSupportedException();
}

public class ServiceBusEndpointsMutationTests
{
    private const string EntityPath = "order-created";

    private static (ProfileRepository Profile, DemoModeService Demo, FakeServiceBusClientFactory Factory, Guid NsId) Build(IServiceBusClient? client = null)
    {
        var profile = new ProfileRepository();
        var nsId = Guid.NewGuid();
        profile.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Id = nsId,
            Alias = "test-ns",
            FullyQualifiedNamespace = "test-ns.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "test-ns-key",
        });
        var demo = new DemoModeService();
        var factory = new FakeServiceBusClientFactory();
        if (client is not null)
            factory.Client = client;
        return (profile, demo, factory, nsId);
    }

    private static int ReadAnonymousIntProperty(IResult result, string propertyName)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        Assert.NotNull(value);
        var property = value!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return (int)property!.GetValue(value)!;
    }

    // â”€â”€ Peek active messages â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PeekMessagesAsync_Success_ReturnsMessagesFromClient()
    {
        var (profile, demo, factory, nsId) = Build(new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()));

        var result = await ServiceBusEndpoints.PeekMessagesAsync(nsId.ToString(), EntityPath, 10, null, profile, factory, demo, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<SbMessage>>>(result);
        Assert.NotEmpty(ok.Value!);
    }

    [Fact]
    public async Task PeekMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory, _) = Build();

        var result = await ServiceBusEndpoints.PeekMessagesAsync(Guid.NewGuid().ToString(), EntityPath, 10, null, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task PeekMessagesAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()) { ThrowOnPeekMessages = new InvalidOperationException("service bus unavailable") };
        var (profile, demo, factory, nsId) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceBusEndpoints.PeekMessagesAsync(nsId.ToString(), EntityPath, 10, null, profile, factory, demo, CancellationToken.None));
        Assert.Equal("service bus unavailable", ex.Message);
    }

    // â”€â”€ Peek dead-letter messages â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PeekDeadLetterAsync_Success_ReturnsMessagesFromClient()
    {
        var (profile, demo, factory, nsId) = Build(new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()));

        var result = await ServiceBusEndpoints.PeekDeadLetterAsync(nsId.ToString(), EntityPath, 10, null, profile, factory, demo, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Ok<IReadOnlyList<SbMessage>>>(result);
        Assert.NotEmpty(ok.Value!);
    }

    [Fact]
    public async Task PeekDeadLetterAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()) { ThrowOnPeekDeadLetter = new InvalidOperationException("service bus unavailable") };
        var (profile, demo, factory, nsId) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceBusEndpoints.PeekDeadLetterAsync(nsId.ToString(), EntityPath, 10, null, profile, factory, demo, CancellationToken.None));
        Assert.Equal("service bus unavailable", ex.Message);
    }

    // â”€â”€ Complete messages â€” the highest-value test in this file â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task CompleteMessagesAsync_Success_CallsUnderlyingClientExactlyOnce()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var (profile, demo, factory, nsId) = Build(faulty);

        var result = await ServiceBusEndpoints.CompleteMessagesAsync(nsId.ToString(), EntityPath, [4501, 4502], profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.Equal(1, faulty.CompleteMessagesCallCount);
        Assert.Equal(2, ReadAnonymousIntProperty(result, "completed"));
    }

    [Fact]
    public async Task CompleteMessagesAsync_NamespaceNotFound_ReturnsNotFound_AndNeverCallsClient()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var (profile, demo, factory, _) = Build(faulty);

        var result = await ServiceBusEndpoints.CompleteMessagesAsync(Guid.NewGuid().ToString(), EntityPath, [4501], profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, faulty.CompleteMessagesCallCount);
    }

    [Fact]
    public async Task CompleteMessagesAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()) { ThrowOnComplete = new InvalidOperationException("service bus unavailable") };
        var (profile, demo, factory, nsId) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceBusEndpoints.CompleteMessagesAsync(nsId.ToString(), EntityPath, [4501], profile, factory, demo, CancellationToken.None));
        Assert.Equal("service bus unavailable", ex.Message);
        Assert.Equal(1, faulty.CompleteMessagesCallCount); // still called exactly once even though it threw
    }

    // â”€â”€ Purge â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PurgeMessagesAsync_Success_ReturnsPurgedCount()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var (profile, demo, factory, nsId) = Build(faulty);

        var result = await ServiceBusEndpoints.PurgeMessagesAsync(nsId.ToString(), EntityPath, false, profile, factory, demo, CancellationToken.None);

        Assert.Equal(1, faulty.PurgeMessagesCallCount);
        Assert.Equal(5, ReadAnonymousIntProperty(result, "purged")); // 5 active messages seeded for order-created
    }

    [Fact]
    public async Task PurgeMessagesAsync_NamespaceNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory, _) = Build();

        var result = await ServiceBusEndpoints.PurgeMessagesAsync(Guid.NewGuid().ToString(), EntityPath, false, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task PurgeMessagesAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()) { ThrowOnPurge = new InvalidOperationException("service bus unavailable") };
        var (profile, demo, factory, nsId) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceBusEndpoints.PurgeMessagesAsync(nsId.ToString(), EntityPath, false, profile, factory, demo, CancellationToken.None));
        Assert.Equal("service bus unavailable", ex.Message);
        Assert.Equal(1, faulty.PurgeMessagesCallCount);
    }

    // â”€â”€ DLQ resubmit â€” same "exactly once" regression concern as complete â”€â”€â”€

    [Fact]
    public async Task ResubmitDeadLetterAsync_Success_CallsUnderlyingClientExactlyOnce()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var (profile, demo, factory, nsId) = Build(faulty);
        var req = new ServiceBusEndpoints.ResubmitRequest { SequenceNumbers = ["4410"], TargetEntityPath = null };

        var result = await ServiceBusEndpoints.ResubmitDeadLetterAsync(nsId.ToString(), EntityPath, req, profile, factory, demo, CancellationToken.None);

        Assert.IsType<Ok>(result);
        Assert.Equal(1, faulty.ResubmitDeadLetterCallCount);
    }

    [Fact]
    public async Task ResubmitDeadLetterAsync_NamespaceNotFound_ReturnsNotFound_AndNeverCallsClient()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var (profile, demo, factory, _) = Build(faulty);
        var req = new ServiceBusEndpoints.ResubmitRequest { SequenceNumbers = ["4410"] };

        var result = await ServiceBusEndpoints.ResubmitDeadLetterAsync(Guid.NewGuid().ToString(), EntityPath, req, profile, factory, demo, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, faulty.ResubmitDeadLetterCallCount);
    }

    [Fact]
    public async Task ResubmitDeadLetterAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()) { ThrowOnResubmit = new InvalidOperationException("service bus unavailable") };
        var (profile, demo, factory, nsId) = Build(faulty);
        var req = new ServiceBusEndpoints.ResubmitRequest { SequenceNumbers = ["4410"] };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceBusEndpoints.ResubmitDeadLetterAsync(nsId.ToString(), EntityPath, req, profile, factory, demo, CancellationToken.None));
        Assert.Equal("service bus unavailable", ex.Message);
        Assert.Equal(1, faulty.ResubmitDeadLetterCallCount); // still called exactly once even though it threw
    }
}
