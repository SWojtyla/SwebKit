using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.WinUI.ViewModels.ServiceBus;
using SwebKit.WinUI.Views.ServiceBus;

namespace SwebKit.WinUI.Tests;

public sealed class ServiceBusPagePresentationTests
{
    [Fact]
    public void ComposeDialogState_TracksTemplateSummaryAndScheduledMode()
    {
        var sendState = ServiceBusPagePresentation.BuildComposeDialogState("orders", templateCount: 0, isScheduled: false);
        var scheduledState = ServiceBusPagePresentation.BuildComposeDialogState("orders", templateCount: 2, isScheduled: true);

        Assert.Equal("Target entity: orders", sendState.TargetEntityText);
        Assert.Equal("No saved templates yet.", sendState.TemplateSummaryText);
        Assert.Equal("Send", sendState.PrimaryButtonText);
        Assert.Equal(Visibility.Collapsed, sendState.SchedulePanelVisibility);

        Assert.Equal("2 saved template(s) available.", scheduledState.TemplateSummaryText);
        Assert.Equal("Schedule", scheduledState.PrimaryButtonText);
        Assert.Equal(Visibility.Visible, scheduledState.SchedulePanelVisibility);
    }

    [Fact]
    public void DeadLetterConfirmationRequests_UseSelectedMessageContext()
    {
        var tab = CreateTab(isDlq: true);
        tab.SelectedMessage = new SbMessage
        {
            MessageId = "msg-123",
            SequenceNumber = 42,
        };

        var resubmit = ServiceBusPagePresentation.BuildResubmitDeadLetterConfirmation(tab);
        var complete = ServiceBusPagePresentation.BuildCompleteDeadLetterConfirmation(tab);

        Assert.Equal("Resubmit dead-letter message", resubmit.Title);
        Assert.Contains("message 'msg-123'", resubmit.Message, StringComparison.Ordinal);
        Assert.Contains("orders", resubmit.Message, StringComparison.Ordinal);
        Assert.Equal("Resubmit", resubmit.PrimaryButtonText);
        Assert.Equal("Resubmit failed", resubmit.FailureTitle);

        Assert.Equal("Complete dead-letter message", complete.Title);
        Assert.Contains("message 'msg-123'", complete.Message, StringComparison.Ordinal);
        Assert.Contains("This removes it from the dead-letter queue.", complete.Message, StringComparison.Ordinal);
        Assert.Equal("Complete", complete.PrimaryButtonText);
        Assert.Equal("Complete failed", complete.FailureTitle);
    }

    [Fact]
    public void ScheduledConfirmationRequests_FallBackToSequenceNumberAndExplainLocalRemoval()
    {
        var scheduledMessage = CreateScheduledMessage(messageId: string.Empty, sequenceNumber: 77);

        var cancel = ServiceBusPagePresentation.BuildCancelScheduledConfirmation(scheduledMessage);
        var remove = ServiceBusPagePresentation.BuildRemoveScheduledConfirmation(scheduledMessage);

        Assert.Contains("sequence #77", cancel.Message, StringComparison.Ordinal);
        Assert.Contains(scheduledMessage.EntityPath, cancel.Message, StringComparison.Ordinal);
        Assert.Equal("Cancel send", cancel.PrimaryButtonText);
        Assert.Equal("Scheduled cancel failed", cancel.FailureTitle);

        Assert.Contains("sequence #77", remove.Message, StringComparison.Ordinal);
        Assert.Contains("This only clears the saved workspace entry.", remove.Message, StringComparison.Ordinal);
        Assert.Equal("Remove local entry", remove.PrimaryButtonText);
        Assert.Equal("Local removal failed", remove.FailureTitle);
    }

    [Fact]
    public void ServiceBusPageXaml_WiresScheduledWorkspaceRenderPath()
    {
        var xaml = File.ReadAllText(GetRepoFile("src", "SwebKit.WinUI", "Views", "ServiceBus", "ServiceBusPage.xaml"));

        Assert.Contains("Visibility=\"{Binding ActiveTab.IsScheduled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ActiveTab.ScheduledMessages}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding ActiveTab.SelectedScheduledMessage, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CancelScheduledMessage_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"RemoveScheduledMessage_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ActiveTab.SelectedScheduledMessageId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ActiveTab.SelectedScheduledStatus}\"", xaml, StringComparison.Ordinal);
    }

    private static ServiceBusTabViewModel CreateTab(bool isDlq)
    {
        var namespaceItem = new ServiceBusNamespaceItemViewModel(
            new ServiceBusNamespace
            {
                Alias = "orders",
                FullyQualifiedNamespace = "orders.servicebus.windows.net",
                CredentialKey = "cred-1",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            Client = new TestServiceBusClient(),
        };

        return new ServiceBusTabViewModel(
            namespaceItem,
            new SbEntityInfo
            {
                Name = "orders",
                EntityPath = "orders",
            },
            isDlq,
            new UiStateRepository(),
            pageSize: 50);
    }

    private static ScheduledMessageItemViewModel CreateScheduledMessage(string messageId, long sequenceNumber)
    {
        var scheduledTime = DateTimeOffset.Now.AddHours(2);
        return new ScheduledMessageItemViewModel(new ScheduledMessageEntry
        {
            NamespaceId = Guid.NewGuid(),
            EntityPath = "orders",
            SequenceNumber = sequenceNumber,
            ScheduledEnqueueTime = scheduledTime,
            MessageId = messageId,
            Subject = "Scheduled order",
            CorrelationId = "corr-123",
            CreatedAt = scheduledTime.AddMinutes(-5),
        });
    }

    private static string GetRepoFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SwebKit.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, Path.Combine(relativeSegments));
    }

    private sealed class TestServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}