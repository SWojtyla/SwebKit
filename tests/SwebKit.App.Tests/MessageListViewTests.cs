using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using System.Collections.Concurrent;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
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

        var uiState = new UiStateRepository();
        Services.AddSingleton(new AppStateService(new ProfileRepository(), uiState, new AppEventBus(NullLogger<AppEventBus>.Instance)));
        Services.AddSingleton(uiState);
        Services.AddSingleton<ITaskQueue>(new TaskQueueService());
        Services.AddSingleton<INotificationService>(new NotificationService(uiState));
    }

    [Fact]
    public void ComposePanel_UsesActiveEntityPath()
    {
        var client = new FakeServiceBusClient();

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.ShowCompose, true));

        cut.Find("[title='Compose and send a new message']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Compose →", cut.Markup);
            Assert.Contains("orders", cut.Markup);
            Assert.DoesNotContain("Compose → EntityPath", cut.Markup, StringComparison.Ordinal);
        });
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
    public void LoadMore_IncreasesLoadedWindow_WhenTotalIsLarger()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = Enumerable.Range(1, 12)
            .Select(i => new SbMessage
            {
                MessageId = $"msg-{i:000}",
                Body = "{}",
                EnqueuedAt = now.AddSeconds(i),
                SequenceNumber = 9000 + i
            })
            .ToList();

        var client = new FakeServiceBusClient(
            statsResolver: _ => new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 },
            peekResolver: _ => messages,
            dlqResolver: _ => []);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.Find("[data-testid='peek-count-select']").Change("5");
        cut.Find("[data-testid='peek-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Showing 5 of 12 message(s)", cut.Markup);
            Assert.Contains("Window 5/12 loaded", cut.Markup);
        });

        cut.Find("[data-testid='load-more-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Showing 10 of 12 message(s)", cut.Markup);
            Assert.Contains("Window 10/12 loaded", cut.Markup);
        });

        Assert.Contains(client.PeekRequests,
            req => req.EntityPath == "orders" && req.Count == 5 && req.FromSequenceNumber == null && !req.DeadLetter);
        Assert.Contains(client.PeekRequests,
            req => req.EntityPath == "orders" && req.Count == 5 && req.FromSequenceNumber == 9006 && !req.DeadLetter);
    }

    [Fact]
    public void LoadMore_Disabled_WhenAllMessagesAreAlreadyLoaded()
    {
        var client = new FakeServiceBusClient(
            messages:
            [
                new SbMessage { MessageId = "msg-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
                new SbMessage { MessageId = "msg-002", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
                new SbMessage { MessageId = "msg-003", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow }
            ],
            stats: new SbEntityStats { ActiveMessageCount = 3, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Showing 3 message(s)", cut.Markup);
            Assert.Contains("Window 3/3 loaded", cut.Markup);
            Assert.Contains("All Loaded", cut.Markup);
        });

        var loadMoreButton = cut.Find("[data-testid='load-more-button']");
        Assert.NotNull(loadMoreButton.GetAttribute("disabled"));
    }

    [Fact]
    public void LoadMore_PreservesFilterAndSelectionContinuity()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = Enumerable.Range(1, 12)
            .Select(i => new SbMessage
            {
                MessageId = $"track-{i:000}",
                Body = i % 2 == 0 ? "target payload" : "other payload",
                EnqueuedAt = now.AddMinutes(i),
                SequenceNumber = 10000 + i
            })
            .ToList();

        List<SbMessage> selectedBatch = [];
        var onBatchChanged = EventCallback.Factory.Create<List<SbMessage>>(this, batch => selectedBatch = batch);

        var client = new FakeServiceBusClient(
            statsResolver: _ => new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 },
            peekResolver: _ => messages,
            dlqResolver: _ => []);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false)
            .Add(p => p.MultiSelect, true)
            .Add(p => p.OnMultiSelectionChanged, onBatchChanged));

        cut.Find("[data-testid='peek-count-select']").Change("5");
        cut.Find("[data-testid='peek-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Showing 5 of 12 message(s)", cut.Markup));

        cut.Find("input[placeholder='Filter messages...']").Input("track-001");

        cut.WaitForAssertion(() => Assert.Contains("Showing 1 filtered of 5 loaded", cut.Markup));

        cut.Find("[title='Select / deselect all visible messages']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(5, selectedBatch.Count);
            Assert.Contains("5 selected", cut.Markup);
        });

        cut.Find("[data-testid='load-more-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Showing 1 filtered of 10 loaded", cut.Markup);
            Assert.Contains("5 selected", cut.Markup);
            Assert.Contains("track-001", cut.Markup);
        });
    }

    [Fact]
    public void LoadMore_StillAvailable_WhenActiveFilterMatchesNoLoadedMessages()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = Enumerable.Range(1, 12)
            .Select(i => new SbMessage
            {
                MessageId = $"evt-{i:000}",
                Body = i == 7 ? "needle payload" : "other payload",
                EnqueuedAt = now.AddSeconds(i),
                SequenceNumber = 9000 + i
            })
            .ToList();

        var client = new FakeServiceBusClient(
            statsResolver: _ => new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 },
            peekResolver: _ => messages,
            dlqResolver: _ => []);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.Find("[data-testid='peek-count-select']").Change("5");
        cut.Find("[data-testid='peek-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Showing 5 of 12 message(s)", cut.Markup));

        // The active filter matches nothing in the currently loaded window — the empty state
        // must not hide the Load More affordance, otherwise there's no way to reach a match
        // without first clearing the filter.
        cut.Find("input[placeholder='Filter messages...']").Input("needle");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No matches", cut.Markup);
            Assert.Contains("Load More below", cut.Markup);
        });

        var loadMoreButton = cut.Find("[data-testid='load-more-button']");
        Assert.Null(loadMoreButton.GetAttribute("disabled"));

        loadMoreButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("evt-007", cut.Markup);
            Assert.Contains("Showing 1 filtered of 10 loaded", cut.Markup);
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

        cut.WaitForAssertion(() => Assert.Contains("Loading", cut.Markup));

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
            Assert.NotEmpty(cut.FindAll(".cell-truncate"));
            Assert.Contains("very-long-message-id-1234567890", cut.Markup);
            Assert.Contains("corr-1234567890", cut.Markup);
        });
    }

    [Fact]
    public void DeleteSelected_ActiveMode_CompletesMessageAndReloadsList()
    {
        var active = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] =
            [
                new SbMessage { MessageId = "active-001", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 501 },
                new SbMessage { MessageId = "active-002", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 502 }
            ]
        };

        var dlq = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = []
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => new SbEntityStats
            {
                ActiveMessageCount = active[entityPath].Count,
                DeadLetterMessageCount = dlq[entityPath].Count
            },
            peekResolver: entityPath => active[entityPath].ToList(),
            dlqResolver: entityPath => dlq[entityPath].ToList(),
            completeResolver: (entityPath, sequenceNumbers) =>
            {
                var removed = active[entityPath].RemoveAll(m =>
                    m.SequenceNumber.HasValue && sequenceNumbers.Contains(m.SequenceNumber.Value));
                return removed;
            });

        var selectedMessage = active["orders"][0];

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false)
            .Add(p => p.SelectedMessage, selectedMessage));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[title='Delete selected active message']").Click();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Delete Message", cut.Markup);
            cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete").Click();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.CompleteMessagesCalls,
                call => call.EntityPath == "orders" && call.SequenceNumbers.SequenceEqual([501]));
            Assert.Contains("Deleted message 'active-001'.", cut.Markup);
            Assert.Contains("Showing 1 message(s)", cut.Markup);
        });

        Assert.True(client.PeekedEntityPaths.Count >= 2);
    }

    [Fact]
    public void PurgeAll_UsesDlqFlag_AndWaitsForConfirm()
    {
        var active = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = []
        };

        var dlq = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] =
            [
                new SbMessage { MessageId = "dead-101", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 8101 },
                new SbMessage { MessageId = "dead-102", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 8102 }
            ]
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => new SbEntityStats
            {
                ActiveMessageCount = active[entityPath].Count,
                DeadLetterMessageCount = dlq[entityPath].Count
            },
            peekResolver: entityPath => active[entityPath].ToList(),
            dlqResolver: entityPath => dlq[entityPath].ToList(),
            purgeResolver: (entityPath, deadLetter) =>
            {
                var bucket = deadLetter ? dlq : active;
                var removed = bucket[entityPath].Count;
                bucket[entityPath].Clear();
                return removed;
            });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, true)
            .Add(p => p.ShowDlqColumns, true));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[title='Permanently delete all messages in the current mode']").Click();
        });

        Assert.Empty(client.PurgeMessagesCalls);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Purge DLQ Messages", cut.Markup);
            cut.FindAll("button").First(b => b.TextContent.Trim() == "Purge").Click();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.PurgeMessagesCalls,
                call => call.EntityPath == "orders" && call.DeadLetter);
            Assert.Contains("Purged 2 DLQ message(s).", cut.Markup);
            Assert.Contains("No messages", cut.Markup);
        });
    }

    [Fact]
    public async Task AdvancedFiltering_AppliesApplicationPropertyNumericAndDateRules()
    {
        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "msg-eu-low-early",
                Body = "{}",
                EnqueuedAt = new DateTimeOffset(2026, 3, 28, 9, 0, 0, TimeSpan.Zero),
                DeliveryCount = 2,
                SequenceNumber = 1101,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu-west" }
            },
            new()
            {
                MessageId = "msg-us-high-late",
                Body = "{}",
                EnqueuedAt = new DateTimeOffset(2026, 3, 28, 13, 0, 0, TimeSpan.Zero),
                DeliveryCount = 7,
                SequenceNumber = 1102,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "us-east" }
            },
            new()
            {
                MessageId = "msg-eu-high-late",
                Body = "{}",
                EnqueuedAt = new DateTimeOffset(2026, 3, 28, 14, 0, 0, TimeSpan.Zero),
                DeliveryCount = 8,
                SequenceNumber = 1103,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu-central" }
            }
        };

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("msg-eu-high-late", cut.Markup));

        cut.Find("[data-testid='advanced-toggle']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='advanced-filter-panel']")));

        await InputRuleValueAsync(cut, 0, "[data-testid='rule-property']", "region");
        await InputRuleValueAsync(cut, 0, "[data-testid='rule-value']", "eu");

        cut.Find("[data-testid='add-rule']").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='advanced-rule']").Count));
        await ChangeRuleValueAsync(cut, 1, "[data-testid='rule-field']", "delivery-count");
        await ChangeRuleValueAsync(cut, 1, "[data-testid='rule-operator']", "gte");
        await InputRuleValueAsync(cut, 1, "[data-testid='rule-value']", "5");

        cut.Find("[data-testid='add-rule']").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid='advanced-rule']").Count));
        await ChangeRuleValueAsync(cut, 2, "[data-testid='rule-field']", "enqueued-time");
        await ChangeRuleValueAsync(cut, 2, "[data-testid='rule-operator']", "on-or-after");
        await InputRuleValueAsync(cut, 2, "[data-testid='rule-value']", "2026-03-28T12:00:00Z");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("msg-eu-high-late", cut.Markup);
            Assert.DoesNotContain("msg-eu-low-early", cut.Markup);
            Assert.DoesNotContain("msg-us-high-late", cut.Markup);
            Assert.Contains("Showing 1 filtered of 3 loaded", cut.Markup);
        });

        static Task InputRuleValueAsync(IRenderedComponent<MessageListView> cut, int ruleIndex, string selector, string value)
            => cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[ruleIndex]
                .QuerySelector(selector)!
                .Input(value));

        static Task ChangeRuleValueAsync(IRenderedComponent<MessageListView> cut, int ruleIndex, string selector, string value)
            => cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[ruleIndex]
                .QuerySelector(selector)!
                .Change(value));
    }

    [Fact]
    public async Task SavedFilterApply_RestoresAdvancedCriteria_AndLegacyTextOnly()
    {
        var uiState = Services.GetRequiredService<UiStateRepository>();
        var namespaceId = Guid.NewGuid();
        var scopeKey = $"{namespaceId}:orders";

        // Legacy format seeded directly: name + value only.
        await uiState.SaveFilterAsync(scopeKey, new SavedFilter
        {
            Name = "Legacy Text",
            Value = "legacy-only"
        });

        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "invoice-eu-001",
                Body = "invoice body",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 7,
                SequenceNumber = 2101,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu" }
            },
            new()
            {
                MessageId = "invoice-us-002",
                Body = "invoice body",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 9,
                SequenceNumber = 2102,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "us" }
            },
            new()
            {
                MessageId = "legacy-only-003",
                Body = "legacy-only payload",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                SequenceNumber = 2103,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "us" }
            }
        };

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.NamespaceId, namespaceId)
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("invoice-eu-001", cut.Markup));

        // Roundtrip: configure advanced criteria in the UI, save them, then re-apply via saved filters.
        await cut.InvokeAsync(() => cut.Find("[data-testid='advanced-toggle']").Click());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='advanced-rule']")));

        await cut.InvokeAsync(() => cut.Find("input[placeholder='Filter messages...']").Input("invoice"));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[0]
            .QuerySelector("[data-testid='rule-property']")!
            .Input("region"));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[0]
            .QuerySelector("[data-testid='rule-operator']")!
            .Change("equals"));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[0]
            .QuerySelector("[data-testid='rule-value']")!
            .Input("eu"));

        await cut.InvokeAsync(() => cut.Find("[data-testid='add-rule']").Click());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='advanced-rule']").Count));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[1]
            .QuerySelector("[data-testid='rule-field']")!
            .Change("delivery-count"));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[1]
            .QuerySelector("[data-testid='rule-operator']")!
            .Change("gte"));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='advanced-rule']")[1]
            .QuerySelector("[data-testid='rule-value']")!
            .Input("5"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("invoice-eu-001", cut.Markup);
            Assert.DoesNotContain("invoice-us-002", cut.Markup);
            Assert.DoesNotContain("legacy-only-003", cut.Markup);
        });

        await cut.InvokeAsync(() => cut.Find("button[title='Save current filter']").Click());
        cut.WaitForAssertion(() => Assert.Contains("Save Filter", cut.Markup));
        await cut.InvokeAsync(() => cut.Find("input[placeholder='Filter name…']").Change("EU Invoice High"));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click());

        cut.WaitForAssertion(() =>
        {
            var saved = uiState.GetFilters(scopeKey);
            var roundtrip = saved.FirstOrDefault(f => f.Name == "EU Invoice High");
            Assert.NotNull(roundtrip);
            Assert.Equal("invoice", roundtrip!.Value);
            Assert.True(roundtrip.FiltersEnabled);
            Assert.True(roundtrip.AdvancedFilterEnabled);
            Assert.Equal(2, roundtrip.AdvancedRules.Count);
        });

        // Reset UI state so applying the saved filter has observable effect.
        await cut.InvokeAsync(() => cut.Find("[data-testid='filters-toggle']").Click());
        cut.WaitForAssertion(() => Assert.Contains("Filters: Off", cut.Markup));
        await cut.InvokeAsync(() => cut.Find("input[placeholder='Filter messages...']").Input(string.Empty));

        await cut.InvokeAsync(() => cut.Find("button[title='Saved filters']").Click());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='saved-filter-item']")));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='saved-filter-item']")
            .First(i => i.GetAttribute("data-filter-name") == "EU Invoice High")
            .Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("invoice-eu-001", cut.Markup);
            Assert.DoesNotContain("invoice-us-002", cut.Markup);
            Assert.DoesNotContain("legacy-only-003", cut.Markup);
            Assert.NotEmpty(cut.FindAll("[data-testid='advanced-rule']"));
        });

        await cut.InvokeAsync(() => cut.Find("button[title='Saved filters']").Click());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='saved-filter-item']")));
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='saved-filter-item']")
            .First(i => i.GetAttribute("data-filter-name") == "Legacy Text")
            .Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("legacy-only-003", cut.Markup);
            Assert.DoesNotContain("invoice-eu-001", cut.Markup);
            Assert.DoesNotContain("invoice-us-002", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='advanced-filter-panel']"));
        });
    }

    [Fact]
    public void DeleteFiltered_ActiveMode_CompletesMatchingSequenceNumbers()
    {
        var active = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] =
            [
                new SbMessage { MessageId = "active-match-001", Body = "target payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 5101 },
                new SbMessage { MessageId = "active-match-002", Body = "target payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 5102 },
                new SbMessage { MessageId = "active-keep-003", Body = "keep payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 5103 }
            ]
        };

        var dlq = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = []
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => new SbEntityStats
            {
                ActiveMessageCount = active[entityPath].Count,
                DeadLetterMessageCount = dlq[entityPath].Count
            },
            peekResolver: entityPath => active[entityPath].ToList(),
            dlqResolver: entityPath => dlq[entityPath].ToList(),
            completeResolver: (entityPath, sequenceNumbers) =>
            {
                var removed = active[entityPath].RemoveAll(m =>
                    m.SequenceNumber is long seq && sequenceNumbers.Contains(seq));
                return removed;
            });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("active-match-001", cut.Markup));

        cut.Find("input[placeholder='Filter messages...']").Input("target");

        cut.WaitForAssertion(() => Assert.Contains("Showing 2 filtered of 3 loaded", cut.Markup));

        cut.Find("[title='Delete currently filtered messages in the current mode']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Delete Filtered Active Messages", cut.Markup);
            Assert.Contains("Delete 2 filtered message(s)", cut.Markup);
        });

        cut.Find("button.confirm-dialog-btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.CompleteMessagesCalls,
                call => call.EntityPath == "orders" && call.SequenceNumbers.SequenceEqual([5101, 5102]));
            Assert.Empty(client.CompleteDeadLetterCalls);
            Assert.Contains("Deleted 2 filtered active message(s).", cut.Markup);
        });

        Assert.True(client.PeekedEntityPaths.Count >= 2);
    }

    [Fact]
    public void DeleteFiltered_DlqMode_CompletesDeadLetterMatchingSequenceNumbers()
    {
        var active = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = []
        };

        var dlq = new Dictionary<string, List<SbMessage>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] =
            [
                new SbMessage { MessageId = "dlq-match-001", Body = "target payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 6101 },
                new SbMessage { MessageId = "dlq-keep-002", Body = "keep payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 6102 },
                new SbMessage { MessageId = "dlq-match-003", Body = "target payload", EnqueuedAt = DateTimeOffset.UtcNow, SequenceNumber = 6103 }
            ]
        };

        var client = new FakeServiceBusClient(
            statsResolver: entityPath => new SbEntityStats
            {
                ActiveMessageCount = active[entityPath].Count,
                DeadLetterMessageCount = dlq[entityPath].Count
            },
            peekResolver: entityPath => active[entityPath].ToList(),
            dlqResolver: entityPath => dlq[entityPath].ToList(),
            completeDeadLetterResolver: (entityPath, sequenceNumbers) =>
            {
                var sequenceSet = sequenceNumbers.ToHashSet(StringComparer.Ordinal);
                var removed = dlq[entityPath].RemoveAll(m =>
                    m.SequenceNumber is long seq && sequenceSet.Contains(seq.ToString()));
                return removed;
            });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, true)
            .Add(p => p.ShowDlqColumns, true));

        cut.WaitForAssertion(() => Assert.Contains("dlq-match-001", cut.Markup));

        cut.Find("input[placeholder='Filter messages...']").Input("target");

        cut.WaitForAssertion(() => Assert.Contains("Showing 2 filtered of 3 loaded", cut.Markup));

        cut.Find("[title='Delete currently filtered messages in the current mode']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Delete Filtered DLQ Messages", cut.Markup));

        cut.Find("button.confirm-dialog-btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(client.CompleteDeadLetterCalls,
                call => call.EntityPath == "orders" && call.SequenceNumbers.SequenceEqual(["6101", "6103"]));
            Assert.Empty(client.CompleteMessagesCalls);
            Assert.Contains("Deleted 2 filtered DLQ message(s).", cut.Markup);
        });

        Assert.True(client.PeekedEntityPaths.Count(path =>
            string.Equals(path, "dlq:orders", StringComparison.OrdinalIgnoreCase)) >= 2);
    }

    [Fact]
    public async Task ColumnChooser_ToggleBuiltInColumn_HidesAndRestoresSubjectValues()
    {
        var messages = new List<SbMessage>
        {
            new() { MessageId = "msg-001", Subject = "subject-visible-1", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, DeliveryCount = 1, SequenceNumber = 7001 },
            new() { MessageId = "msg-002", Subject = "subject-visible-2", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, DeliveryCount = 1, SequenceNumber = 7002 }
        };

        var client = new FakeServiceBusClient(messages: messages, stats: new SbEntityStats { ActiveMessageCount = 2 });
        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("subject-visible-1", cut.Markup));

        cut.Find("[data-testid='column-chooser-toggle']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='column-chooser-menu']")));

        await cut.InvokeAsync(() => cut.FindAll("[data-testid='built-in-column-option']")
            .First(option => option.GetAttribute("data-column-key") == "subject")
            .QuerySelector("input")!
            .Change(false));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("subject-visible-1", cut.Markup);
            Assert.DoesNotContain("subject-visible-2", cut.Markup);
        });

        await cut.InvokeAsync(() => cut.FindAll("[data-testid='built-in-column-option']")
            .First(option => option.GetAttribute("data-column-key") == "subject")
            .QuerySelector("input")!
            .Change(true));

        cut.WaitForAssertion(() => Assert.Contains("subject-visible-1", cut.Markup));
    }

    [Fact]
    public void ColumnChooser_CustomPropertyColumn_AddRemove_RendersExpectedValues()
    {
        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "msg-prop-001",
                Body = "{}",
                Subject = "subject",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                SequenceNumber = 7101,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu-west" }
            }
        };

        var client = new FakeServiceBusClient(messages: messages, stats: new SbEntityStats { ActiveMessageCount = 1 });
        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.DoesNotContain("eu-west", cut.Markup));

        cut.Find("[data-testid='column-chooser-toggle']").Click();
        cut.Find("[data-testid='custom-column-input']").Input("region");
        cut.Find("[data-testid='add-custom-column']").Click();

        cut.WaitForAssertion(() => Assert.Contains("eu-west", cut.Markup));

        cut.FindAll("[data-testid='remove-custom-column']")
            .First(button => button.GetAttribute("data-column-name") == "region")
            .Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("eu-west", cut.Markup));
    }

    [Fact]
    public async Task ColumnPreferences_PersistenceRestore_AppliesDensityBuiltInAndCustomColumns()
    {
        var namespaceId = Guid.NewGuid();
        var preferenceScope = $"{namespaceId}:orders:active";
        var uiState = Services.GetRequiredService<UiStateRepository>();

        await uiState.SaveMessageListPreferencesAsync(preferenceScope, new MessageListPreferences
        {
            RowDensity = "compact",
            BuiltInColumns = new Dictionary<string, bool> { ["subject"] = false },
            CustomPropertyColumns = ["region"]
        });

        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "persisted-001",
                Subject = "subject-hidden-by-preference",
                Body = "{}",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 3,
                SequenceNumber = 7201,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu-north" }
            }
        };

        var client = new FakeServiceBusClient(messages: messages, stats: new SbEntityStats { ActiveMessageCount = 1 });
        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.NamespaceId, namespaceId)
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("density-compact", cut.Find(".message-grid-host").ClassName);
            Assert.DoesNotContain("subject-hidden-by-preference", cut.Markup);
            Assert.Contains("eu-north", cut.Markup);
        });
    }

    [Fact]
    public void ColumnPreferences_Reset_RestoresDefaultsAndClearsCustomColumns()
    {
        var namespaceId = Guid.NewGuid();
        var uiState = Services.GetRequiredService<UiStateRepository>();

        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "reset-001",
                Subject = "subject-restored-after-reset",
                Body = "{}",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                SequenceNumber = 7301,
                ApplicationProperties = new Dictionary<string, object> { ["tenant"] = "tenant-a" }
            }
        };

        var client = new FakeServiceBusClient(messages: messages, stats: new SbEntityStats { ActiveMessageCount = 1 });
        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.NamespaceId, namespaceId)
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() => Assert.Contains("subject-restored-after-reset", cut.Markup));

        cut.Find("[data-testid='column-chooser-toggle']").Click();
        cut.FindAll("[data-testid='built-in-column-option']")
            .First(option => option.GetAttribute("data-column-key") == "subject")
            .QuerySelector("input")!
            .Change(false);
        cut.Find("[data-testid='custom-column-input']").Input("tenant");
        cut.Find("[data-testid='add-custom-column']").Click();
        cut.Find("button.density-btn").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("subject-restored-after-reset", cut.Markup);
            Assert.Contains("tenant-a", cut.Markup);
            Assert.Contains("density-compact", cut.Find(".message-grid-host").ClassName);
        });

        cut.Find("[data-testid='reset-column-preferences']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("subject-restored-after-reset", cut.Markup);
            Assert.DoesNotContain("tenant-a", cut.Markup);
            Assert.Contains("density-default", cut.Find(".message-grid-host").ClassName);
        });

        var preferenceScope = $"{namespaceId}:orders:active";
        var stored = uiState.GetMessageListPreferences(preferenceScope);
        Assert.Equal("default", stored.RowDensity);
        Assert.Empty(stored.BuiltInColumns);
        Assert.Empty(stored.CustomPropertyColumns);
    }

    [Fact]
    public void KeyboardNavigation_StillSelectsRows_AfterColumnCustomization()
    {
        SbMessage? selected = null;

        var messages = new List<SbMessage>
        {
            new()
            {
                MessageId = "keyboard-001",
                Subject = "keyboard-subject-1",
                Body = "{}",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                SequenceNumber = 7401,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "eu" }
            },
            new()
            {
                MessageId = "keyboard-002",
                Subject = "keyboard-subject-2",
                Body = "{}",
                EnqueuedAt = DateTimeOffset.UtcNow,
                DeliveryCount = 1,
                SequenceNumber = 7402,
                ApplicationProperties = new Dictionary<string, object> { ["region"] = "us" }
            }
        };

        var client = new FakeServiceBusClient(messages: messages, stats: new SbEntityStats { ActiveMessageCount = 2 });
        var onMessageSelected = EventCallback.Factory.Create<SbMessage?>(this, (SbMessage? msg) => selected = msg);
        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false)
            .Add(p => p.OnMessageSelected, onMessageSelected));

        cut.WaitForAssertion(() => Assert.Contains("keyboard-001", cut.Markup));

        cut.Find("[data-testid='column-chooser-toggle']").Click();
        cut.FindAll("[data-testid='built-in-column-option']")
            .First(option => option.GetAttribute("data-column-key") == "subject")
            .QuerySelector("input")!
            .Change(false);
        cut.Find("[data-testid='custom-column-input']").Input("region");
        cut.Find("[data-testid='add-custom-column']").Click();

        cut.Find(".message-grid-host").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => Assert.Equal("keyboard-001", selected?.MessageId));
    }

    [Fact]
    public void PinnedSessionId_ShowsBadge_WhenSet()
    {
        var client = new FakeServiceBusClient(
            messages: [],
            stats: new SbEntityStats { ActiveMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.PinnedSessionId, "sess-pinned-123"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='pinned-session-badge']"));
            Assert.Contains("sess-pinned-123", cut.Markup);
        });
    }

    [Fact]
    public void PinnedSessionId_NotShown_WhenNull()
    {
        var client = new FakeServiceBusClient(
            messages: [],
            stats: new SbEntityStats { ActiveMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.PinnedSessionId, null));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='pinned-session-badge']"));
        });
    }

    [Fact]
    public void PinnedSessionId_FiltersMessages_ToMatchingSession()
    {
        var messages = new List<SbMessage>
        {
            new() { MessageId = "m1", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SessionId = "sess-A" },
            new() { MessageId = "m2", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SessionId = "sess-B" },
            new() { MessageId = "m3", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow, SessionId = "sess-A" },
        };

        var client = new FakeServiceBusClient(
            statsResolver: _ => new SbEntityStats { ActiveMessageCount = messages.Count },
            peekResolver: _ => messages,
            dlqResolver: _ => []);

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.PinnedSessionId, "sess-A"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("m1", cut.Markup);
            Assert.Contains("m3", cut.Markup);
            Assert.DoesNotContain("m2", cut.Markup);
        });
    }

    // ── Wave 3: TracePivotFilter and large-window cue ─────────────────────

    [Fact]
    public void TracePivotFilter_WhenSet_AppliesTextFilter_AndFiltersMessages()
    {
        var messages = new List<SbMessage>
        {
            new SbMessage { MessageId = "corr-abc", CorrelationId = "abc-123", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
            new SbMessage { MessageId = "corr-xyz", CorrelationId = "xyz-999", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
        };

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false)
            .Add(p => p.TracePivotFilter, "abc-123"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("corr-abc", cut.Markup);
            Assert.DoesNotContain("corr-xyz", cut.Markup);
            Assert.Contains("Showing 1 filtered of 2 loaded", cut.Markup);
        });
    }

    [Fact]
    public void TracePivotFilter_WhenChangedToSameValue_DoesNotReapply()
    {
        // Verifies idempotency — a second render with the same pivot filter doesn't change state.
        var messages = new List<SbMessage>
        {
            new SbMessage { MessageId = "stable-001", CorrelationId = "same-value", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
            new SbMessage { MessageId = "stable-002", CorrelationId = "other-value", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow },
        };

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = messages.Count, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false)
            .Add(p => p.TracePivotFilter, "same-value"));

        cut.WaitForAssertion(() => Assert.Contains("Showing 1 filtered of 2 loaded", cut.Markup));

        // Manually change the filter input to something else
        cut.Find("input[placeholder='Filter messages...']").Input("other-value");
        cut.WaitForAssertion(() => Assert.Contains("Showing 1 filtered of 2 loaded", cut.Markup));

        // Re-render with the same TracePivotFilter — should not revert the manual change
        cut.SetParametersAndRender(ps => ps
            .Add(p => p.TracePivotFilter, "same-value"));

        // The manual filter should remain ("other-value" was set after the pivot applied)
        cut.WaitForAssertion(() => Assert.Contains("other-value", cut.Find("input[placeholder='Filter messages...']").GetAttribute("value") ?? ""));
    }

    [Fact]
    public void LargeWindowCue_ShowsWhenMessageCountExceedsThreshold()
    {
        var messages = Enumerable.Range(1, 200)
            .Select(i => new SbMessage { MessageId = $"msg-{i:000}", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow })
            .ToList();

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = 200, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        // Change PeekCount to 200 so all 200 messages are loaded in one peek
        cut.Find("[data-testid='peek-count-select']").Change("200");
        cut.Find("[data-testid='peek-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='large-window-cue']"));
            Assert.Contains("Large window", cut.Markup);
        });
    }

    [Fact]
    public void LargeWindowCue_HiddenWhenMessageCountBelowThreshold()
    {
        var messages = Enumerable.Range(1, 50)
            .Select(i => new SbMessage { MessageId = $"msg-{i:000}", Body = "{}", EnqueuedAt = DateTimeOffset.UtcNow })
            .ToList();

        var client = new FakeServiceBusClient(
            messages: messages,
            stats: new SbEntityStats { ActiveMessageCount = 50, DeadLetterMessageCount = 0 });

        var cut = RenderComponent<MessageListView>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.IsDlqMode, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='large-window-cue']"));
        });
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        private readonly Func<string, int, long?, IReadOnlyList<SbMessage>> _peekResolver;
        private readonly Func<string, int, long?, IReadOnlyList<SbMessage>> _dlqResolver;
        private readonly Func<string, SbEntityStats> _statsResolver;
        private readonly Func<string, Task>? _beforePeekAsync;
        private readonly Func<string, IReadOnlyList<long>, int>? _completeResolver;
        private readonly Func<string, IReadOnlyList<string>, int>? _completeDeadLetterResolver;
        private readonly Func<string, bool, int>? _purgeResolver;

        public ConcurrentQueue<string> PeekedEntityPaths { get; } = [];
        public List<(string EntityPath, int Count, bool DeadLetter, long? FromSequenceNumber)> PeekRequests { get; } = [];
        public List<(string EntityPath, IReadOnlyList<long> SequenceNumbers)> CompleteMessagesCalls { get; } = [];
        public List<(string EntityPath, IReadOnlyList<string> SequenceNumbers)> CompleteDeadLetterCalls { get; } = [];
        public List<(string EntityPath, bool DeadLetter)> PurgeMessagesCalls { get; } = [];

        public FakeServiceBusClient()
            : this([], new SbEntityStats())
        {
        }

        public FakeServiceBusClient(IReadOnlyList<SbMessage> messages, SbEntityStats stats)
            : this(_ => stats, _ => messages, _ => messages)
        {
        }

        public FakeServiceBusClient(
            Func<string, SbEntityStats> statsResolver,
            Func<string, IReadOnlyList<SbMessage>> peekResolver,
            Func<string, IReadOnlyList<SbMessage>> dlqResolver,
            Func<string, Task>? beforePeekAsync = null,
            Func<string, IReadOnlyList<long>, int>? completeResolver = null,
            Func<string, IReadOnlyList<string>, int>? completeDeadLetterResolver = null,
            Func<string, bool, int>? purgeResolver = null)
        {
            _statsResolver = statsResolver;
            _peekResolver = (entityPath, count, fromSequenceNumber) => Filter(peekResolver(entityPath), fromSequenceNumber).Take(count).ToList();
            _dlqResolver = (entityPath, count, fromSequenceNumber) => Filter(dlqResolver(entityPath), fromSequenceNumber).Take(count).ToList();
            _beforePeekAsync = beforePeekAsync;
            _completeResolver = completeResolver;
            _completeDeadLetterResolver = completeDeadLetterResolver;
            _purgeResolver = purgeResolver;
        }

        private static IEnumerable<SbMessage> Filter(IReadOnlyList<SbMessage> source, long? fromSequenceNumber) =>
            fromSequenceNumber is long seq ? source.Where(m => m.SequenceNumber >= seq) : source;

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            Task.FromResult(_statsResolver(entityPath));

        public async Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null)
        {
            PeekedEntityPaths.Enqueue(entityPath);
            PeekRequests.Add((entityPath, count, false, fromSequenceNumber));
            if (_beforePeekAsync is not null)
            {
                await _beforePeekAsync(entityPath);
            }

            return _peekResolver(entityPath, count, fromSequenceNumber);
        }

        public async Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null)
        {
            PeekedEntityPaths.Enqueue($"dlq:{entityPath}");
            PeekRequests.Add((entityPath, count, true, fromSequenceNumber));
            if (_beforePeekAsync is not null)
            {
                await _beforePeekAsync(entityPath);
            }

            return _dlqResolver(entityPath, count, fromSequenceNumber);
        }

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
        {
            CompleteMessagesCalls.Add((entityPath, sequenceNumbers.ToArray()));
            return Task.FromResult(_completeResolver?.Invoke(entityPath, sequenceNumbers) ?? 0);
        }

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default)
        {
            PurgeMessagesCalls.Add((entityPath, deadLetter));
            return Task.FromResult(_purgeResolver?.Invoke(entityPath, deadLetter) ?? 0);
        }

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            Task.FromResult(100_000L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default)
        {
            CompleteDeadLetterCalls.Add((entityPath, sequenceNumbers.ToArray()));
            _completeDeadLetterResolver?.Invoke(entityPath, sequenceNumbers);
            return Task.CompletedTask;
        }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
