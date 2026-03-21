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
    public void QueueRow_ShowsActiveAndDlqCounts_AndExplicitModeButtons()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<EntityTree>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Active 42", cut.Markup);
            Assert.Contains("DLQ 3", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".entity-mode-btn.active"));
            Assert.NotEmpty(cut.FindAll(".entity-mode-btn.dlq"));
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

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "orders",
                    EntityPath = "orders",
                    Stats = new SbEntityStats { ActiveMessageCount = 42, DeadLetterMessageCount = 3 }
                }
            ]);

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "bundle-1",
                    EntityPath = "bundle-1"
                }
            ]);

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>(
            [
                new SbEntityInfo
                {
                    Name = "processor-a",
                    EntityPath = $"{topicName}/subscriptions/processor-a",
                    Stats = new SbEntityStats { ActiveMessageCount = 7, DeadLetterMessageCount = 1 }
                }
            ]);

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            Task.FromResult(new SbEntityStats());

        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

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
    }
}
