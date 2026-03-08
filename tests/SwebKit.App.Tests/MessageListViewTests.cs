using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using System.Collections.Concurrent;

namespace SwebKit.App.Tests;

public sealed class MessageListViewTests : TestContext
{
    public MessageListViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();

        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus()));
    }

    [Fact]
    public void DlqMode_ShowsModeBadge_AndShowingOfTotalSummary()
    {
        var client = new FakeServiceBusClient(
            messages: [
                new SbMessage { MessageId = "dead-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, DeadLetterReason = "MaxDeliveryCountExceeded" },
                new SbMessage { MessageId = "dead-002", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, DeadLetterReason = "MaxDeliveryCountExceeded" },
                new SbMessage { MessageId = "dead-003", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, DeadLetterReason = "MaxDeliveryCountExceeded" }
            ],
            stats: new SbEntityStats { DeadLetterMessageCount = 1483, ActiveMessageCount = 12 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, true)
            .Add(p => p.ShowDlqColumns, true));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".view-mode-badge.dlq"));
            Assert.Contains("DLQ mode", cut.Markup);
            Assert.Contains("Showing 3 of 1483 message(s)", cut.Markup);
        });
    }

    [Fact]
    public void PeekMode_ShowsPeekBadge_AndLoadedSummaryWhenTotalMatches()
    {
        var client = new FakeServiceBusClient(
            messages: [
                new SbMessage { MessageId = "msg-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
                new SbMessage { MessageId = "msg-002", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }
            ],
            stats: new SbEntityStats { ActiveMessageCount = 2, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".view-mode-badge.peek"));
            Assert.Contains("Peek mode", cut.Markup);
            Assert.Contains("Showing 2 message(s)", cut.Markup);
        });
    }

    [Fact]
    public void ChangingEntityPath_ReloadsMessagesForNewEntity()
    {
        var perEntityMessages = new Dictionary<string, IReadOnlyList<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = [new SbMessage { MessageId = "orders-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }],
            ["payments"] =
            [
                new SbMessage { MessageId = "payments-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
                new SbMessage { MessageId = "payments-002", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }
            ]
        };

        var perEntityStats = new Dictionary<string, SbEntityStats>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = new SbEntityStats { ActiveMessageCount = 1, DeadLetterMessageCount = 0 },
            ["payments"] = new SbEntityStats { ActiveMessageCount = 7, DeadLetterMessageCount = 0 }
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => perEntityStats[entityPath],
            peekResolver: entityPath => perEntityMessages[entityPath],
            dlqResolver: _ => []);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("orders-001", cut.Markup);
            Assert.Contains("Showing 1 message(s)", cut.Markup);
        });

        cut.SetParametersAndRender(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "payments")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("payments-001", cut.Markup);
            Assert.Contains("payments-002", cut.Markup);
            Assert.Contains("Showing 2 of 7 message(s)", cut.Markup);
            Assert.DoesNotContain("orders-001", cut.Markup);
        });

        Assert.Contains("orders", client.PeekedEntityPaths);
        Assert.Contains("payments", client.PeekedEntityPaths);
    }

    [Fact]
    public async Task NewerLoadWins_WhenOlderLoadCompletesLater()
    {
        var slowEntityGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var perEntityMessages = new Dictionary<string, IReadOnlyList<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["slow-queue"] = [new SbMessage { MessageId = "slow-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }],
            ["fast-queue"] = [new SbMessage { MessageId = "fast-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }]
        };

        var perEntityStats = new Dictionary<string, SbEntityStats>(StringComparer.OrdinalIgnoreCase)
        {
            ["slow-queue"] = new SbEntityStats { ActiveMessageCount = 1 },
            ["fast-queue"] = new SbEntityStats { ActiveMessageCount = 1 }
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => perEntityStats[entityPath],
            peekResolver: entityPath => perEntityMessages[entityPath],
            dlqResolver: _ => [],
            beforePeekAsync: entityPath => entityPath.Equals("slow-queue", StringComparison.OrdinalIgnoreCase)
                ? slowEntityGate.Task
                : Task.CompletedTask);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "slow-queue")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("Loading...", cut.Markup));

        cut.SetParametersAndRender(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "fast-queue")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("fast-001", cut.Markup));

        slowEntityGate.SetResult();
        await Task.Delay(25);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("fast-001", cut.Markup);
            Assert.DoesNotContain("slow-001", cut.Markup);
        });
    }

    [Fact]
    public void MessageGrid_UsesResponsiveCellAndScrollOwnerClasses_ForLayoutStability()
    {
        var client = new FakeServiceBusClient(
            messages:
            [
                new SbMessage
                {
                    MessageId = "very-long-message-id-1234567890",
                    CorrelationId = "corr-1234567890",
                    Subject = "A subject that should wrap for readability",
                    DeadLetterReason = "A long dead letter reason that should wrap",
                    Body = "{}",
                    EnqueuedAt = DateTimeOffset.UtcNow,
                    DeliveryCount = 2
                }
            ],
            stats: new SbEntityStats { DeadLetterMessageCount = 1, ActiveMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, true)
            .Add(p => p.ShowDlqColumns, true));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".message-list-host"));
            Assert.NotNull(cut.Find(".message-grid-scroll"));
            Assert.NotNull(cut.Find(".message-grid"));
            Assert.NotNull(cut.Find(".responsive-message-grid"));
            Assert.NotEmpty(cut.FindAll(".cell-truncate"));
            Assert.NotEmpty(cut.FindAll(".cell-wrap"));
            Assert.NotEmpty(cut.FindAll(".col-message-id"));
            Assert.NotEmpty(cut.FindAll(".col-correlation-id"));
            Assert.NotEmpty(cut.FindAll(".col-subject"));
            Assert.NotEmpty(cut.FindAll(".col-delivery"));
            Assert.NotEmpty(cut.FindAll(".col-dlq-reason"));
        });
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        private readonly Func<string, IReadOnlyList<SbMessage>> _peekResolver;
        private readonly Func<string, IReadOnlyList<SbMessage>> _dlqResolver;
        private readonly Func<string, SbEntityStats> _statsResolver;
        private readonly Func<string, Task>? _beforePeekAsync;

        public ConcurrentQueue<string> PeekedEntityPaths { get; } = [];

        public FakeServiceBusClient(IReadOnlyList<SbMessage> messages, SbEntityStats stats)
            : this(_ => stats, _ => messages, _ => messages)
        {
        }

        public FakeServiceBusClient(
            Func<string, SbEntityStats> statsResolver,
            Func<string, IReadOnlyList<SbMessage>> peekResolver,
            Func<string, IReadOnlyList<SbMessage>> dlqResolver,
            Func<string, Task>? beforePeekAsync = null)
        {
            _statsResolver = statsResolver;
            _peekResolver = peekResolver;
            _dlqResolver = dlqResolver;
            _beforePeekAsync = beforePeekAsync;
        }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            Task.FromResult(_statsResolver(entityPath));

        public async Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default)
        {
            PeekedEntityPaths.Enqueue(entityPath);
            if (_beforePeekAsync is not null)
            {
                await _beforePeekAsync(entityPath);
            }

            return _peekResolver(entityPath);
        }

        public async Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default)
        {
            PeekedEntityPaths.Enqueue($"dlq:{entityPath}");
            if (_beforePeekAsync is not null)
            {
                await _beforePeekAsync(entityPath);
            }

            return _dlqResolver(entityPath);
        }

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
