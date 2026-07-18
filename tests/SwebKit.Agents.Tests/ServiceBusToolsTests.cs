using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ServiceBusToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static ServiceBusNamespace Namespace() => new()
    {
        Id = Guid.NewGuid(),
        Alias = "orders-dev",
        FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
        CredentialKey = "sb:orders-dev",
    };

    private static Mock<ICredentialStore> CredStore(string? connectionString)
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.Get(It.IsAny<string>())).Returns(connectionString);
        return store;
    }

    private static (Mock<IServiceBusClientFactory> factory, Mock<IServiceBusClient> client) MakeSb()
    {
        var client = new Mock<IServiceBusClient>();
        var factory = new Mock<IServiceBusClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<SbTransportType>())).Returns(client.Object);
        return (factory, client);
    }

    // ── GetQueueStatsTool ─────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueStats_NoNamespaceConfigured_ReturnsError()
    {
        var (factory, _) = MakeSb();
        var tool = new GetQueueStatsTool(factory.Object, TestSupport.CreateAppState(), CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not configured", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetQueueStats_MissingConnectionString_ReturnsError()
    {
        var (factory, _) = MakeSb();
        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueStatsTool(factory.Object, appState, CredStore(null).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("connection string not available", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetQueueStats_SpecificQueue_ReturnsStats()
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.GetEntityStatsAsync("queues/orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SbEntityStats { ActiveMessageCount = 5, DeadLetterMessageCount = 1, ScheduledMessageCount = 0, TransferCount = 0 });

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueStatsTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("orders", doc.RootElement.GetProperty("queue_name").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("active_message_count").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("dead_letter_message_count").GetInt64());
    }

    [Fact]
    public async Task GetQueueStats_AllQueues_ReturnsStatsPerQueue()
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.ListQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SbEntityInfo { Name = "orders", EntityPath = "queues/orders" }]);
        client.Setup(c => c.GetEntityStatsAsync("queues/orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SbEntityStats { ActiveMessageCount = 3 });

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueStatsTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("queue_count").GetInt32());
        var queue = doc.RootElement.GetProperty("queues").EnumerateArray().First();
        Assert.Equal("orders", queue.GetProperty("queue_name").GetString());
        Assert.Equal(3, queue.GetProperty("active_message_count").GetInt64());
    }

    [Fact]
    public async Task GetQueueStats_DemoMode_ReturnsDemoNamespaceStats()
    {
        var (factory, _) = MakeSb();
        var appState = TestSupport.CreateAppState();
        await appState.SetDemoModeAsync(true);
        var tool = new GetQueueStatsTool(factory.Object, appState, CredStore(null).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("demo", doc.RootElement.GetProperty("namespace_alias").GetString());
        Assert.True(doc.RootElement.GetProperty("queue_count").GetInt32() > 0);
    }

    // ── GetQueueMessagesTool ──────────────────────────────────────────────

    [Fact]
    public async Task GetQueueMessages_PeeksActiveMessages()
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.PeekMessagesAsync("queues/orders", 10, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync([new SbMessage { MessageId = "m1", Body = "hello", EnqueuedAt = DateTimeOffset.UtcNow }]);

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueMessagesTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("peek_dead_letter").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("messages_returned").GetInt32());
        Assert.Equal("m1", doc.RootElement.GetProperty("messages").EnumerateArray().First().GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task GetQueueMessages_PeekDeadLetter_UsesDeadLetterApi()
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.PeekDeadLetterAsync("queues/orders", 3, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync([new SbMessage { MessageId = "dl1", Body = "poison", EnqueuedAt = DateTimeOffset.UtcNow, DeadLetterReason = "MaxDeliveryCountExceeded" }]);

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueMessagesTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders", "count": 3, "peek_dead_letter": true }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("peek_dead_letter").GetBoolean());
        client.Verify(c => c.PeekDeadLetterAsync("queues/orders", 3, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task GetQueueMessages_ClientThrows_ReturnsError()
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.PeekMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new GetQueueMessagesTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("boom", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetQueueMessages_DemoMode_ReturnsDemoMessages()
    {
        var (factory, _) = MakeSb();
        var appState = TestSupport.CreateAppState();
        await appState.SetDemoModeAsync(true);
        var tool = new GetQueueMessagesTool(factory.Object, appState, CredStore(null).Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "order-created" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("demo", doc.RootElement.GetProperty("namespace_alias").GetString());
        Assert.Equal("order-created", doc.RootElement.GetProperty("queue_name").GetString());
    }

    // ── AnalyzeQueueHealthTool ────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, "Healthy")]
    [InlineData(200, 0, "Warning")]
    [InlineData(0, 4, "Critical")]
    [InlineData(2000, 0, "Critical")]
    public async Task AnalyzeQueueHealth_ComputesHealthSummary(long active, long deadLetter, string expected)
    {
        var (factory, client) = MakeSb();
        client.Setup(c => c.GetEntityStatsAsync("queues/orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SbEntityStats { ActiveMessageCount = active, DeadLetterMessageCount = deadLetter });
        client.Setup(c => c.PeekDeadLetterAsync("queues/orders", 10, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync([]);

        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [Namespace()]);
        var tool = new AnalyzeQueueHealthTool(factory.Object, appState, CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(expected, doc.RootElement.GetProperty("health_summary").GetString());
    }

    [Fact]
    public async Task AnalyzeQueueHealth_NoNamespace_ReturnsError()
    {
        var (factory, _) = MakeSb();
        var tool = new AnalyzeQueueHealthTool(factory.Object, TestSupport.CreateAppState(), CredStore("conn").Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "orders" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Contains("not configured", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnalyzeQueueHealth_DemoMode_ReturnsDemoNamespace()
    {
        var (factory, _) = MakeSb();
        var appState = TestSupport.CreateAppState();
        await appState.SetDemoModeAsync(true);
        var tool = new AnalyzeQueueHealthTool(factory.Object, appState, CredStore(null).Object);

        var result = await tool.ExecuteAsync(Args("""{ "queue_name": "order-created" }"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("demo", doc.RootElement.GetProperty("namespace_alias").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("health_summary").GetString()));
    }
}
