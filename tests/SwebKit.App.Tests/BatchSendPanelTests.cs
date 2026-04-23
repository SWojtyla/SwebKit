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

public sealed class BatchSendPanelTests : TestContext
{
    public BatchSendPanelTests()
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

    // ── Import step ────────────────────────────────────────────────────────

    [Fact]
    public void ImportStep_IsShownInitially()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "orders-queue"));

        cut.Find("[data-testid='batch-send-import']");
    }

    [Fact]
    public void ImportStep_HasJsonTextarea_AndValidateButton()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "orders-queue"));

        cut.Find("[data-testid='batch-send-json-input']");
        cut.Find("[data-testid='batch-send-validate-btn']");
    }

    [Fact]
    public void ValidateButton_IsDisabled_WhenJsonInputIsEmpty()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient()));

        var btn = cut.Find("[data-testid='batch-send-validate-btn']");
        Assert.True(btn.HasAttribute("disabled"));
    }

    // ── Validation: valid JSON ─────────────────────────────────────────────

    [Fact]
    public async Task ValidJson_ShowsPreviewTable()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"hello","subject":"test"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        cut.Find("[data-testid='batch-send-preview']");
        cut.Find("[data-testid='batch-send-preview-table']");
    }

    [Fact]
    public async Task ValidJson_Shows_CorrectValidCount()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"a"},{"body":"b"},{"body":"c"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        var countEl = cut.Find("[data-testid='batch-send-valid-count']");
        Assert.Contains("3", countEl.TextContent);
    }

    // ── Validation: invalid JSON ───────────────────────────────────────────

    [Fact]
    public async Task InvalidJson_ShowsParseError()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = "not json at all"
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        cut.Find("[data-testid='batch-send-parse-error']");
    }

    [Fact]
    public async Task EntryWithNoBody_IsMarkedInvalid()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"subject":"no-body-here"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        var preview = cut.Find("[data-testid='batch-send-preview']");
        Assert.Contains("1", cut.Find("[data-testid='batch-send-invalid-count']").TextContent);
    }

    [Fact]
    public async Task MixedEntries_ShowsBoth_ValidAndInvalidCounts()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"ok"},{"subject":"missing-body"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        Assert.Contains("1", cut.Find("[data-testid='batch-send-valid-count']").TextContent);
        Assert.Contains("1", cut.Find("[data-testid='batch-send-invalid-count']").TextContent);
    }

    // ── Send execution ─────────────────────────────────────────────────────

    [Fact]
    public async Task Send_CallsSendBatchAsync_OnClient()
    {
        var client = new CapturingSendClient();

        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.DefaultEntityPath, "orders-queue"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"order-1"},{"body":"order-2"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        await cut.Find("[data-testid='batch-send-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.True(client.SendBatchCalled);
    }

    [Fact]
    public async Task Send_ShowsSummary_AfterCompletion()
    {
        var client = new CapturingSendClient();

        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"hello"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();
        await cut.Find("[data-testid='batch-send-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='batch-send-summary']");
    }

    [Fact]
    public async Task Send_InvokesOnCompleted_WithResult()
    {
        var client = new CapturingSendClient();
        BatchOperationResult? received = null;

        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.DefaultEntityPath, "q")
            .Add(p => p.OnCompleted, EventCallback.Factory.Create<BatchOperationResult>(this, r => received = r)));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"event"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();
        await cut.Find("[data-testid='batch-send-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.NotNull(received);
    }

    [Fact]
    public async Task ExecuteButton_IsDisabled_WhenAllEntriesInvalid()
    {
        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, new CapturingSendClient())
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"subject":"no-body"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();

        var executeBtn = cut.Find("[data-testid='batch-send-execute-btn']");
        Assert.True(executeBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ResetButton_ShowsImportStep_AfterSummary()
    {
        var client = new CapturingSendClient();

        var cut = RenderComponent<BatchSendPanel>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.DefaultEntityPath, "q"));

        var textarea = cut.Find("[data-testid='batch-send-json-input']");
        await textarea.ChangeAsync(new ChangeEventArgs
        {
            Value = """[{"body":"hi"}]"""
        });

        cut.Find("[data-testid='batch-send-validate-btn']").Click();
        await cut.Find("[data-testid='batch-send-execute-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='batch-send-reset-btn']").Click();
        cut.Find("[data-testid='batch-send-import']");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private sealed class CapturingSendClient : IServiceBusClient
    {
        public bool SendBatchCalled { get; private set; }

        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default)
        {
            SendBatchCalled = true;
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
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => Task.FromResult(1L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
