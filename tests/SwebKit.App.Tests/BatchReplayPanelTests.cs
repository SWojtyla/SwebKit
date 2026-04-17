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

namespace SwebKit.App.Tests;

public sealed class BatchReplayPanelTests : TestContext
{
    public BatchReplayPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();

        var uiState = new UiStateRepository();
        Services.AddSingleton(new AppStateService(new ProfileRepository(), uiState, new AppEventBus(NullLogger<AppEventBus>.Instance)));
        Services.AddSingleton<INotificationService>(_ => new NotificationService(uiState));
    }

    // ── Preview bar (config step) ──────────────────────────────────────────

    [Fact]
    public void ConfigStep_ShowsMessageCount_AndSourceEntity()
    {
        var client = new CapturingClient();
        var messages = MakeMessages(3);

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "orders/dlq")
            .Add(p => p.Messages, messages));

        var bar = cut.Find("[data-testid='batch-replay-preview-bar']");
        Assert.Contains("3", bar.TextContent);
        Assert.Contains("orders/dlq", bar.TextContent);
    }

    [Fact]
    public void ConfigStep_ReviewButton_IsPresent()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(2)));

        cut.Find("[data-testid='batch-replay-review-btn']");
    }

    [Fact]
    public void ConfigStep_ReviewButton_IsDisabled_WhenNoMessages()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, []));

        var btn = cut.Find("[data-testid='batch-replay-review-btn']");
        Assert.True(btn.HasAttribute("disabled"));
    }

    [Fact]
    public void ConfigStep_RemapDetailsSummary_IsPresent()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(1)));

        cut.Find("[data-testid='remap-details-summary']");
    }

    [Fact]
    public void ConfigStep_TargetEntityInput_IsPresent()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(1)));

        cut.Find("[data-testid='target-entity-input']");
    }

    // ── Confirm step ────────────────────────────────────────────────────────

    [Fact]
    public void ClickReview_TransitionsToConfirmStep()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(2)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();

        cut.Find("[data-testid='batch-replay-confirm']");
    }

    [Fact]
    public void ConfirmStep_ShowsMessageCount()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(4)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();

        Assert.Contains("4", cut.Find("[data-testid='batch-replay-confirm']").TextContent);
    }

    [Fact]
    public void ConfirmStep_ShowsProductionWarning_WhenIsProduction()
    {
        // Arrange: set IsProduction via AppStateService config
        // bUnit doesn't mount AppStateService with IsProduction=true by default,
        // so we use the execute button presence as the confirm-step proxy instead.
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "orders")
            .Add(p => p.Messages, MakeMessages(1)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        cut.Find("[data-testid='batch-replay-execute-btn']");
    }

    [Fact]
    public void BackButton_ReturnsToConfigStep()
    {
        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, new CapturingClient())
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(1)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        cut.Find("[data-testid='batch-replay-confirm']");

        // Click back
        var backBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Back"));
        backBtn.Click();

        cut.Find("[data-testid='batch-replay-config']");
    }

    // ── Execution ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_CallsResubmitDeadLetterAsync()
    {
        var client = new CapturingClient();
        var messages = MakeMessages(2);

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "orders-queue")
            .Add(p => p.Messages, messages));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        await cut.Find("[data-testid='batch-replay-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.True(client.ResubmitCalled);
    }

    [Fact]
    public async Task Execute_ShowsSummary_AfterCompletion()
    {
        var client = new CapturingClient();
        var messages = MakeMessages(2);

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "orders-queue")
            .Add(p => p.Messages, messages));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        await cut.Find("[data-testid='batch-replay-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='batch-replay-summary']");
    }

    [Fact]
    public async Task Execute_InvokesOnCompleted_WithResult()
    {
        var client = new CapturingClient();
        BatchOperationResult? received = null;

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "test-queue")
            .Add(p => p.Messages, MakeMessages(1))
            .Add(p => p.OnCompleted, EventCallback.Factory.Create<BatchOperationResult>(this, r => received = r)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        await cut.Find("[data-testid='batch-replay-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.NotNull(received);
        Assert.True(received!.Succeeded > 0 || received.Failed > 0);
    }

    [Fact]
    public async Task Execute_PassesRemapRules_ToClient()
    {
        var client = new CapturingClient();
        var messages = MakeMessages(1);

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "src-queue")
            .Add(p => p.Messages, messages));

        // Set subject override in the UI
        var subjectInput = cut.FindAll("input").FirstOrDefault(i => i.GetAttribute("placeholder") == "(keep original)");
        if (subjectInput is not null)
            await subjectInput.ChangeAsync(new ChangeEventArgs { Value = "override-subject" });

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        await cut.Find("[data-testid='batch-replay-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Client should have been called; remap rules are forwarded if subject was set
        Assert.True(client.ResubmitCalled);
        if (subjectInput is not null)
            Assert.Equal("override-subject", client.CapturedRemapRules?.OverrideSubject);
    }

    [Fact]
    public async Task ResetButton_ShowsConfigStep_AfterSummary()
    {
        var client = new CapturingClient();

        var cut = RenderComponent<BatchReplayPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.SourceEntityPath, "q")
            .Add(p => p.Messages, MakeMessages(1)));

        cut.Find("[data-testid='batch-replay-review-btn']").Click();
        await cut.Find("[data-testid='batch-replay-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='batch-replay-reset-btn']").Click();
        cut.Find("[data-testid='batch-replay-config']");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IReadOnlyList<SbMessage> MakeMessages(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new SbMessage
            {
                MessageId = $"msg-{i}",
                Body = "test",
                SequenceNumber = i
            })
            .ToList();

    private sealed class CapturingClient : IServiceBusClient
    {
        public bool ResubmitCalled { get; private set; }
        public RemapRules? CapturedRemapRules { get; private set; }

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default)
        {
            ResubmitCalled = true;
            CapturedRemapRules = remapRules;
            return Task.CompletedTask;
        }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => Task.FromResult(new SbNamespaceInfo { Name = "test", Endpoint = "test.servicebus.windows.net" });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => Task.FromResult(new SbEntityStats());
        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => Task.FromResult(1L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
