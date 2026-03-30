using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class EntityTreeTests : TestContext
{
    public EntityTreeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)));
    }

    [Fact]
    public void QueueRow_UsesCompactModeLabels_AndKeepsEntityNameVisible()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            var queueName = cut.Find(".entity-tree-name");
            Assert.Equal("orders", queueName.TextContent.Trim());
            Assert.Equal("orders", queueName.GetAttribute("title"));

            var activeButton = cut.Find(".entity-mode-btn.active");
            var dlqButton = cut.Find(".entity-mode-btn.dlq");

            Assert.Equal("A", activeButton.QuerySelector(".entity-mode-short")!.TextContent.Trim());
            Assert.Equal("42", activeButton.QuerySelector(".entity-mode-count")!.TextContent.Trim());
            Assert.Equal("D", dlqButton.QuerySelector(".entity-mode-short")!.TextContent.Trim());
            Assert.Equal("3", dlqButton.QuerySelector(".entity-mode-count")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ExplicitDlqButton_InvokesModeSelectionCallbackWithDlqTrue()
    {
        var client = new FakeServiceBusClient();
        (SbEntityInfo Entity, bool IsDlq)? selection = null;

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid())
            .Add(p => p.OnEntityModeSelected, value => selection = value));

        cut.WaitForAssertion(() =>
        {
            var dlqButton = cut.Find(".entity-mode-btn.dlq");
            dlqButton.Click();
        });

        Assert.NotNull(selection);
        Assert.True(selection!.Value.IsDlq);
        Assert.Equal("orders", selection.Value.Entity.EntityPath);
    }

    [Fact]
    public void RowClick_UsesDefaultActiveModeWhenModeCallbackProvided()
    {
        var client = new FakeServiceBusClient();
        (SbEntityInfo Entity, bool IsDlq)? selection = null;

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid())
            .Add(p => p.OnEntityModeSelected, value => selection = value));

        cut.WaitForAssertion(() =>
        {
            var queueRow = cut.FindAll(".entity-tree-item")
                .First(item => item.TextContent.Contains("orders", StringComparison.OrdinalIgnoreCase));
            queueRow.Click();
        });

        Assert.NotNull(selection);
        Assert.False(selection!.Value.IsDlq);
        Assert.Equal("orders", selection.Value.Entity.EntityPath);
    }

    [Fact]
    public void TopicRows_RenderPlainGlyphs_WithoutEncodedEntityArtifacts()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            var topicRow = cut.Find(".entity-tree-topic");
            Assert.Contains("bundle-1", topicRow.TextContent);
            Assert.Contains("▶", topicRow.TextContent);
            Assert.DoesNotContain("&#", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find(".entity-tree-topic").Click();

        cut.WaitForAssertion(() =>
        {
            var topicRow = cut.Find(".entity-tree-topic");
            Assert.Contains("▼", topicRow.TextContent);

            var subscriptionRow = cut.Find(".entity-tree-subscription");
            Assert.Contains("↳", subscriptionRow.TextContent);
            Assert.Contains("processor-a", subscriptionRow.TextContent);
            Assert.DoesNotContain("&#", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void QueueToggle_InvokesSetQueueEnabled_AndRefreshesStatusBadge()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            var queueRow = cut.FindAll(".entity-tree-item")
                .First(item => item.TextContent.Contains("orders", StringComparison.OrdinalIgnoreCase));

            Assert.Contains("Disabled", queueRow.TextContent, StringComparison.OrdinalIgnoreCase);
            queueRow.QuerySelector(".entity-toggle-btn")!.Click();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.QueueToggleCalls, call => call.QueueName == "orders" && call.Enabled);
            var queueRow = cut.FindAll(".entity-tree-item")
                .First(item => item.TextContent.Contains("orders", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Active", queueRow.TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TopicToggle_InvokesSetTopicEnabled()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            var topicRow = cut.Find(".entity-tree-topic");
            Assert.Contains("Disabled", topicRow.TextContent, StringComparison.OrdinalIgnoreCase);
            topicRow.QuerySelector(".entity-toggle-btn")!.Click();
        });

        Assert.Contains(client.TopicToggleCalls, call => call.TopicName == "bundle-1" && call.Enabled);
    }

    [Fact]
    public void SubscriptionToggle_InvokesSetSubscriptionEnabled()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.Find(".entity-tree-topic").Click();

        cut.WaitForAssertion(() =>
        {
            var subscriptionRow = cut.Find(".entity-tree-subscription");
            Assert.Contains("Disabled", subscriptionRow.TextContent, StringComparison.OrdinalIgnoreCase);
            subscriptionRow.QuerySelector(".entity-toggle-btn")!.Click();
        });

        Assert.Contains(client.SubscriptionToggleCalls,
            call => call.TopicName == "bundle-1" && call.SubscriptionName == "processor-a" && call.Enabled);
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        private readonly HashSet<string> _disabledEntities =
            ["orders", "bundle-1", "bundle-1/subscriptions/processor-a"];

        public List<(string QueueName, bool Enabled)> QueueToggleCalls { get; } = [];
        public List<(string TopicName, bool Enabled)> TopicToggleCalls { get; } = [];
        public List<(string TopicName, string SubscriptionName, bool Enabled)> SubscriptionToggleCalls { get; } = [];

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "orders",
                    EntityPath = "orders",
                    IsDisabled = IsDisabled("orders"),
                    Stats = new SbEntityStats { ActiveMessageCount = 42, DeadLetterMessageCount = 3 }
                }
            ]);

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "bundle-1",
                    EntityPath = "bundle-1",
                    IsTopic = true,
                    IsDisabled = IsDisabled("bundle-1")
                }
            ]);

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "processor-a",
                    EntityPath = $"{topicName}/subscriptions/processor-a",
                    TopicName = topicName,
                    IsSubscription = true,
                    IsDisabled = IsDisabled($"{topicName}/subscriptions/processor-a"),
                    Stats = new SbEntityStats { ActiveMessageCount = 7, DeadLetterMessageCount = 1 }
                }
            ]);

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default)
        {
            QueueToggleCalls.Add((queueName, enabled));
            SetEntityEnabled(queueName, enabled);
            return Task.CompletedTask;
        }

        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default)
        {
            TopicToggleCalls.Add((topicName, enabled));
            SetEntityEnabled(topicName, enabled);
            return Task.CompletedTask;
        }

        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default)
        {
            SubscriptionToggleCalls.Add((topicName, subscriptionName, enabled));
            SetEntityEnabled($"{topicName}/subscriptions/{subscriptionName}", enabled);
            return Task.CompletedTask;
        }

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            Task.FromResult(new SbEntityStats());

        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            Task.FromResult(100_000L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);

        private bool IsDisabled(string entityPath) => _disabledEntities.Contains(entityPath);

        private void SetEntityEnabled(string entityPath, bool enabled)
        {
            if (enabled)
            {
                _disabledEntities.Remove(entityPath);
            }
            else
            {
                _disabledEntities.Add(entityPath);
            }
        }
    }
}
