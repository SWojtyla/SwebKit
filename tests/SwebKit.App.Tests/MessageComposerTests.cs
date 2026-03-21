using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

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

    private sealed class StubServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            Task.FromResult(new SbEntityStats());
        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            Task.FromResult(999_001L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
