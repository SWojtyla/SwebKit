using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
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

[Collection("AppDataSerial")]
public sealed class MessageComposerTests : TestContext
{
    public MessageComposerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)));
        Services.AddSingleton<ITaskQueue>(new TaskQueueService());
        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));
    }

    [Fact]
    public void ComposeMode_ShowsComposeHeader()
    {
        var client = new StubServiceBusClient();
        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Compose));

        Assert.Contains("Compose →", cut.Markup);
        Assert.Contains("orders", cut.Markup);
    }

    [Fact]
    public void PrefillMessage_PopulatesFields()
    {
        var prefill = new SbMessage
        {
            MessageId = "orig-id",
            CorrelationId = "corr-123",
            Subject = "test-subject",
            Body = "hello prefilled body",
            ContentType = "application/json"
        };

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Edit)
            .Add(p => p.PrefillMessage, prefill));

        // Subject, correlation and body fields should be prefilled
        Assert.Contains("corr-123", cut.Markup);
        Assert.Contains("test-subject", cut.Markup);
        Assert.Contains("hello prefilled body", cut.Markup);
    }

    [Fact]
    public void ScheduleMode_ShowsDatetimeLocalInput()
    {
        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Schedule));

        Assert.NotNull(cut.Find("input[type='datetime-local']"));
        Assert.Contains("Schedule →", cut.Markup);
    }

    [Fact]
    public void ReplayMode_ShowsTargetNamespaceDropdown_WhenNamespacesProvided()
    {
        var ns = new MessageComposer.NamespaceOption(Guid.NewGuid(), "Production", new StubServiceBusClient());

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Replay)
            .Add(p => p.AvailableNamespaces, [ns]));

        Assert.Contains("Replay →", cut.Markup);
        Assert.Contains("Production", cut.Markup);
        // namespace select has the option
        var select = cut.Find("select");
        Assert.NotNull(select);
        Assert.Contains("Production", select.TextContent);
    }

    [Fact]
    public void ReplayMode_ShowsRemapRulesDetails()
    {
        var ns = new MessageComposer.NamespaceOption(Guid.NewGuid(), "Production", new StubServiceBusClient());

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Replay)
            .Add(p => p.AvailableNamespaces, [ns]));

        Assert.NotNull(cut.Find("details"));
        Assert.Contains("Remap rules", cut.Markup);
    }

    [Fact]
    public void EditMode_ShowsResubmitLabel()
    {
        var prefill = new SbMessage
        {
            MessageId = "msg-1",
            Body = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Edit)
            .Add(p => p.PrefillMessage, prefill));

        Assert.Contains("Resubmit", cut.Markup);
    }

    [Fact]
    public void PrefillMessage_ChangingReference_ReprefillsFields()
    {
        var first = new SbMessage { MessageId = "first", CorrelationId = "corr-first", Body = "{}" };
        var second = new SbMessage { MessageId = "second", CorrelationId = "corr-second", Body = "{}" };

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Edit)
            .Add(p => p.PrefillMessage, first));

        Assert.Contains("corr-first", cut.Markup);

        cut.SetParametersAndRender(ps => ps
            .Add(p => p.PrefillMessage, second));

        Assert.Contains("corr-second", cut.Markup);
        Assert.DoesNotContain("corr-first", cut.Markup);
    }

    [Fact]
    public async Task SaveAsTemplate_PersistsTemplateInAppState()
    {
        using var sandbox = new AppDataSandbox();

        var appState = Services.GetRequiredService<AppStateService>();
        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, new StubServiceBusClient())
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Compose));

        cut.Find("[data-testid='composer-body-input']").Change("{\"orderId\":42}");
        cut.Find("[data-testid='open-save-template-button']").Click();
        cut.Find("[data-testid='save-template-name-input']").Change("Order Template");
        cut.Find("[data-testid='save-template-confirm-button']").Click();

        cut.WaitForAssertion(() =>
        {
            var template = Assert.Single(appState.MessageTemplates);
            Assert.Equal("Order Template", template.Name);
            Assert.Equal("{\"orderId\":42}", template.Body);
        });

        await Task.CompletedTask;
    }

    [Fact]
    public async Task LoadTemplate_ApplyAndSend_InvokesCallback_AndUsesTemplateFields()
    {
        using var sandbox = new AppDataSandbox();

        var appState = Services.GetRequiredService<AppStateService>();
        await appState.SaveMessageTemplateAsync(new SwebKit.Core.Domain.SbMessageTemplate
        {
            Name = "Billing Template",
            Body = "{\"invoiceId\":9001}",
            ContentType = "application/json",
            Subject = "invoice.created",
            CorrelationId = "corr-bill-1",
            Properties = new Dictionary<string, string>
            {
                ["source"] = "template"
            }
        });

        var client = new StubServiceBusClient();
        var callbackInvoked = false;
        ScheduledMessageEntry? callbackPayload = new()
        {
            NamespaceId = Guid.NewGuid(),
            EntityPath = "sentinel",
            SequenceNumber = -1,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow
        };

        var cut = RenderComponent<MessageComposer>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.EntityPath, "orders")
            .Add(p => p.Mode, MessageComposer.ComposerMode.Compose)
            .Add(p => p.OnSent, EventCallback.Factory.Create<ScheduledMessageEntry?>(this, entry =>
            {
                callbackInvoked = true;
                callbackPayload = entry;
            })));

        cut.Find("[data-testid='load-template-button']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Billing Template", cut.Markup));

        cut.Find("[data-testid='apply-template-button']").Click();
        cut.WaitForAssertion(() => Assert.Contains("invoice.created", cut.Markup));

        cut.Find("[data-testid='composer-send-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, client.SendCalls);
            Assert.NotNull(client.LastSentMessage);
            Assert.Equal("orders", client.LastEntityPath);
            Assert.Equal("invoice.created", client.LastSentMessage!.Subject);
            Assert.Equal("corr-bill-1", client.LastSentMessage.CorrelationId);
            Assert.Equal("application/json", client.LastSentMessage.ContentType);
            Assert.Equal("{\"invoiceId\":9001}", client.LastSentMessage.Body);
            Assert.Equal("template", client.LastSentMessage.ApplicationProperties["source"]?.ToString());
            Assert.True(callbackInvoked);
            Assert.Null(callbackPayload);
        });
    }

    private sealed class StubServiceBusClient : IServiceBusClient
    {
        public int SendCalls { get; private set; }
        public string? LastEntityPath { get; private set; }
        public SbMessage? LastSentMessage { get; private set; }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
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
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default)
        {
            SendCalls++;
            LastEntityPath = entityPath;
            LastSentMessage = message;
            return Task.CompletedTask;
        }
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            Task.FromResult(999_001L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class AppDataSandbox : IDisposable
    {
        private readonly string? _originalAppData;
        private readonly string? _originalRootOverride;
        private readonly string _tempRoot;

        public AppDataSandbox()
        {
            _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            _originalRootOverride = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _originalRootOverride);
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
