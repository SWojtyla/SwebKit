using Microsoft.UI.Xaml;
using SwebKit.WinUI.ViewModels.ServiceBus;

namespace SwebKit.WinUI.Views.ServiceBus;

internal static class ServiceBusPagePresentation
{
    internal static ServiceBusComposeDialogState BuildComposeDialogState(string? entityPath, int templateCount, bool isScheduled, bool isReplay)
    {
        return new ServiceBusComposeDialogState(
            isReplay ? "Replay message" : "Compose message",
            $"{(isReplay ? "Replay target" : "Target entity")}: {entityPath ?? string.Empty}",
            templateCount == 0 ? "No saved templates yet." : $"{templateCount} saved template(s) available.",
            isReplay ? "Replay" : isScheduled ? "Schedule" : "Send",
            !isReplay && isScheduled ? Visibility.Visible : Visibility.Collapsed,
            isReplay ? Visibility.Visible : Visibility.Collapsed,
            isReplay ? Visibility.Collapsed : Visibility.Visible);
    }

    internal static ServiceBusConfirmationRequest BuildResubmitDeadLetterConfirmation(ServiceBusTabViewModel activeTab)
    {
        var selectedMessage = activeTab.SelectedMessage ?? throw new InvalidOperationException("A selected dead-letter message is required.");
        var sequenceNumber = selectedMessage.SequenceNumber ?? throw new InvalidOperationException("The selected dead-letter message must have a sequence number.");

        return new ServiceBusConfirmationRequest(
            "Resubmit dead-letter message",
            $"Resubmit {BuildMessageLabel(selectedMessage.MessageId, sequenceNumber)} from {activeTab.EntityPath} back to the active entity?",
            "Resubmit",
            "Resubmit failed");
    }

    internal static ServiceBusConfirmationRequest BuildCompleteDeadLetterConfirmation(ServiceBusTabViewModel activeTab)
    {
        var selectedMessage = activeTab.SelectedMessage ?? throw new InvalidOperationException("A selected dead-letter message is required.");
        var sequenceNumber = selectedMessage.SequenceNumber ?? throw new InvalidOperationException("The selected dead-letter message must have a sequence number.");

        return new ServiceBusConfirmationRequest(
            "Complete dead-letter message",
            $"Permanently complete {BuildMessageLabel(selectedMessage.MessageId, sequenceNumber)} from {activeTab.EntityPath}? This removes it from the dead-letter queue.",
            "Complete",
            "Complete failed");
    }

    internal static ServiceBusConfirmationRequest BuildCancelScheduledConfirmation(ScheduledMessageItemViewModel scheduledMessage)
    {
        return new ServiceBusConfirmationRequest(
            "Cancel scheduled message",
            $"Cancel {BuildMessageLabel(scheduledMessage.MessageId, scheduledMessage.SequenceNumber)} scheduled for {scheduledMessage.ScheduledEnqueueTimeText} on {scheduledMessage.EntityPath}?",
            "Cancel send",
            "Scheduled cancel failed");
    }

    internal static ServiceBusConfirmationRequest BuildRemoveScheduledConfirmation(ScheduledMessageItemViewModel scheduledMessage)
    {
        return new ServiceBusConfirmationRequest(
            "Remove local scheduled entry",
            $"Remove the local record for {BuildMessageLabel(scheduledMessage.MessageId, scheduledMessage.SequenceNumber)} on {scheduledMessage.EntityPath}? This only clears the saved workspace entry.",
            "Remove local entry",
            "Local removal failed");
    }

    internal static string BuildMessageLabel(string? messageId, long? sequenceNumber)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            return $"message '{messageId}'";
        }

        return sequenceNumber is long value
            ? $"sequence #{value}"
            : "the selected message";
    }
}

internal sealed record ServiceBusComposeDialogState(
    string DialogTitle,
    string TargetEntityText,
    string TemplateSummaryText,
    string PrimaryButtonText,
    Visibility SchedulePanelVisibility,
    Visibility ReplayPanelVisibility,
    Visibility ScheduleToggleVisibility);

internal sealed record ServiceBusConfirmationRequest(
    string Title,
    string Message,
    string PrimaryButtonText,
    string FailureTitle);